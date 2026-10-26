using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests.Simulation;

public class SimulationTimelineTests
{
    [Fact]
    public void GetDuration_UsesLatestLastKeyframe()
    {
        var first = TrackWith(Frame(0, 0), Frame(2, 1), Frame(5, 2));
        var second = TrackWith(Frame(1, 0), Frame(3, 1));

        Assert.Equal(5d, SimulationTimeline.GetDuration(new[] { first, second }));
    }

    [Fact]
    public void GetDuration_EmptyIsZero()
    {
        Assert.Equal(0d, SimulationTimeline.GetDuration(Array.Empty<SimulationTrack>()));
    }

    [Fact]
    public void GetDuration_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SimulationTimeline.GetDuration(null!));
    }

    [Fact]
    public void ClampAndStep_RespectBounds()
    {
        Assert.Equal(1.5d, SimulationTimeline.Clamp(1.5d, 2d));
        Assert.Equal(0d, SimulationTimeline.Clamp(-1d, 2d));
        Assert.Equal(2d, SimulationTimeline.Clamp(3d, 2d));
        Assert.Equal(0d, SimulationTimeline.Clamp(1d, 0d));
        Assert.Equal(2d, SimulationTimeline.Step(1d, 1d, 2d));
        Assert.Equal(2d, SimulationTimeline.Step(2d, 1d, 2d));
    }

    [Theory]
    [InlineData(0.25d, 0.25d)]
    [InlineData(1.25d, 0.25d)]
    [InlineData(2d, 0d)]
    [InlineData(-0.25d, 0.75d)]
    public void Wrap_MapsIntoCycle(double time, double expected)
    {
        Assert.Equal(expected, SimulationTimeline.Wrap(time, 1d), 9);
    }

    [Fact]
    public void Wrap_ZeroOrNegativeDurationReturnsZero()
    {
        Assert.Equal(0d, SimulationTimeline.Wrap(1d, 0d));
        Assert.Equal(0d, SimulationTimeline.Wrap(1d, -1d));
    }

    [Fact]
    public void Methods_RejectNonFiniteInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationTimeline.Clamp(double.NaN, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationTimeline.Clamp(0d, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationTimeline.Clamp(0d, -1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationTimeline.Wrap(double.NaN, 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SimulationTimeline.Step(0d, double.NaN, 1d));
    }

    private static SimulationTrack TrackWith(params SimulationKeyframe[] keyframes)
        => new(Guid.NewGuid(), "Value", keyframes);
}
