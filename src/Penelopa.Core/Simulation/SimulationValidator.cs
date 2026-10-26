using Penelopa.Core.Primitives;

namespace Penelopa.Core.Simulation;

/// <summary>
/// 仿真配置的结构/限制校验与目标属性值校验。结构问题会阻止播放；
/// 目标不存在或属性不是 Float 的轨道属于 Dormant，不计入错误。
/// </summary>
public static class SimulationValidator
{
    public static IReadOnlyList<SimulationValidationIssue> ValidateStructure(
        SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var issues = new List<SimulationValidationIssue>();
        if (config.Tracks.Count > SimulationLimits.MaxTracks)
        {
            issues.Add(Global(
                SimulationIssueKind.Structure,
                $"A simulation can contain at most {SimulationLimits.MaxTracks} tracks."));
        }

        var keys = new HashSet<SimulationTrackKey>();
        var totalKeyframes = 0;
        foreach (var track in config.Tracks)
        {
            if (track.PrimitiveId == Guid.Empty)
            {
                issues.Add(TrackIssue(
                    track,
                    SimulationIssueKind.Structure,
                    "Track primitive id must not be empty."));
            }

            if (string.IsNullOrWhiteSpace(track.PropertyName))
            {
                issues.Add(TrackIssue(
                    track,
                    SimulationIssueKind.Structure,
                    "Track property name must not be empty."));
            }

            if (!keys.Add(track.Key))
            {
                issues.Add(TrackIssue(
                    track,
                    SimulationIssueKind.Structure,
                    $"A track for '{track.PropertyName}' already exists on this primitive."));
            }

            if (track.Keyframes.Count > SimulationLimits.MaxKeyframesPerTrack)
            {
                issues.Add(TrackIssue(
                    track,
                    SimulationIssueKind.Structure,
                    $"A track can contain at most {SimulationLimits.MaxKeyframesPerTrack} keyframes."));
            }

            totalKeyframes += track.Keyframes.Count;
            ValidateKeyframes(track, issues);
        }

        if (totalKeyframes > SimulationLimits.MaxKeyframesTotal)
        {
            issues.Add(Global(
                SimulationIssueKind.Structure,
                $"A simulation can contain at most {SimulationLimits.MaxKeyframesTotal} keyframes."));
        }

        return issues;
    }

    /// <summary>校验 Active 轨道的关键帧值是否满足目标 Float 属性的领域约束。</summary>
    public static IReadOnlyList<SimulationValidationIssue> ValidateValues(
        IEnumerable<ResolvedSimulationTrack> activeTracks)
    {
        ArgumentNullException.ThrowIfNull(activeTracks);

        var issues = new List<SimulationValidationIssue>();
        foreach (var resolved in activeTracks)
        {
            var keyframes = resolved.Track.Keyframes;
            for (var index = 0; index < keyframes.Count; index++)
            {
                var keyframe = keyframes[index];
                if (!resolved.Property.ValidateValue(keyframe.Value, out var error))
                {
                    issues.Add(new SimulationValidationIssue(
                        SimulationIssueKind.Value,
                        resolved.Track.PrimitiveId,
                        resolved.Track.PropertyName,
                        index,
                        error ?? "The keyframe value is invalid for this property."));
                }
            }
        }

        return issues;
    }

    /// <summary>结构校验 + Active 轨道的目标值校验；Dormant 不产生错误。</summary>
    public static IReadOnlyList<SimulationValidationIssue> Validate(
        SimulationConfig config,
        SimulationPrimitiveIndex index)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(index);

        var issues = new List<SimulationValidationIssue>(ValidateStructure(config));
        var resolution = SimulationResolver.Resolve(config, index);
        issues.AddRange(ValidateValues(resolution.Active));
        return issues;
    }

    /// <summary>校验一个候选关键帧值；不修改属性。</summary>
    public static bool TryValidateKeyframeValue(
        FloatPropValue property,
        float value,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (!float.IsFinite(value))
        {
            error = "Value must be a finite number.";
            return false;
        }

        if (Math.Abs((double)value) > SimulationLimits.MaxValueMagnitude)
        {
            error = "Value is too large.";
            return false;
        }

        return property.ValidateValue(value, out error);
    }

    private static void ValidateKeyframes(
        SimulationTrack track,
        List<SimulationValidationIssue> issues)
    {
        double? previousTime = null;
        for (var index = 0; index < track.Keyframes.Count; index++)
        {
            var keyframe = track.Keyframes[index];
            var timeIsValid = double.IsFinite(keyframe.Time)
                && keyframe.Time >= 0d
                && keyframe.Time <= SimulationLimits.MaxTimeSeconds;

            if (!timeIsValid)
            {
                issues.Add(KeyframeIssue(
                    track,
                    index,
                    "Keyframe time must be a finite number between 0 and 3600 seconds."));
                previousTime = null;
            }
            else
            {
                if (previousTime is { } previous
                    && keyframe.Time - previous < SimulationLimits.MinKeyframeGapSeconds)
                {
                    issues.Add(KeyframeIssue(
                        track,
                        index,
                        "Keyframe times must increase by at least 1 ms."));
                }

                previousTime = keyframe.Time;
            }

            if (!float.IsFinite(keyframe.Value)
                || Math.Abs((double)keyframe.Value) > SimulationLimits.MaxValueMagnitude)
            {
                issues.Add(KeyframeIssue(
                    track,
                    index,
                    "Keyframe value must be a finite number with magnitude at most 10000000."));
            }

            if (!Enum.IsDefined(typeof(InterpolationKind), keyframe.Interpolation))
            {
                issues.Add(KeyframeIssue(
                    track,
                    index,
                    "Unknown interpolation kind."));
            }

            if (keyframe.Interpolation == InterpolationKind.CubicBezier)
            {
                if (keyframe.Bezier is null || !keyframe.Bezier.Value.IsInUnitRange)
                {
                    issues.Add(KeyframeIssue(
                        track,
                        index,
                        "Cubic-bezier control points must all be between 0 and 1."));
                }
            }
            else if (keyframe.Bezier is not null)
            {
                issues.Add(KeyframeIssue(
                    track,
                    index,
                    "Bezier control points are only valid for cubic-bezier interpolation."));
            }
        }
    }

    private static SimulationValidationIssue Global(
        SimulationIssueKind kind,
        string message)
        => new(kind, null, null, null, message);

    private static SimulationValidationIssue TrackIssue(
        SimulationTrack track,
        SimulationIssueKind kind,
        string message)
        => new(kind, track.PrimitiveId, track.PropertyName, null, message);

    private static SimulationValidationIssue KeyframeIssue(
        SimulationTrack track,
        int index,
        string message)
        => new(SimulationIssueKind.Structure, track.PrimitiveId, track.PropertyName, index, message);
}
