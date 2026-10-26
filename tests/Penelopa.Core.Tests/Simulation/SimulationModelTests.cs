using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests.Simulation;

public class SimulationModelTests
{
    [Fact]
    public void Track_RequiresAtLeastOneKeyframe()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new SimulationTrack(Guid.NewGuid(), "PosX", Array.Empty<SimulationKeyframe>()));

        Assert.Contains("at least one", exception.Message);
    }

    [Fact]
    public void Track_CopiesKeyframeSource()
    {
        var source = new[] { Frame(0, 1), Frame(1, 2) };
        var track = new SimulationTrack(Guid.NewGuid(), "PosX", source);

        source[0] = Frame(0, 99);

        Assert.Equal(1f, track.Keyframes[0].Value);
    }

    [Fact]
    public void Config_CopiesTrackSource()
    {
        var rectangle = CreateRectangle();
        var track = Track(rectangle, "PosX", Frame(0, 10), Frame(1, 20));
        var source = new List<SimulationTrack> { track };

        var config = new SimulationConfig(false, source);
        source.Add(Track(rectangle, "PosY", Frame(0, 20), Frame(1, 30)));

        var onlyTrack = Assert.Single(config.Tracks);
        Assert.Same(track, onlyTrack);
    }

    [Fact]
    public void Keyframe_CubicBezierRequiresControlPoints()
    {
        Assert.Throws<ArgumentException>(() =>
            new SimulationKeyframe(0, 0, InterpolationKind.CubicBezier));
    }

    [Fact]
    public void Keyframe_NonCubicRejectsBezier()
    {
        Assert.Throws<ArgumentException>(() =>
            new SimulationKeyframe(
                0,
                0,
                InterpolationKind.Linear,
                new CubicBezier(0d, 0d, 1d, 1d)));
    }

    [Theory]
    [InlineData(0d, 0d, 1d, 1d, true)]
    [InlineData(-0.1d, 0d, 1d, 1d, false)]
    [InlineData(0d, 1.1d, 1d, 1d, false)]
    [InlineData(0d, 0d, 2d, 1d, false)]
    [InlineData(0d, 0d, 1d, -0.2d, false)]
    public void CubicBezier_UnitRange(
        double x1,
        double y1,
        double x2,
        double y2,
        bool expected)
    {
        Assert.Equal(expected, new CubicBezier(x1, y1, x2, y2).IsInUnitRange);
    }

    [Fact]
    public void CubicBezier_NonFiniteControlPointIsInvalid()
    {
        Assert.False(new CubicBezier(double.NaN, 0d, 1d, 1d).IsInUnitRange);
        Assert.False(new CubicBezier(0d, double.PositiveInfinity, 1d, 1d).IsInUnitRange);
    }

    [Fact]
    public void TrackKey_IsCaseSensitive()
    {
        var id = Guid.NewGuid();

        Assert.NotEqual(
            new SimulationTrackKey(id, "PosX"),
            new SimulationTrackKey(id, "posx"));
    }

    [Fact]
    public void PrimitiveIndex_ResolvesNestedFloatProperties()
    {
        var container = Container.CreateRotation("Rotate", 0f);
        var rectangle = CreateRectangle();
        container.AddChild(rectangle);

        var index = SimulationPrimitiveIndex.Create(new Primitive[] { container });

        Assert.True(index.TryGetPrimitive(rectangle.Id, out var found));
        Assert.Same(rectangle, found);
        Assert.True(index.TryGetFloatProperty(rectangle.Id, "PosX", out var posX));
        Assert.Same(rectangle.PosX, posX);
        Assert.True(index.TryGetFloatProperty(container.Id, "Rotation", out var rotation));
        Assert.Same(container.Rotation, rotation);
        Assert.Equal(2, index.Primitives.Count);
        Assert.Contains(
            index.FloatProperties,
            entry => ReferenceEquals(entry.Property, rectangle.PosX));
    }

    [Fact]
    public void PrimitiveIndex_ReturnsFalseForMissingPrimitiveOrProperty()
    {
        var rectangle = CreateRectangle();
        var index = SimulationPrimitiveIndex.Create(new Primitive[] { rectangle });

        Assert.False(index.TryGetPrimitive(Guid.NewGuid(), out _));
        Assert.False(index.TryGetFloatProperty(rectangle.Id, "Color", out _));
        Assert.False(index.TryGetFloatProperty(Guid.NewGuid(), "PosX", out _));
    }
}
