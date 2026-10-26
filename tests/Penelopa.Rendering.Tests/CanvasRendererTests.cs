using Penelopa.Core.Primitives;
using Penelopa.Rendering;
using SkiaSharp;
using Xunit;

namespace Penelopa.Rendering.Tests;

public class CanvasRendererTests
{
    private const int CanvasSize = 512;
    private const float Center = CanvasSize / 2f;

    [Fact]
    public void HitTest_AtCircleScreenPosition_ReturnsThatCircle()
    {
        var renderer = new CanvasRenderer();
        // World (20,20) renders at the canvas center offset: (256+20, 256-20).
        var circle = new Circle { CenterX = { Value = 20f }, CenterY = { Value = 20f }, Radius = { Value = 10f } };

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { circle });
        var hit = renderer.HitTest(Center + 20f, Center - 20f);

        Assert.Same(circle, hit);
    }

    [Fact]
    public void HitTest_AtMirroredScreenPosition_ReturnsNull()
    {
        var renderer = new CanvasRenderer();
        var circle = new Circle { CenterX = { Value = 20f }, CenterY = { Value = 20f }, Radius = { Value = 10f } };

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { circle });
        // CSS (20,20) maps to world (20-256, 256-20) = (-236, 236), outside.
        var hit = renderer.HitTest(20f, 20f);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_AtEmptyArea_ReturnsNull()
    {
        var renderer = new CanvasRenderer();
        var circle = new Circle { CenterX = { Value = 30f }, CenterY = { Value = 30f }, Radius = { Value = 5f } };

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { circle });
        var hit = renderer.HitTest(100f, 100f);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_TwoOverlappingShapes_RespectsZOrder()
    {
        var renderer = new CanvasRenderer();
        var bottom = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 50f }, Height = { Value = 50f } };
        var top = new Circle { CenterX = { Value = 25f }, CenterY = { Value = 25f }, Radius = { Value = 10f } };

        // Later primitives paint over earlier ones, so the circle wins at its center.
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { bottom, top });
        var hit = renderer.HitTest(Center + 25f, Center - 25f);

        Assert.Same(top, hit);
    }

    [Fact]
    public void HitTest_TwoOverlappingShapes_BackgroundWinsOutsideTopShape()
    {
        var renderer = new CanvasRenderer();
        var bottom = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 50f }, Height = { Value = 50f } };
        var top = new Circle { CenterX = { Value = 25f }, CenterY = { Value = 25f }, Radius = { Value = 10f } };

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { bottom, top });
        // Inside the rectangle but outside the circle (radius 10): bottom wins.
        var hit = renderer.HitTest(Center + 10f, Center - 10f);

        Assert.Same(bottom, hit);
    }

    [Fact]
    public void Render_AfterViewportResize_PreservesZoomAndPan()
    {
        var renderer = new CanvasRenderer();
        renderer.Render(NewSurface(), NewInfo(), 1f, Array.Empty<Primitive>());
        renderer.ZoomAt(100f, 200f, 2f);
        renderer.PanBy(15f, -5f);
        var before = renderer.CurrentViewTransform;

        var bitmap = new SKBitmap(new SKImageInfo(800, 600));
        renderer.Render(new SKCanvas(bitmap), new SKImageInfo(800, 600), 1f, Array.Empty<Primitive>());
        var after = renderer.CurrentViewTransform;

        Assert.Equal(800, after.ViewWidth);
        Assert.Equal(600, after.ViewHeight);
        Assert.Equal(before.Zoom, after.Zoom);
        Assert.Equal(before.PanX, after.PanX);
        Assert.Equal(before.PanY, after.PanY);
    }

    [Fact]
    public void SetDevicePixelRatio_PreservesZoomAndPan()
    {
        var renderer = new CanvasRenderer();
        renderer.Render(NewSurface(), NewInfo(), 1f, Array.Empty<Primitive>());
        renderer.ZoomAt(100f, 200f, 2f);
        renderer.PanBy(15f, -5f);
        var before = renderer.CurrentViewTransform;

        renderer.SetDevicePixelRatio(2f);
        var after = renderer.CurrentViewTransform;

        Assert.Equal(2f, after.DevicePixelRatio);
        Assert.Equal(before.Zoom, after.Zoom);
        Assert.Equal(before.PanX, after.PanX);
        Assert.Equal(before.PanY, after.PanY);
    }

    [Fact]
    public void InvalidPropertyInput_IsRejectedBeforeRendering()
    {
        var renderer = new CanvasRenderer();
        var circle = new Circle { CenterX = { Value = 20f }, CenterY = { Value = 20f }, Radius = { Value = 15f } };
        var rectangle = new Rectangle();

        // 属性面板/加载路径会在写入前拒绝非法值，渲染循环永远看不到逆包围盒
        Assert.False(circle.Radius.TrySetText("-5", out _));
        Assert.False(circle.Radius.TrySetText("NaN", out _));
        Assert.False(rectangle.Width.TrySetText("0", out _));
        Assert.False(rectangle.Height.TrySetText("Infinity", out _));

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { circle, rectangle }, new Primitive[] { circle });

        var hit = renderer.HitTestSelection(Center + 20f, Center - 20f, new Primitive[] { circle });
        Assert.Same(circle, hit.Primitive);
        Assert.Null(hit.Handle);
    }

    [Fact]
    public void HitTest_ContainerChild_AppliesContainerTransform()
    {
        var renderer = new CanvasRenderer();
        var container = Container.CreateOffset("c", 20f, 20f);
        var child = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        container.AddChild(child);

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { container });

        Assert.Same(child, renderer.HitTest(Center + 25f, Center - 25f));
        Assert.Null(renderer.HitTest(Center + 10f, Center - 10f));
    }

    [Fact]
    public void HitTest_ChildInsideRotatedContainer_UsesRotatedPosition()
    {
        var renderer = new CanvasRenderer();
        var container = Container.CreateRotation("r", 90f);
        var child = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        container.AddChild(child);

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { container });

        // Local (0..10, 0..10) rotated 90° around the origin → world (-10..0, 0..10).
        Assert.Same(child, renderer.HitTest(Center - 5f, Center - 5f));
        Assert.Null(renderer.HitTest(Center + 5f, Center - 5f));
    }

    [Fact]
    public void HitTest_Triangle_UsesColorKeyInterior()
    {
        var renderer = new CanvasRenderer();
        var triangle = new Triangle();
        triangle.Vertex1X.Value = 0f; triangle.Vertex1Y.Value = 0f;
        triangle.Vertex2X.Value = 10f; triangle.Vertex2Y.Value = 0f;
        triangle.Vertex3X.Value = 5f; triangle.Vertex3Y.Value = 10f;

        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { triangle });

        Assert.Same(triangle, renderer.HitTest(Center + 5f, Center - 3f));
        Assert.Null(renderer.HitTest(Center + 1f, Center - 9f));
    }

    [Fact]
    public void HitTest_AfterZoom_UsesSharedViewTransform()
    {
        var renderer = new CanvasRenderer();
        var circle = new Circle { CenterX = { Value = 10f }, CenterY = { Value = 0f }, Radius = { Value = 5f } };
        var surface = NewSurface();
        renderer.Render(surface, NewInfo(), 1f, new Primitive[] { circle });
        renderer.ZoomAt(Center, Center, 2f);
        renderer.Render(surface, NewInfo(), 1f, new Primitive[] { circle });

        // At 200% zoom the circle center is 20 CSS px right of the canvas center.
        Assert.Same(circle, renderer.HitTest(Center + 20f, Center));
        Assert.Null(renderer.HitTest(Center + 30f, Center));
    }

    [Fact]
    public void Render_WithoutHitBuffer_StillDrawsVisibleContent()
    {
        using var renderer = new CanvasRenderer();
        var circle = new Circle
        {
            CenterX = { Value = 20f },
            CenterY = { Value = 20f },
            Radius = { Value = 10f },
        };
        using var bitmap = new SKBitmap(new SKImageInfo(CanvasSize, CanvasSize));
        using var canvas = new SKCanvas(bitmap);

        renderer.Render(
            canvas,
            NewInfo(),
            1f,
            new Primitive[] { circle },
            renderHitBuffer: false);

        Assert.Null(renderer.HitTest(Center + 20f, Center - 20f));
        Assert.Equal(
            0xFFFFFFFFu,
            (uint)bitmap.GetPixel((int)Center + 20, (int)Center - 20));
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var renderer = new CanvasRenderer();

        renderer.Dispose();
        renderer.Dispose();
    }

    [Fact]
    public void HitTest_EmptyCanvas_ReturnsNull()
    {
        var renderer = new CanvasRenderer();

        renderer.Render(NewSurface(), NewInfo(), 1f, Array.Empty<Primitive>());
        var hit = renderer.HitTest(0f, 0f);

        Assert.Null(hit);
    }

    [Fact]
    public void HitTest_WithDevicePixelRatio2_MapsCssPixelsToPhysicalPixels()
    {
        var renderer = new CanvasRenderer();
        var circle = new Circle { CenterX = { Value = 20f }, CenterY = { Value = 20f }, Radius = { Value = 10f } };

        // SKGLView with IgnorePixelScaling=false reports the physical render
        // target size in e.Info (CSS size x dpr). The visible canvas must be
        // sized accordingly so the world maps to CSS 1:1.
        var physicalSize = CanvasSize * 2;
        var bitmap = new SKBitmap(new SKImageInfo(physicalSize, physicalSize));
        var canvas = new SKCanvas(bitmap);
        renderer.Render(canvas, new SKImageInfo(physicalSize, physicalSize), 2f, new Primitive[] { circle });

        // World (20,20) -> view pixel (512+40, 512-40) -> CSS (276, 236).
        var hit = renderer.HitTest(Center + 20f, Center - 20f);
        Assert.Same(circle, hit);

        // CSS top-left (20,20) maps to world (-236,236): outside the circle.
        Assert.Null(renderer.HitTest(20f, 20f));

        // The visible color is painted at the physical pixel (552, 472).
        var rendered = bitmap.GetPixel(physicalSize / 2 + 40, physicalSize / 2 - 40);
        Assert.Equal(0xFFFFFFFFu, (uint)rendered);
    }

    private static SKCanvas NewSurface()
    {
        var bitmap = new SKBitmap(new SKImageInfo(CanvasSize, CanvasSize));
        return new SKCanvas(bitmap);
    }

    private static SKImageInfo NewInfo() => new(CanvasSize, CanvasSize);
}
