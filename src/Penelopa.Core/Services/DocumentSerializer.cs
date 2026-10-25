using System.Text;
using System.Text.Json;
using Penelopa.Core.Primitives;

namespace Penelopa.Core.Services;

/// <summary>
/// 图元树的 JSON 序列化/反序列化。选区以图元 Id 存储，加载后可重新解析。
/// 容器子元素嵌套在父节点内。加载时校验版本、Id 唯一性、节点数/深度/字节数，
/// 防止手工编辑、旧版本或被污染的 localStorage 破坏页面。
/// </summary>
public static class DocumentSerializer
{
    /// <summary>当前文档格式版本。缺失版本视为旧版（版本 0），其数据结构与版本 1 相同。</summary>
    public const int CurrentVersion = 1;

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

    public static string Serialize(IReadOnlyList<Primitive> roots, IEnumerable<Primitive> selection)
    {
        var dto = new DocumentDto
        {
            Version = CurrentVersion,
            Primitives = roots.Select(ToDto).ToList(),
            SelectionIds = selection.Select(p => p.Id).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>
    /// 反序列化文档。JSON 无效、版本未知、Id 重复/为空、超过节点/深度/字节上限时返回 null。
    /// 调用方根据重建的树重新解析选区 Id。
    /// </summary>
    public static DeserializeResult? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
        {
            return null;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<DocumentDto>(json, Options);
            if (dto?.Primitives is null || dto.Version < 0 || dto.Version > CurrentVersion)
            {
                return null;
            }

            var context = new ParseContext();
            var roots = new List<Primitive>();
            foreach (var primitiveDto in dto.Primitives)
            {
                roots.Add(FromDto(primitiveDto, context, depth: 0));
            }

            var selectionIds = (dto.SelectionIds ?? new List<Guid>())
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            return new DeserializeResult(roots, selectionIds);
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

    private static Primitive FromDto(PrimitiveDto dto, ParseContext context, int depth)
    {
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
    public DeserializeResult(IReadOnlyList<Primitive> roots, IReadOnlyList<Guid> selectionIds)
    {
        Roots = roots;
        SelectionIds = selectionIds;
    }

    public IReadOnlyList<Primitive> Roots { get; }

    public IReadOnlyList<Guid> SelectionIds { get; }

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
