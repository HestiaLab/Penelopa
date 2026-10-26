namespace Penelopa.Core.Simulation;

/// <summary>仿真配置的防御性上限与编辑默认值。</summary>
public static class SimulationLimits
{
    /// <summary>单个文档允许的最大轨道数。</summary>
    public const int MaxTracks = 2_000;

    /// <summary>单条轨道允许的最大关键帧数。</summary>
    public const int MaxKeyframesPerTrack = 200;

    /// <summary>单个文档允许的关键帧总数。</summary>
    public const int MaxKeyframesTotal = 10_000;

    /// <summary>关键帧时间上限（秒）。</summary>
    public const double MaxTimeSeconds = 3_600d;

    /// <summary>同一轨道相邻关键帧的最小时间间隔（秒）。</summary>
    public const double MinKeyframeGapSeconds = 0.001d;

    /// <summary>判定“同一时刻”的容差，取最小间隔的一半。</summary>
    public const double SameTimeToleranceSeconds = MinKeyframeGapSeconds * 0.5d;

    /// <summary>关键帧值允许的最大绝对值。</summary>
    public const float MaxValueMagnitude = 1e7f;

    /// <summary>新建轨道第二个关键帧的默认时间跨度（秒）。</summary>
    public const double DefaultNewTrackSpanSeconds = 1d;

    /// <summary>仿真配置撤销栈的最大深度。</summary>
    public const int ConfigUndoDepth = 50;

    /// <summary>动画范围估算的最大采样点数。</summary>
    public const int MaxFitSamples = 512;
}
