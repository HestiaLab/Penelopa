namespace Penelopa.Core.Simulation;

/// <summary>把轨道在给定时刻求值为属性值；不修改轨道或图元。</summary>
public static class SimulationEvaluator
{
    public static float Evaluate(SimulationTrack track, double time)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (!double.IsFinite(time))
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }

        var keyframes = track.Keyframes;
        if (time <= keyframes[0].Time)
        {
            return keyframes[0].Value;
        }

        var lastIndex = keyframes.Count - 1;
        if (time >= keyframes[lastIndex].Time)
        {
            return keyframes[lastIndex].Value;
        }

        // 找到最后一个时间不大于 time 的关键帧；首尾已排除，lower 最大为 Count - 2。
        var lower = 0;
        var upper = lastIndex;
        while (lower + 1 < upper)
        {
            var middle = (lower + upper) / 2;
            if (keyframes[middle].Time <= time)
            {
                lower = middle;
            }
            else
            {
                upper = middle;
            }
        }

        var start = keyframes[lower];
        var end = keyframes[upper];
        if (start.Interpolation == InterpolationKind.Step)
        {
            return start.Value;
        }

        var span = end.Time - start.Time;
        if (span <= 0d)
        {
            return end.Value;
        }

        var progress = (time - start.Time) / span;
        var eased = Ease(progress, start);
        return (float)(start.Value + ((end.Value - start.Value) * eased));
    }

    public static double Ease(double progress, SimulationKeyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        var clamped = Math.Clamp(progress, 0d, 1d);
        return keyframe.Interpolation switch
        {
            InterpolationKind.Linear => clamped,
            InterpolationKind.EaseIn => CubicBezierSolver.Evaluate(clamped, CubicBezier.EaseIn),
            InterpolationKind.EaseOut => CubicBezierSolver.Evaluate(clamped, CubicBezier.EaseOut),
            InterpolationKind.EaseInOut => CubicBezierSolver.Evaluate(clamped, CubicBezier.EaseInOut),
            InterpolationKind.Step => 0d,
            InterpolationKind.CubicBezier => CubicBezierSolver.Evaluate(
                clamped,
                keyframe.Bezier!.Value),
            _ => throw new ArgumentOutOfRangeException(
                nameof(keyframe),
                "Unknown interpolation kind."),
        };
    }
}
