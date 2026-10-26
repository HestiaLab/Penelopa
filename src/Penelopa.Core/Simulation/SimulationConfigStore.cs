using Penelopa.Core.Primitives;
using Penelopa.Core.Services;

namespace Penelopa.Core.Simulation;

/// <summary>
/// 仿真配置的 singleton 存储。轨道/关键帧编辑会创建新的不可变配置并进入
/// 独立的 50 步撤销栈；Loop 是播放设置，不进入该撤销栈。所有写入都先用
/// 当前文档解析目标并校验，失败时不改变配置。
/// </summary>
public sealed class SimulationConfigStore
{
    private readonly IPrimitiveService _primitives;
    private readonly List<IReadOnlyList<SimulationTrack>> _undo = new();
    private readonly List<IReadOnlyList<SimulationTrack>> _redo = new();

    private SimulationConfig _config = SimulationConfig.Empty;

    public SimulationConfigStore(IPrimitiveService primitives)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        _primitives = primitives;
    }

    public SimulationConfig Config => _config;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public event Action? Changed;

    /// <summary>把关键帧值设置到指定时刻；已有同刻关键帧时更新其值，否则插入。</summary>
    public bool TrySetKeyframeValue(
        Guid primitiveId,
        string propertyName,
        double time,
        float value,
        out string? error)
    {
        var index = BuildIndex();
        if (!index.TryGetFloatProperty(primitiveId, propertyName, out var property))
        {
            error = "The primitive or float property no longer exists.";
            return false;
        }

        var track = FindTrack(_config, primitiveId, propertyName);
        if (track is null)
        {
            error = "The animation track does not exist.";
            return false;
        }

        if (!TryValidateTime(time, out error)
            || !SimulationValidator.TryValidateKeyframeValue(property, value, out error))
        {
            return false;
        }

        var keyframes = track.Keyframes;
        var existing = -1;
        for (var position = 0; position < keyframes.Count; position++)
        {
            if (Math.Abs(keyframes[position].Time - time)
                <= SimulationLimits.SameTimeToleranceSeconds)
            {
                existing = position;
                break;
            }
        }

        SimulationKeyframe[] updated;
        if (existing >= 0)
        {
            updated = keyframes.ToArray();
            var current = updated[existing];
            updated[existing] = new SimulationKeyframe(
                current.Time,
                value,
                current.Interpolation,
                current.Bezier);
        }
        else
        {
            var list = keyframes.ToList();
            var insertAt = list.FindIndex(candidate => candidate.Time > time);
            if (insertAt < 0)
            {
                insertAt = list.Count;
            }

            list.Insert(
                insertAt,
                new SimulationKeyframe(time, value, InterpolationKind.Linear));
            updated = list.ToArray();
        }

        var replacement = new SimulationTrack(
            track.PrimitiveId,
            track.PropertyName,
            updated);
        var candidate = ReplaceTrack(_config, track, replacement);
        if (!TryAccept(candidate, index, out error))
        {
            return false;
        }

        CommitTracks(candidate.Tracks);
        error = null;
        return true;
    }

    /// <summary>
    /// 新建轨道：在给定时间与 +1s（边界处回退）各放一个同值关键帧。
    /// 同一 (图元, 属性) 只允许一条轨道。
    /// </summary>
    public bool TryAddTrack(
        Guid primitiveId,
        string propertyName,
        double time,
        float value,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            error = "Choose a float property to animate.";
            return false;
        }

        var index = BuildIndex();
        if (!index.TryGetFloatProperty(primitiveId, propertyName, out var property))
        {
            error = "The primitive or float property no longer exists.";
            return false;
        }

        if (FindTrack(_config, primitiveId, propertyName) is not null)
        {
            error = "This property already has an animation track.";
            return false;
        }

        if (!TryValidateTime(time, out error)
            || !SimulationValidator.TryValidateKeyframeValue(property, value, out error))
        {
            return false;
        }

        var secondTime = GetSecondKeyframeTime(time);
        var atTime = new SimulationKeyframe(time, value, InterpolationKind.Linear);
        var atSecond = new SimulationKeyframe(secondTime, value, InterpolationKind.Linear);
        var keyframes = time <= secondTime
            ? new[] { atTime, atSecond }
            : new[] { atSecond, atTime };

        var track = new SimulationTrack(primitiveId, propertyName, keyframes);
        var candidate = new SimulationConfig(_config.Loop, _config.Tracks.Append(track));
        if (!TryAccept(candidate, index, out error))
        {
            return false;
        }

        CommitTracks(candidate.Tracks);
        error = null;
        return true;
    }

    public bool TryRemoveTrack(
        Guid primitiveId,
        string propertyName,
        out string? error)
    {
        var track = FindTrack(_config, primitiveId, propertyName);
        if (track is null)
        {
            error = "The animation track does not exist.";
            return false;
        }

        var remaining = _config.Tracks
            .Where(candidate => candidate.Key != track.Key)
            .ToArray();
        CommitTracks(remaining);
        error = null;
        return true;
    }

    public bool TryUpdateKeyframe(
        Guid primitiveId,
        string propertyName,
        int keyframeIndex,
        double time,
        float value,
        InterpolationKind interpolation,
        CubicBezier? bezier,
        out string? error)
    {
        var index = BuildIndex();
        if (!index.TryGetFloatProperty(primitiveId, propertyName, out var property))
        {
            error = "The primitive or float property no longer exists.";
            return false;
        }

        var track = FindTrack(_config, primitiveId, propertyName);
        if (track is null)
        {
            error = "The animation track does not exist.";
            return false;
        }

        if (keyframeIndex < 0 || keyframeIndex >= track.Keyframes.Count)
        {
            error = "The keyframe does not exist.";
            return false;
        }

        if (!TryValidateTime(time, out error)
            || !SimulationValidator.TryValidateKeyframeValue(property, value, out error))
        {
            return false;
        }

        if (!Enum.IsDefined(typeof(InterpolationKind), interpolation))
        {
            error = "Choose a valid interpolation kind.";
            return false;
        }

        if (interpolation == InterpolationKind.CubicBezier)
        {
            if (bezier is null || !bezier.Value.IsInUnitRange)
            {
                error = "Cubic-bezier control points must all be between 0 and 1.";
                return false;
            }
        }
        else if (bezier is not null)
        {
            error = "Bezier control points are only valid for cubic-bezier interpolation.";
            return false;
        }

        var keyframes = track.Keyframes.ToArray();
        keyframes[keyframeIndex] = new SimulationKeyframe(time, value, interpolation, bezier);
        var replacement = new SimulationTrack(
            track.PrimitiveId,
            track.PropertyName,
            keyframes);
        var candidate = ReplaceTrack(_config, track, replacement);
        if (!TryAccept(candidate, index, out error))
        {
            return false;
        }

        CommitTracks(candidate.Tracks);
        error = null;
        return true;
    }

    public bool TryRemoveKeyframe(
        Guid primitiveId,
        string propertyName,
        int keyframeIndex,
        out string? error)
    {
        var track = FindTrack(_config, primitiveId, propertyName);
        if (track is null)
        {
            error = "The animation track does not exist.";
            return false;
        }

        if (keyframeIndex < 0 || keyframeIndex >= track.Keyframes.Count)
        {
            error = "The keyframe does not exist.";
            return false;
        }

        var keyframes = track.Keyframes.ToList();
        keyframes.RemoveAt(keyframeIndex);

        IReadOnlyList<SimulationTrack> tracks;
        if (keyframes.Count == 0)
        {
            tracks = _config.Tracks
                .Where(candidate => candidate.Key != track.Key)
                .ToArray();
        }
        else
        {
            var replacement = new SimulationTrack(
                track.PrimitiveId,
                track.PropertyName,
                keyframes);
            var candidate = ReplaceTrack(_config, track, replacement);
            if (!TryAccept(candidate, BuildIndex(), out error))
            {
                return false;
            }

            tracks = candidate.Tracks;
        }

        CommitTracks(tracks);
        error = null;
        return true;
    }

    /// <summary>Loop 是播放设置，变更不进入配置撤销栈。</summary>
    public void SetLoop(bool loop)
    {
        if (_config.Loop == loop)
        {
            return;
        }

        _config = new SimulationConfig(loop, _config.Tracks);
        Changed?.Invoke();
    }

    /// <summary>删除所有指向已不存在图元/非 Float 属性的轨道；返回删除数量。</summary>
    public int RemoveDormantTracks()
    {
        var index = BuildIndex();
        var resolution = SimulationResolver.Resolve(_config, index);
        if (resolution.Dormant.Count == 0)
        {
            return 0;
        }

        var dormantKeys = new HashSet<SimulationTrackKey>(
            resolution.Dormant.Select(track => track.Key));
        var remaining = _config.Tracks
            .Where(track => !dormantKeys.Contains(track.Key))
            .ToArray();
        CommitTracks(remaining);
        return resolution.Dormant.Count;
    }

    /// <summary>返回按文档顺序、属性名排序且排除 Dormant 的配置快照。</summary>
    public SimulationConfig ExportActive()
    {
        var index = BuildIndex();
        var resolution = SimulationResolver.Resolve(_config, index);
        var primitivePositions = new Dictionary<Guid, int>();
        for (var position = 0; position < index.Primitives.Count; position++)
        {
            primitivePositions[index.Primitives[position].Id] = position;
        }

        var propertyPositions = new Dictionary<SimulationTrackKey, int>();
        for (var position = 0; position < index.FloatProperties.Count; position++)
        {
            var entry = index.FloatProperties[position];
            propertyPositions[new SimulationTrackKey(entry.Primitive.Id, entry.Property.Name)] = position;
        }

        var tracks = resolution.Active
            .Select(resolved => resolved.Track)
            .OrderBy(
                track => primitivePositions.TryGetValue(track.PrimitiveId, out var position)
                    ? position
                    : int.MaxValue)
            .ThenBy(
                track => propertyPositions.TryGetValue(track.Key, out var position)
                    ? position
                    : int.MaxValue)
            .ToArray();
        return new SimulationConfig(_config.Loop, tracks);
    }

    public IReadOnlyList<SimulationTrack> GetDormantTracks()
    {
        var index = BuildIndex();
        return SimulationResolver.Resolve(_config, index).Dormant.ToArray();
    }

    /// <summary>加载文档时整体替换配置，并清空配置撤销栈。</summary>
    public void ReplaceConfig(SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _config = config;
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    /// <summary>捕获当前配置与完整配置撤销/重做栈。</summary>
    public SimulationConfigState CaptureState()
        => new(_config, _undo.ToArray(), _redo.ToArray());

    /// <summary>恢复配置、Loop 与配置撤销/重做栈；会触发一次 Changed。</summary>
    public void RestoreState(SimulationConfigState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _config = state.Config;
        _undo.Clear();
        _undo.AddRange(state.Undo);
        _redo.Clear();
        _redo.AddRange(state.Redo);
        Changed?.Invoke();
    }

    /// <summary>清空配置撤销/重做历史，但保留当前配置（进入仿真页时使用）。</summary>
    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        var previous = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(_config.Tracks);
        _config = new SimulationConfig(_config.Loop, previous);
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        var next = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(_config.Tracks);
        if (_undo.Count > SimulationLimits.ConfigUndoDepth)
        {
            _undo.RemoveAt(0);
        }

        _config = new SimulationConfig(_config.Loop, next);
        Changed?.Invoke();
    }

    private SimulationPrimitiveIndex BuildIndex()
        => SimulationPrimitiveIndex.Create(_primitives.GetAll());

    private static SimulationTrack? FindTrack(
        SimulationConfig config,
        Guid primitiveId,
        string propertyName)
    {
        foreach (var track in config.Tracks)
        {
            if (track.PrimitiveId == primitiveId
                && string.Equals(track.PropertyName, propertyName, StringComparison.Ordinal))
            {
                return track;
            }
        }

        return null;
    }

    private static SimulationConfig ReplaceTrack(
        SimulationConfig config,
        SimulationTrack oldTrack,
        SimulationTrack newTrack)
    {
        var tracks = config.Tracks
            .Select(track => track.Key == oldTrack.Key ? newTrack : track)
            .ToArray();
        return new SimulationConfig(config.Loop, tracks);
    }

    private static bool TryAccept(
        SimulationConfig candidate,
        SimulationPrimitiveIndex index,
        out string? error)
    {
        var issues = SimulationValidator.Validate(candidate, index);
        if (issues.Count > 0)
        {
            error = issues[0].Message;
            return false;
        }

        error = null;
        return true;
    }

    private void CommitTracks(IReadOnlyList<SimulationTrack> tracks)
    {
        _undo.Add(_config.Tracks);
        if (_undo.Count > SimulationLimits.ConfigUndoDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        _config = new SimulationConfig(_config.Loop, tracks);
        Changed?.Invoke();
    }

    private static bool TryValidateTime(double time, out string? error)
    {
        if (!double.IsFinite(time)
            || time < 0d
            || time > SimulationLimits.MaxTimeSeconds)
        {
            error = "Keyframe time must be between 0 and 3600 seconds.";
            return false;
        }

        error = null;
        return true;
    }

    private static double GetSecondKeyframeTime(double time)
    {
        var second = Math.Min(
            time + SimulationLimits.DefaultNewTrackSpanSeconds,
            SimulationLimits.MaxTimeSeconds);
        if (second - time < SimulationLimits.MinKeyframeGapSeconds)
        {
            second = time - SimulationLimits.DefaultNewTrackSpanSeconds;
            if (second < 0d)
            {
                second = 0d;
            }
        }

        return second;
    }
}

/// <summary>仿真配置的一次性完整快照；内容对调用方不透明。</summary>
public sealed class SimulationConfigState
{
    internal SimulationConfigState(
        SimulationConfig config,
        IReadOnlyList<IReadOnlyList<SimulationTrack>> undo,
        IReadOnlyList<IReadOnlyList<SimulationTrack>> redo)
    {
        Config = config;
        Undo = undo;
        Redo = redo;
    }

    internal SimulationConfig Config { get; }

    internal IReadOnlyList<IReadOnlyList<SimulationTrack>> Undo { get; }

    internal IReadOnlyList<IReadOnlyList<SimulationTrack>> Redo { get; }
}
