using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests.Simulation;

public class SimulationEvaluatorTests
{
    [Fact]
    public void Evaluate_HoldsFirstAndLastValuesOutsideKeyframes()
    {
        var track = TrackWith(Frame(1, 10), Frame(2, 20));

        Assert.Equal(10f, SimulationEvaluator.Evaluate(track, 0));
        Assert.Equal(10f, SimulationEvaluator.Evaluate(track, 1));
        Assert.Equal(20f, SimulationEvaluator.Evaluate(track, 2));
        Assert.Equal(20f, SimulationEvaluator.Evaluate(track, 3));
    }

    [Fact]
    public void Evaluate_LinearlyInterpolates()
    {
        var track = TrackWith(Frame(0, 10), Frame(2, 30));

        Assert.Equal(20f, SimulationEvaluator.Evaluate(track, 1));
        Assert.Equal(17.5f, SimulationEvaluator.Evaluate(track, 0.75));
    }

    [Fact]
    public void Evaluate_SingleKeyframeIsConstant()
    {
        var track = TrackWith(Frame(5, 42));

        Assert.Equal(42f, SimulationEvaluator.Evaluate(track, 0));
        Assert.Equal(42f, SimulationEvaluator.Evaluate(track, 5));
        Assert.Equal(42f, SimulationEvaluator.Evaluate(track, 10));
    }

    [Fact]
    public void Evaluate_UsesBinarySearchAcrossManyKeyframes()
    {
        var keyframes = Enumerable.Range(0, 10)
            .Select(index => Frame(index, index * 10))
            .ToArray();
        var track = TrackWith(keyframes);

        Assert.Equal(45f, SimulationEvaluator.Evaluate(track, 4.5));
        Assert.Equal(50f, SimulationEvaluator.Evaluate(track, 5));
    }

    [Fact]
    public void Evaluate_StepHoldsUntilNextKeyframe()
    {
        var track = TrackWith(
            Frame(0, 0, InterpolationKind.Step),
            Frame(1, 100),
            Frame(2, 200));

        Assert.Equal(0f, SimulationEvaluator.Evaluate(track, 0.999));
        Assert.Equal(100f, SimulationEvaluator.Evaluate(track, 1));
        Assert.Equal(150f, SimulationEvaluator.Evaluate(track, 1.5));
    }

    [Fact]
    public void Evaluate_CubicBezierUsesControlPoints()
    {
        var track = TrackWith(
            Frame(
                0,
                0,
                InterpolationKind.CubicBezier,
                new CubicBezier(0d, 0d, 1d, 1d)),
            Frame(1, 100));

        Assert.Equal(50d, SimulationEvaluator.Evaluate(track, 0.5), 3);
        Assert.Equal(100f, SimulationEvaluator.Evaluate(track, 1));
    }

    [Theory]
    [InlineData(InterpolationKind.Linear)]
    [InlineData(InterpolationKind.EaseIn)]
    [InlineData(InterpolationKind.EaseOut)]
    [InlineData(InterpolationKind.EaseInOut)]
    [InlineData(InterpolationKind.Step)]
    [InlineData(InterpolationKind.CubicBezier)]
    public void Evaluate_AllModesStayBetweenEndpoints(InterpolationKind interpolation)
    {
        CubicBezier? bezier = interpolation == InterpolationKind.CubicBezier
            ? new CubicBezier(0.42d, 0d, 0.58d, 1d)
            : null;
        var track = TrackWith(Frame(0, 0, interpolation, bezier), Frame(1, 100));

        for (var step = 0; step <= 20; step++)
        {
            Assert.InRange(
                SimulationEvaluator.Evaluate(track, step / 20d),
                0f,
                100f);
        }
    }

    [Fact]
    public void Ease_StepReturnsZeroAndUnknownThrows()
    {
        Assert.Equal(0d, SimulationEvaluator.Ease(0.5, Frame(0, 0, InterpolationKind.Step)));

        var unknown = Frame(0, 0, (InterpolationKind)999);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationEvaluator.Ease(0.5, unknown));
    }

    [Fact]
    public void Evaluate_RejectsNullTrackAndNonFiniteTime()
    {
        var track = TrackWith(Frame(0, 0), Frame(1, 1));

        Assert.Throws<ArgumentNullException>(() =>
            SimulationEvaluator.Evaluate(null!, 0d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationEvaluator.Evaluate(track, double.NaN));
        Assert.Throws<ArgumentNullException>(() =>
            SimulationEvaluator.Ease(0d, null!));
    }

    private static SimulationTrack TrackWith(params SimulationKeyframe[] keyframes)
        => new(Guid.NewGuid(), "Value", keyframes);
}
