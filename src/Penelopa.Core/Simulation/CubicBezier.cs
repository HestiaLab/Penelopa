namespace Penelopa.Core.Simulation;

/// <summary>
/// CSS 风格三次贝塞尔缓动的控制点。P0=(0,0)、P3=(1,1)，这里只保存 P1/P2。
/// v1 要求四个控制点都位于 [0,1]，因此插值因子不会越过端点。
/// </summary>
public readonly record struct CubicBezier(double X1, double Y1, double X2, double Y2)
{
    public static CubicBezier EaseIn { get; } = new(0.42d, 0d, 1d, 1d);

    public static CubicBezier EaseOut { get; } = new(0d, 0d, 0.58d, 1d);

    public static CubicBezier EaseInOut { get; } = new(0.42d, 0d, 0.58d, 1d);

    /// <summary>四个控制点是否都是 [0,1] 内的有限数。</summary>
    public bool IsInUnitRange
    {
        get
        {
            return double.IsFinite(X1) && double.IsFinite(Y1)
                && double.IsFinite(X2) && double.IsFinite(Y2)
                && X1 is >= 0d and <= 1d
                && Y1 is >= 0d and <= 1d
                && X2 is >= 0d and <= 1d
                && Y2 is >= 0d and <= 1d;
        }
    }
}
