namespace Penelopa.Core.Simulation;

/// <summary>
/// 轨道上的一个关键帧。插值方式作用于本帧到下一帧的区间；
/// 时间/值的领域校验由 <see cref="SimulationValidator"/> 负责。
/// </summary>
public sealed record SimulationKeyframe
{
    public SimulationKeyframe(
        double time,
        float value,
        InterpolationKind interpolation,
        CubicBezier? bezier = null)
    {
        if (interpolation == InterpolationKind.CubicBezier && bezier is null)
        {
            throw new ArgumentException(
                "Cubic-bezier interpolation requires control points.",
                nameof(bezier));
        }

        if (interpolation != InterpolationKind.CubicBezier && bezier is not null)
        {
            throw new ArgumentException(
                "Bezier control points are only valid for cubic-bezier interpolation.",
                nameof(bezier));
        }

        Time = time;
        Value = value;
        Interpolation = interpolation;
        Bezier = bezier;
    }

    public double Time { get; }

    public float Value { get; }

    public InterpolationKind Interpolation { get; }

    public CubicBezier? Bezier { get; }
}
