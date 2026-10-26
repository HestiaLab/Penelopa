namespace Penelopa.Core.Simulation;

/// <summary>
/// 求解 CSS 风格 cubic-bezier 缓动：先由 x(t)=progress 反解参数 t，再取 y(t)。
/// 控制点由校验器限制在 [0,1]，因此 x(t) 单调，二分法稳定。
/// </summary>
public static class CubicBezierSolver
{
    private const int BisectionIterations = 32;

    public static double Evaluate(double progress, CubicBezier bezier)
    {
        if (!double.IsFinite(progress))
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }

        var target = Math.Clamp(progress, 0d, 1d);
        if (target <= 0d)
        {
            return 0d;
        }

        if (target >= 1d)
        {
            return 1d;
        }

        var lower = 0d;
        var upper = 1d;
        for (var index = 0; index < BisectionIterations; index++)
        {
            var middle = (lower + upper) * 0.5d;
            var x = Coordinate(middle, bezier.X1, bezier.X2);
            if (x < target)
            {
                lower = middle;
            }
            else
            {
                upper = middle;
            }
        }

        var t = (lower + upper) * 0.5d;
        var y = Coordinate(t, bezier.Y1, bezier.Y2);
        return Math.Clamp(y, 0d, 1d);
    }

    private static double Coordinate(double t, double firstControl, double secondControl)
    {
        var oneMinusT = 1d - t;
        return (3d * oneMinusT * oneMinusT * t * firstControl)
            + (3d * oneMinusT * t * t * secondControl)
            + (t * t * t);
    }
}
