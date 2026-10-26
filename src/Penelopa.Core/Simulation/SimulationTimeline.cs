namespace Penelopa.Core.Simulation;

/// <summary>全局绝对时间轴的时长、夹取、循环与步进计算。</summary>
public static class SimulationTimeline
{
    /// <summary>所有给定轨道最后一个关键帧时间的最大值；空集合返回 0。</summary>
    public static double GetDuration(IEnumerable<SimulationTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var duration = 0d;
        foreach (var track in tracks)
        {
            var keyframes = track.Keyframes;
            var last = keyframes[keyframes.Count - 1].Time;
            if (last > duration)
            {
                duration = last;
            }
        }

        return duration;
    }

    public static double Clamp(double time, double duration)
    {
        if (!double.IsFinite(time))
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }

        if (!double.IsFinite(duration) || duration < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        return duration <= 0d ? 0d : Math.Clamp(time, 0d, duration);
    }

    /// <summary>把时间映射到 [0, duration) 的循环位置；duration 为 0 时返回 0。</summary>
    public static double Wrap(double time, double duration)
    {
        if (!double.IsFinite(time))
        {
            throw new ArgumentOutOfRangeException(nameof(time));
        }

        if (!double.IsFinite(duration) || duration <= 0d)
        {
            return 0d;
        }

        var wrapped = time % duration;
        return wrapped < 0d ? wrapped + duration : wrapped;
    }

    public static double Step(double time, double delta, double duration)
    {
        if (!double.IsFinite(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        return Clamp(time + delta, duration);
    }
}
