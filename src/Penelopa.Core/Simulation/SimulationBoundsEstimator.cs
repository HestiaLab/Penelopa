using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;

namespace Penelopa.Core.Simulation;

/// <summary>
/// 估算动画在整条时间轴上的世界包围盒，供只读画布 Fit 使用。
/// 采样点包含所有关键帧时间与相邻中点；临时写入/恢复属性，不产生通知或历史。
/// 估算在 Ready/Stop 后或暂停会话的显式 Fit 时调用，不得与播放帧并发。
/// </summary>
public static class SimulationBoundsEstimator
{
    public static Box? Estimate(
        IReadOnlyList<Primitive> roots,
        SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(config);
        if (roots.Count == 0)
        {
            return null;
        }

        var index = SimulationPrimitiveIndex.Create(roots);
        var baseBounds = ComputeBounds(roots);
        var resolution = SimulationResolver.Resolve(config, index);
        var issues = new List<SimulationValidationIssue>(
            SimulationValidator.ValidateStructure(config));
        issues.AddRange(SimulationValidator.ValidateValues(resolution.Active));
        if (resolution.Active.Count == 0 || issues.Count > 0)
        {
            return baseBounds;
        }

        var duration = SimulationTimeline.GetDuration(
            resolution.Active.Select(track => track.Track));
        if (duration <= 0d)
        {
            return baseBounds;
        }

        var sampleTimes = BuildSampleTimes(resolution.Active, duration);
        var baseValues = new List<(FloatPropValue Property, float Value)>();
        foreach (var entry in index.FloatProperties)
        {
            baseValues.Add((entry.Property, entry.Property.Value));
        }

        try
        {
            var union = baseBounds;
            foreach (var time in sampleTimes)
            {
                foreach (var (property, baseValue) in baseValues)
                {
                    property.Value = baseValue;
                }

                foreach (var resolved in resolution.Active)
                {
                    resolved.Property.Value = SimulationEvaluator.Evaluate(
                        resolved.Track,
                        time);
                }

                union = Union(union, ComputeBounds(roots));
            }

            return union;
        }
        finally
        {
            foreach (var (property, baseValue) in baseValues)
            {
                property.Value = baseValue;
            }
        }
    }

    private static IReadOnlyList<double> BuildSampleTimes(
        IReadOnlyList<ResolvedSimulationTrack> active,
        double duration)
    {
        var keyTimes = new SortedSet<double> { 0d, duration };
        foreach (var resolved in active)
        {
            foreach (var keyframe in resolved.Track.Keyframes)
            {
                keyTimes.Add(Math.Clamp(keyframe.Time, 0d, duration));
            }
        }

        var ordered = keyTimes.ToList();
        var samples = new List<double>(ordered.Count * 2);
        for (var position = 0; position < ordered.Count; position++)
        {
            samples.Add(ordered[position]);
            if (position + 1 < ordered.Count)
            {
                var next = ordered[position + 1];
                if (next - ordered[position] > 0d)
                {
                    samples.Add((ordered[position] + next) * 0.5d);
                }
            }
        }

        if (samples.Count <= SimulationLimits.MaxFitSamples)
        {
            return samples;
        }

        var downsampled = new List<double>(SimulationLimits.MaxFitSamples);
        for (var position = 0; position < SimulationLimits.MaxFitSamples; position++)
        {
            var keyPosition = (int)Math.Round(
                (double)position * (ordered.Count - 1)
                / (SimulationLimits.MaxFitSamples - 1));
            downsampled.Add(ordered[keyPosition]);
        }

        downsampled[0] = 0d;
        downsampled[downsampled.Count - 1] = duration;
        return downsampled;
    }

    private static Box ComputeBounds(IReadOnlyList<Primitive> roots)
    {
        var first = roots[0].GetWorldBoundingBox();
        var minX = first.MinX;
        var minY = first.MinY;
        var maxX = first.MaxX;
        var maxY = first.MaxY;

        for (var position = 1; position < roots.Count; position++)
        {
            var bounds = roots[position].GetWorldBoundingBox();
            minX = MathF.Min(minX, bounds.MinX);
            minY = MathF.Min(minY, bounds.MinY);
            maxX = MathF.Max(maxX, bounds.MaxX);
            maxY = MathF.Max(maxY, bounds.MaxY);
        }

        return new Box(minX, minY, maxX, maxY);
    }

    private static Box Union(Box left, Box right)
        => new(
            MathF.Min(left.MinX, right.MinX),
            MathF.Min(left.MinY, right.MinY),
            MathF.Max(left.MaxX, right.MaxX),
            MathF.Max(left.MaxY, right.MaxY));
}
