using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests.Simulation;

public class SimulationBoundsEstimatorTests
{
    [Fact]
    public void Estimate_ReturnsUnionOfBaseAndAnimatedBoundsAndRestoresValues()
    {
        var rectangle = CreateRectangle(posX: 10f, posY: 20f, width: 10f, height: 10f);
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)));

        var result = SimulationBoundsEstimator.Estimate(
            new Primitive[] { rectangle },
            config);

        Assert.NotNull(result);
        var bounds = result.Value;
        Assert.True(bounds.MinX <= 10f);
        Assert.True(bounds.MaxX >= 110f);
        Assert.True(bounds.MinY <= 20f);
        Assert.True(bounds.MaxY >= 30f);

        Assert.Equal(10f, rectangle.PosX.Value);
        Assert.Equal(20f, rectangle.PosY.Value);
        Assert.Equal(10f, rectangle.Width.Value);
        Assert.Equal(10f, rectangle.Height.Value);
    }

    [Fact]
    public void Estimate_NoActiveTracks_ReturnsBaseBounds()
    {
        var rectangle = CreateRectangle();
        var dormant = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[] { Frame(0, 10), Frame(1, 100) });

        var result = SimulationBoundsEstimator.Estimate(
            new Primitive[] { rectangle },
            Config(false, dormant));

        Assert.Equal(rectangle.GetWorldBoundingBox(), result!.Value);
    }

    [Fact]
    public void Estimate_ZeroDurationTrack_ReturnsBaseBounds()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10)));

        var result = SimulationBoundsEstimator.Estimate(
            new Primitive[] { rectangle },
            config);

        Assert.Equal(rectangle.GetWorldBoundingBox(), result!.Value);
    }

    [Fact]
    public void Estimate_InvalidConfig_ReturnsBaseBounds()
    {
        var circle = new Circle();
        var config = Config(
            false,
            Track(circle, "Radius", Frame(0, 5), Frame(1, -1)));
        var baseBounds = circle.GetWorldBoundingBox();

        var result = SimulationBoundsEstimator.Estimate(
            new Primitive[] { circle },
            config);

        Assert.Equal(baseBounds, result!.Value);
        Assert.Equal(5f, circle.Radius.Value);
    }

    [Fact]
    public void Estimate_ContainerRotationCoversEndpointPoses()
    {
        var container = Container.CreateRotation("Rotate", 0f);
        var rectangle = CreateRectangle(posX: 20f, posY: 0f, width: 10f, height: 10f);
        container.AddChild(rectangle);

        var baseBounds = container.GetWorldBoundingBox();
        container.Rotation.Value = 90f;
        var rotatedBounds = container.GetWorldBoundingBox();
        container.Rotation.Value = 0f;

        var config = Config(
            false,
            Track(container, "Rotation", Frame(0, 0), Frame(1, 90)));
        var result = SimulationBoundsEstimator.Estimate(
            new Primitive[] { container },
            config);

        Assert.NotNull(result);
        var bounds = result.Value;
        Assert.True(bounds.MinX <= MathF.Min(baseBounds.MinX, rotatedBounds.MinX) + 1e-4f);
        Assert.True(bounds.MinY <= MathF.Min(baseBounds.MinY, rotatedBounds.MinY) + 1e-4f);
        Assert.True(bounds.MaxX >= MathF.Max(baseBounds.MaxX, rotatedBounds.MaxX) - 1e-4f);
        Assert.True(bounds.MaxY >= MathF.Max(baseBounds.MaxY, rotatedBounds.MaxY) - 1e-4f);

        Assert.Equal(0f, container.Rotation.Value);
        Assert.Equal(20f, rectangle.PosX.Value);
    }

    [Fact]
    public void Estimate_MultipleRoots_UnionsTheirBounds()
    {
        var first = CreateRectangle(posX: 0f, posY: 0f);
        var second = CreateRectangle(posX: 100f, posY: 50f);

        var result = SimulationBoundsEstimator.Estimate(
            new Primitive[] { first, second },
            SimulationConfig.Empty);

        Assert.NotNull(result);
        var bounds = result!.Value;
        Assert.True(bounds.MinX <= 0f);
        Assert.True(bounds.MinY <= 0f);
        Assert.True(bounds.MaxX >= second.GetWorldBoundingBox().MaxX);
        Assert.True(bounds.MaxY >= second.GetWorldBoundingBox().MaxY);
        Assert.Equal(0f, first.PosX.Value);
        Assert.Equal(100f, second.PosX.Value);
    }

    [Fact]
    public void Estimate_MoreThanMaxFitSamples_DownsamplesAndRestoresValues()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frames(200, 5d, 10f)),
            Track(rectangle, "PosY", Frames(200, 4.9d, 20f)));

        var result = SimulationBoundsEstimator.Estimate(
            new Primitive[] { rectangle },
            config);

        Assert.NotNull(result);
        Assert.True(result!.Value.MaxX >= 209f);
        Assert.Equal(10f, rectangle.PosX.Value);
        Assert.Equal(20f, rectangle.PosY.Value);
    }

    [Fact]
    public void Estimate_EmptyRoots_ReturnsNull()
    {
        Assert.Null(SimulationBoundsEstimator.Estimate(
            Array.Empty<Primitive>(),
            SimulationConfig.Empty));
    }

    [Fact]
    public void Estimate_RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SimulationBoundsEstimator.Estimate(null!, SimulationConfig.Empty));
        Assert.Throws<ArgumentNullException>(() =>
            SimulationBoundsEstimator.Estimate(Array.Empty<Primitive>(), null!));
    }

    private static SimulationKeyframe[] Frames(
        int count,
        double duration,
        float firstValue)
        => Enumerable.Range(0, count)
            .Select(index => Frame(
                index * duration / (count - 1),
                firstValue + index))
            .ToArray();
}
