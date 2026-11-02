using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;
using Xunit;

namespace Penelopa.Core.Tests;

public class EffectiveValueHitTests
{
    [Fact]
    public void CircleHit_UsesOverlayCoordinates()
    {
        var circle = new Circle
        {
            CenterX = { Value = 10f },
            CenterY = { Value = 10f },
            Radius = { Value = 5f },
        };
        var overlay = new SimulationOverlay();
        overlay.Set(circle.CenterX, 100f);
        overlay.Set(circle.CenterY, 100f);

        Assert.True(circle.ContainsWorldPoint(new Point(100f, 100f), overlay));
        Assert.False(circle.ContainsWorldPoint(new Point(10f, 10f), overlay));
    }

    [Fact]
    public void ContainerHit_UsesOverlayTransformForChildren()
    {
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle
        {
            PosX = { Value = 0f },
            PosY = { Value = 0f },
            Width = { Value = 10f },
            Height = { Value = 10f },
        };
        container.AddChild(child);
        var overlay = new SimulationOverlay();
        overlay.Set(container.OffsetX, 50f);

        Assert.True(container.ContainsWorldPoint(new Point(55f, 5f), overlay));
        Assert.False(container.ContainsWorldPoint(new Point(55f, 5f)));
    }
}
