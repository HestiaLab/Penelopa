using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;

namespace Penelopa.Core.Services;

/// <summary>
/// 图元树的 JSON 序列化/反序列化。选区以图元 Id 存储，加载后可重新解析。
/// 容器子元素嵌套在父节点内。所有读取只接受当前（v2）文档；加载时还校验
/// Id 唯一性、节点数/深度/字节数，防止手工编辑或被污染的 localStorage 破坏页面。
/// </summary>
public static class DocumentSerializer
{
    /// <summary>当前文档格式版本；读取与写入都只使用此版本。</summary>
    public const int CurrentVersion = 2;

    /// <summary>单条加载警告的最大长度，避免把文件可控字符串整段回显给用户。</summary>
    private const int MaxWarningLength = 240;

    /// <summary>单文档最大图元数。</summary>
    public const int MaxNodes = 10_000;

    /// <summary>容器最大嵌套深度。</summary>
    public const int MaxDepth = 64;

    /// <summary>单文档最大 UTF-8 字节数。</summary>
    public const int MaxDocumentBytes = 4 * 1024 * 1024;

    private sealed class DocumentDto
    {
        public int Version { get; set; }
        public List<PrimitiveDto>? Primitives { get; set; }
        public List<Guid>? SelectionIds { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonElement? Simulation { get; set; }
    }

    private sealed class SimulationDto
    {
        public bool Loop { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SimulationTrackDto>? Tracks { get; set; }
    }

    private sealed class SimulationTrackDto
    {
        public Guid PrimitiveId { get; set; }
        public string? Property { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SimulationKeyframeDto>? Keyframes { get; set; }
    }

    private sealed class SimulationKeyframeDto
    {
        public double Time { get; set; }
        public float Value { get; set; }
        public string? Interpolation { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<double>? Bezier { get; set; }
    }

    private sealed class ParseContext
    {
        public HashSet<Guid> Ids { get; } = new();
        public int NodeCount { get; set; }
    }

    private sealed class PrimitiveDto
    {
        public Guid Id { get; set; }
        public string? Type { get; set; }
        public string? Kind { get; set; }
        public Dictionary<string, object>? Props { get; set; }
        public List<PrimitiveDto>? Children { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        // 让解析器先接受超过领域深度上限的文档，再由 FromDto 返回可诊断的拒绝结果
        MaxDepth = 256,
    };

    public static string Serialize(
        IReadOnlyList<Primitive> roots,
        IEnumerable<Primitive> selection,
        SimulationConfig? simulation = null)
    {
        var dto = new DocumentDto
        {
            Version = CurrentVersion,
            Primitives = roots.Select(ToDto).ToList(),
            SelectionIds = selection.Select(p => p.Id).ToList(),
            Simulation = simulation is null
                ? null
                : ToJsonElement(simulation),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>
    /// 反序列化 v2 文档。JSON 无效、版本不是 2、Id 重复/为空、超过节点/深度/字节上限时
    /// 返回 null。调用方根据重建的树重新解析选区 Id。
    /// </summary>
    public static DeserializeResult? Deserialize(string json)
        => Deserialize(json, out _);

    /// <summary>
    /// 反序列化 v2 文档，并在仅因版本不受支持而拒绝时通过
    /// <paramref name="rejectedVersion"/> 报告文件中的版本号
    /// （缺失版本为 0）；JSON/结构错误时为 null。
    /// </summary>
    public static DeserializeResult? Deserialize(string json, out int? rejectedVersion)
    {
        rejectedVersion = null;
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<DocumentDto>(json, Options);
            if (dto is null)
            {
                return null;
            }

            if (dto.Version != CurrentVersion)
            {
                rejectedVersion = dto.Version;
                return null;
            }

            if (dto.Primitives is null)
            {
                return null;
            }

            var context = new ParseContext();
            var roots = new List<Primitive>();
            foreach (var primitiveDto in dto.Primitives)
            {
                if (primitiveDto is null)
                {
                    throw new ArgumentException("A primitive entry is null.");
                }

                roots.Add(FromDto(primitiveDto, context, depth: 0));
            }

            var selectionIds = (dto.SelectionIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            var warnings = new List<string>();
            var simulation = ParseSimulation(dto, roots, warnings);
            return new DeserializeResult(roots, selectionIds, simulation, warnings);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or InvalidCastException or ArgumentException)
        {
            // JSON 合法但文档结构异常（未知类型/种类或属性类型不匹配），
            // 如手工编辑或未来版本文件。与无效 JSON 同等处理。
            return null;
        }
    }

    private static PrimitiveDto ToDto(Primitive primitive)
    {
        var props = new Dictionary<string, object>();
        foreach (var prop in primitive.Props)
        {
            if (prop.GetBoxedValue() is { } value)
            {
                props[prop.Name] = value;
            }
        }

        var dto = new PrimitiveDto
        {
            Id = primitive.Id,
            Type = primitive.GetType().Name,
            Props = props,
        };
        if (primitive is Container container)
        {
            dto.Kind = container.Kind.ToString();
            dto.Children = container.Children.Select(ToDto).ToList();
        }

        return dto;
    }

    private static JsonElement ToJsonElement(SimulationConfig simulation)
    {
        var dto = new SimulationDto
        {
            Loop = simulation.Loop,
            Tracks = simulation.Tracks.Select(ToDto).ToList(),
        };
        return JsonSerializer.SerializeToElement(dto, Options);
    }

    private static SimulationTrackDto ToDto(SimulationTrack track)
    {
        return new SimulationTrackDto
        {
            PrimitiveId = track.PrimitiveId,
            Property = track.PropertyName,
            Keyframes = track.Keyframes.Select(ToDto).ToList(),
        };
    }

    private static SimulationKeyframeDto ToDto(SimulationKeyframe keyframe)
    {
        return new SimulationKeyframeDto
        {
            Time = keyframe.Time,
            Value = keyframe.Value,
            Interpolation = ToDto(keyframe.Interpolation),
            Bezier = keyframe.Bezier is { } bezier
                ? new List<double> { bezier.X1, bezier.Y1, bezier.X2, bezier.Y2 }
                : null,
        };
    }

    private static string ToDto(InterpolationKind interpolation)
    {
        return interpolation switch
        {
            InterpolationKind.Linear => "linear",
            InterpolationKind.EaseIn => "ease-in",
            InterpolationKind.EaseOut => "ease-out",
            InterpolationKind.EaseInOut => "ease-in-out",
            InterpolationKind.Step => "step",
            InterpolationKind.CubicBezier => "cubic-bezier",
            _ => throw new ArgumentOutOfRangeException(nameof(interpolation)),
        };
    }

    private static SimulationConfig? ParseSimulation(
        DocumentDto dto,
        IReadOnlyList<Primitive> roots,
        List<string> warnings)
    {
        if (dto.Simulation is not { } element
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        SimulationDto? simulationDto;
        try
        {
            simulationDto = JsonSerializer.Deserialize<SimulationDto>(
                element.GetRawText(),
                Options);
        }
        catch (JsonException)
        {
            AddWarning(warnings, "The saved simulation configuration was ignored because it is invalid.");
            return null;
        }

        if (simulationDto is null)
        {
            AddWarning(warnings, "The saved simulation configuration was ignored because it is invalid.");
            return null;
        }

        try
        {
            var trackDtos = simulationDto.Tracks ?? new List<SimulationTrackDto>();
            if (trackDtos.Count > SimulationLimits.MaxTracks)
            {
                AddWarning(
                    warnings,
                    $"The saved simulation configuration was ignored: A simulation can contain at most {SimulationLimits.MaxTracks} tracks.");
                return null;
            }

            var rawKeyframeCount = 0L;
            foreach (var trackDto in trackDtos)
            {
                if (trackDto is null)
                {
                    AddWarning(
                        warnings,
                        "The saved simulation configuration was ignored: A simulation track is null.");
                    return null;
                }

                rawKeyframeCount += trackDto.Keyframes?.Count ?? 0;
            }

            if (rawKeyframeCount > SimulationLimits.MaxKeyframesTotal)
            {
                AddWarning(
                    warnings,
                    $"The saved simulation configuration was ignored: A simulation can contain at most {SimulationLimits.MaxKeyframesTotal} keyframes.");
                return null;
            }

            // Dormant/孤儿轨道先剪除，再做结构/值校验；结构非法的 Active 仍然整段丢弃。
            var index = SimulationPrimitiveIndex.Create(roots);
            var activeTracks = new List<SimulationTrack>();
            var dormantCount = 0;
            foreach (var trackDto in trackDtos)
            {
                if (string.IsNullOrWhiteSpace(trackDto.Property))
                {
                    AddWarning(
                        warnings,
                        "The saved simulation configuration was ignored: A simulation track is missing a property name.");
                    return null;
                }

                if (!index.TryGetPrimitive(trackDto.PrimitiveId, out _)
                    || !index.TryGetFloatProperty(
                        trackDto.PrimitiveId,
                        trackDto.Property,
                        out _))
                {
                    dormantCount++;
                    continue;
                }

                activeTracks.Add(FromDto(trackDto));
            }

            var config = new SimulationConfig(simulationDto.Loop, activeTracks);
            var structureIssues = SimulationValidator.ValidateStructure(config);
            if (structureIssues.Count > 0)
            {
                AddWarning(
                    warnings,
                    $"The saved simulation configuration was ignored: {structureIssues[0].Message}");
                return null;
            }

            var resolution = SimulationResolver.Resolve(config, index);
            var valueIssues = SimulationValidator.ValidateValues(resolution.Active);
            if (valueIssues.Count > 0)
            {
                AddWarning(
                    warnings,
                    $"The saved simulation configuration was ignored: {valueIssues[0].Message}");
                return null;
            }

            if (dormantCount > 0)
            {
                AddWarning(
                    warnings,
                    $"Removed {dormantCount} simulation track(s) whose primitives or properties no longer exist.");
            }

            return config;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            AddWarning(
                warnings,
                $"The saved simulation configuration was ignored: {ex.Message}");
            return null;
        }
    }

    private static void AddWarning(List<string> warnings, string message)
    {
        warnings.Add(
            message.Length <= MaxWarningLength
                ? message
                : message[..MaxWarningLength]);
    }

    private static SimulationTrack FromDto(SimulationTrackDto trackDto)
    {
        if (trackDto is null)
        {
            throw new FormatException("A simulation track is null.");
        }

        if (trackDto.Property is null)
        {
            throw new FormatException("A simulation track is missing a property name.");
        }

        if (trackDto.Keyframes is null || trackDto.Keyframes.Count == 0)
        {
            throw new FormatException("A simulation track has no keyframes.");
        }

        var keyframes = new List<SimulationKeyframe>(trackDto.Keyframes.Count);
        foreach (var keyframeDto in trackDto.Keyframes)
        {
            if (keyframeDto is null)
            {
                throw new FormatException("A simulation keyframe is null.");
            }

            keyframes.Add(FromDto(keyframeDto));
        }

        return new SimulationTrack(
            trackDto.PrimitiveId,
            trackDto.Property,
            keyframes);
    }

    private static SimulationKeyframe FromDto(SimulationKeyframeDto dto)
    {
        var interpolation = FromDto(dto.Interpolation);
        CubicBezier? bezier = null;
        if (dto.Bezier is not null)
        {
            if (dto.Bezier.Count != 4)
            {
                throw new FormatException(
                    "Cubic-bezier control points must contain four values.");
            }

            bezier = new CubicBezier(
                dto.Bezier[0],
                dto.Bezier[1],
                dto.Bezier[2],
                dto.Bezier[3]);
        }

        return new SimulationKeyframe(dto.Time, dto.Value, interpolation, bezier);
    }

    private static InterpolationKind FromDto(string? interpolation)
    {
        if (interpolation is null)
        {
            throw new FormatException("A simulation keyframe is missing an interpolation kind.");
        }

        if (string.Equals(interpolation, "linear", StringComparison.OrdinalIgnoreCase))
        {
            return InterpolationKind.Linear;
        }

        if (string.Equals(interpolation, "ease-in", StringComparison.OrdinalIgnoreCase))
        {
            return InterpolationKind.EaseIn;
        }

        if (string.Equals(interpolation, "ease-out", StringComparison.OrdinalIgnoreCase))
        {
            return InterpolationKind.EaseOut;
        }

        if (string.Equals(interpolation, "ease-in-out", StringComparison.OrdinalIgnoreCase))
        {
            return InterpolationKind.EaseInOut;
        }

        if (string.Equals(interpolation, "step", StringComparison.OrdinalIgnoreCase))
        {
            return InterpolationKind.Step;
        }

        if (string.Equals(interpolation, "cubic-bezier", StringComparison.OrdinalIgnoreCase))
        {
            return InterpolationKind.CubicBezier;
        }

        throw new FormatException(
            "A simulation keyframe uses an unknown interpolation kind.");
    }

    private static Primitive FromDto(PrimitiveDto? dto, ParseContext context, int depth)
    {
        // JSON 数组允许 null 元素；localStorage 属不可信输入，必须给出可诊断的拒绝。
        if (dto is null)
        {
            throw new ArgumentException("A primitive entry is null.");
        }

        if (depth > MaxDepth)
        {
            throw new ArgumentException("Document nesting is too deep.");
        }

        if (++context.NodeCount > MaxNodes)
        {
            throw new ArgumentException("Document has too many primitives.");
        }

        if (dto.Id == Guid.Empty)
        {
            throw new ArgumentException("Primitive id must not be empty.");
        }

        if (!context.Ids.Add(dto.Id))
        {
            throw new ArgumentException($"Duplicate primitive id '{dto.Id}'.");
        }

        Primitive primitive = dto.Type switch
        {
            "Circle" => new Circle(),
            "Rectangle" => new Rectangle(),
            "Triangle" => new Triangle(),
            "Container" => CreateContainer(dto.Kind),
            _ => throw new InvalidOperationException($"Unknown primitive type '{dto.Type}'."),
        };
        primitive.Id = dto.Id;

        if (dto.Props is not null)
        {
            ApplyProps(primitive, dto.Props);
        }

        ValidateProps(primitive);

        if (primitive is Container container)
        {
            if (dto.Children is not null)
            {
                foreach (var childDto in dto.Children)
                {
                    container.AddChild(FromDto(childDto, context, depth + 1));
                }
            }
        }
        else if (dto.Children is { Count: > 0 })
        {
            throw new ArgumentException($"Primitive '{dto.Id}' cannot have children.");
        }

        return primitive;
    }

    private static Container CreateContainer(string? kind)
    {
        return kind switch
        {
            "Offset" => Container.CreateOffset("Offset Container", 0f, 0f),
            "Rotation" => Container.CreateRotation("Rotate Container", 0f),
            "Flip" => Container.CreateFlip("Flip Container", flipX: false, flipY: false),
            _ => throw new InvalidOperationException($"Unknown container kind '{kind}'."),
        };
    }

    private static void ApplyProps(Primitive primitive, Dictionary<string, object> props)
    {
        foreach (var prop in primitive.Props)
        {
            if (!props.TryGetValue(prop.Name, out var value))
            {
                continue;
            }

            prop.SetBoxedValue(value);
        }
    }

    /// <summary>加载与 UI 使用同一套领域约束；非法值使整个文档被拒绝，而不是进入渲染循环。</summary>
    private static void ValidateProps(Primitive primitive)
    {
        foreach (var prop in primitive.Props)
        {
            if (!prop.IsValidValue(out var error))
            {
                throw new ArgumentException($"Invalid value for '{prop.Name}': {error}");
            }
        }
    }
}

/// <summary>文档反序列化结果。</summary>
public sealed class DeserializeResult
{
    public DeserializeResult(
        IReadOnlyList<Primitive> roots,
        IReadOnlyList<Guid> selectionIds,
        SimulationConfig? simulation = null,
        IReadOnlyList<string>? warnings = null)
    {
        Roots = roots;
        SelectionIds = selectionIds;
        Simulation = simulation;
        Warnings = warnings is null ? Array.Empty<string>() : warnings.ToArray();
    }

    public IReadOnlyList<Primitive> Roots { get; }

    public IReadOnlyList<Guid> SelectionIds { get; }

    /// <summary>保存的仿真配置；无 simulation 段时为 null。</summary>
    public SimulationConfig? Simulation { get; }

    /// <summary>加载仿真段时产生的警告（非法段被丢弃、悬空轨道被剪除等）。</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>按 Id 在重建的树中解析选区；未知 Id 被忽略。</summary>
    public IReadOnlyList<Primitive> ResolveSelection()
    {
        var byId = new Dictionary<Guid, Primitive>();
        Collect(Roots, byId);
        return SelectionIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
    }

    private static void Collect(IEnumerable<Primitive> primitives, Dictionary<Guid, Primitive> byId)
    {
        foreach (var primitive in primitives)
        {
            byId[primitive.Id] = primitive;
            if (primitive is Container container)
            {
                Collect(container.Children, byId);
            }
        }
    }
}
