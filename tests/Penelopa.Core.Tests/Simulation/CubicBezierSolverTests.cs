using Penelopa.Core.Simulation;
using Xunit;

namespace Penelopa.Core.Tests.Simulation;

public class CubicBezierSolverTests
{
    [Theory]
    [InlineData(-1d, 0d)]
    [InlineData(2d, 1d)]
    public void Evaluate_ClampsOutsideUnitInterval(double progress, double expected)
    {
        Assert.Equal(expected, CubicBezierSolver.Evaluate(progress, CubicBezier.EaseIn), 12);
    }

    [Fact]
    public void Evaluate_ThrowsForNonFiniteProgress()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CubicBezierSolver.Evaluate(double.NaN, CubicBezier.EaseIn));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CubicBezierSolver.Evaluate(double.PositiveInfinity, CubicBezier.EaseIn));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void Evaluate_IdentityControlPointsReturnsProgress(double progress)
    {
        var identity = new CubicBezier(0d, 0d, 1d, 1d);

        Assert.Equal(progress, CubicBezierSolver.Evaluate(progress, identity), 9);
    }

    [Fact]
    public void Evaluate_PresetsAreMonotonicAndBounded()
    {
        var presets = new[]
        {
            CubicBezier.EaseIn,
            CubicBezier.EaseOut,
            CubicBezier.EaseInOut,
        };

        foreach (var preset in presets)
        {
            var previous = 0d;
            for (var step = 0; step <= 100; step++)
            {
                var value = CubicBezierSolver.Evaluate(step / 100d, preset);
                Assert.InRange(value, 0d, 1d);
                Assert.True(value >= previous - 1e-9, "Easing must be monotonic.");
                previous = value;
            }

            Assert.Equal(0d, CubicBezierSolver.Evaluate(0d, preset), 12);
            Assert.Equal(1d, CubicBezierSolver.Evaluate(1d, preset), 12);
        }
    }

    [Fact]
    public void Evaluate_EaseInIsBelowLinearAndEaseOutAbove()
    {
        Assert.True(CubicBezierSolver.Evaluate(0.5d, CubicBezier.EaseIn) < 0.5d);
        Assert.True(CubicBezierSolver.Evaluate(0.5d, CubicBezier.EaseOut) > 0.5d);
        Assert.Equal(0.5d, CubicBezierSolver.Evaluate(0.5d, CubicBezier.EaseInOut), 9);
    }
}
