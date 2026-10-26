namespace Penelopa.Core.Simulation;

/// <summary>关键帧到下一关键帧区间的插值方式。</summary>
public enum InterpolationKind
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,
    Step,
    CubicBezier,
}
