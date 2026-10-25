using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using Penelopa.Rendering;
using SkiaSharp;
using Xunit;

namespace Penelopa.Rendering.Tests;

public class CanvasRendererSelectionTests
{
    private const int CanvasSize = 512;
    private const float Center = CanvasSize / 2f;

    [Fact]
    public void HitTestSelection_SingleSelection_HandleWinsOverBody()
    {
        var renderer = new CanvasRenderer();
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 50f }, Height = { Value = 50f } };
        var selection = new[] { rect };
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { rect }, selection);

        // World bottom-right corner (60, 10) → CSS (256+60, 256-10).
        var hit = renderer.HitTestSelection(Center + 60f, Center - 10f, selection);

        Assert.Equal(ResizeHandle.BottomRight, hit.Handle);
        Assert.Same(rect, hit.Primitive);
    }

    [Fact]
    public void HitTestSelection_HandleHitRadius_DetectsNearbyPresses()
    {
        var renderer = new CanvasRenderer();
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 50f }, Height = { Value = 50f } };
        var selection = new[] { rect };
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { rect }, selection);

        // A few CSS px inside the corner still counts as the handle.
        var hit = renderer.HitTestSelection(Center + 58f, Center - 8f, selection);

        Assert.Equal(ResizeHandle.BottomRight, hit.Handle);
    }

    [Fact]
    public void HitTestSelection_BodyHit_ReturnsPrimitive()
    {
        var renderer = new CanvasRenderer();
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 50f }, Height = { Value = 50f } };
        var selection = new[] { rect };
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { rect }, selection);

        // World center (35, 35) → CSS (291, 221).
        var hit = renderer.HitTestSelection(Center + 35f, Center - 35f, selection);

        Assert.Null(hit.Handle);
        Assert.Same(rect, hit.Primitive);
        Assert.False(hit.InUnionBox);
    }

    [Fact]
    public void HitTestSelection_EmptyAreaOutsideUnion_ReturnsNothing()
    {
        var renderer = new CanvasRenderer();
        var a = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 20f }, Height = { Value = 20f } };
        var b = new Rectangle { PosX = { Value = 40f }, PosY = { Value = 0f }, Width = { Value = 20f }, Height = { Value = 20f } };
        var selection = new[] { a, b };
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { a, b }, selection);

        // (90, 10) in world is outside the union box (0..60, 0..20).
        var hit = renderer.HitTestSelection(Center + 90f, Center - 10f, selection);

        Assert.Null(hit.Handle);
        Assert.Null(hit.Primitive);
        Assert.False(hit.InUnionBox);
    }

    [Fact]
    public void HitTestSelection_MultiSelectionUnionBox_HitInBlankArea()
    {
        var renderer = new CanvasRenderer();
        var a = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 20f }, Height = { Value = 20f } };
        var b = new Rectangle { PosX = { Value = 40f }, PosY = { Value = 0f }, Width = { Value = 20f }, Height = { Value = 20f } };
        var selection = new[] { a, b };
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { a, b }, selection);

        // World (30, 10) lies between the two rects: inside the union, no body.
        var hit = renderer.HitTestSelection(Center + 30f, Center - 10f, selection);

        Assert.Null(hit.Primitive);
        Assert.True(hit.InUnionBox);
    }

    public enum TinyShape
    {
        Circle,
        Rectangle,
        SmallRectangle,
    }

    [Theory]
    [InlineData(TinyShape.Circle)]
    [InlineData(TinyShape.Rectangle)]
    [InlineData(TinyShape.SmallRectangle)]
    public void HitTestSelection_TinySelection_CenterHitsBodyNotHandle(TinyShape shape)
    {
        Primitive primitive = shape switch
        {
            TinyShape.Circle => new Circle { CenterX = { Value = 10f }, CenterY = { Value = 10f }, Radius = { Value = 5f } },
            TinyShape.Rectangle => new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 10f }, Height = { Value = 10f } },
            TinyShape.SmallRectangle => new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 5f }, Height = { Value = 5f } },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        var renderer = new CanvasRenderer();
        var selection = new[] { primitive };
        renderer.Render(NewSurface(), NewInfo(), 1f, selection, selection);
        var bounds = primitive.GetWorldBoundingBox();
        var center = renderer.CurrentViewTransform.WorldToView(bounds.CenterX, bounds.CenterY);
        var screen = renderer.CurrentViewTransform.ViewToScreen(center.X, center.Y);

        var hit = renderer.HitTestSelection(screen.X, screen.Y, selection);

        Assert.Null(hit.Handle);
        Assert.Same(primitive, hit.Primitive);
    }

    [Fact]
    public void HitTestSelection_TinySelection_CornerStillResizes()
    {
        var renderer = new CanvasRenderer();
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 10f }, Height = { Value = 10f } };
        var selection = new[] { rect };
        renderer.Render(NewSurface(), NewInfo(), 1f, selection, selection);

        // Bottom-right corner world (20, 10) → CSS (276, 246).
        var hit = renderer.HitTestSelection(Center + 20f, Center - 10f, selection);

        Assert.Equal(ResizeHandle.BottomRight, hit.Handle);
    }

    [Fact]
    public void HitTestSelection_TinyContainer_CenterAllowsDrillDown()
    {
        var renderer = new CanvasRenderer();
        var container = Container.CreateOffset("c", 10f, 10f);
        var child = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        container.AddChild(child);
        var selection = new[] { container };
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { container }, selection);

        // 容器世界框 (10,10)-(20,20) 的中心
        var hit = renderer.HitTestSelection(Center + 15f, Center - 15f, selection);

        Assert.Null(hit.Handle);
        Assert.Same(child, hit.Primitive);
        Assert.NotNull(hit.Candidates);
        Assert.Equal(new Primitive[] { container, child }, hit.Candidates);
    }

    [Fact]
    public void HitTestSelection_TinySelectionAtZoomOut_CenterStillHitsBody()
    {
        var renderer = new CanvasRenderer();
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 10f }, Height = { Value = 10f } };
        var selection = new[] { rect };
        renderer.SetViewTransform(new ViewTransform(CanvasSize, CanvasSize, 1f, zoom: 0.5f));
        renderer.Render(NewSurface(), NewInfo(), 1f, selection, selection);

        // 5 CSS px 的选择框中心
        var worldCenter = rect.GetWorldBoundingBox().Center;
        var view = renderer.CurrentViewTransform.WorldToView(worldCenter.X, worldCenter.Y);
        var screen = renderer.CurrentViewTransform.ViewToScreen(view.X, view.Y);
        var hit = renderer.HitTestSelection(screen.X, screen.Y, selection);

        Assert.Null(hit.Handle);
        Assert.Same(rect, hit.Primitive);
    }

    [Fact]
    public void HitTestSelection_ChildOfRotatedContainer_DoesNotExposeResizeHandles()
    {
        var renderer = new CanvasRenderer();
        var container = Container.CreateRotation("r", 45f);
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 50f }, Height = { Value = 50f } };
        container.AddChild(rect);
        var selection = new[] { rect };
        renderer.Render(NewSurface(), NewInfo(), 1f, new Primitive[] { container }, selection);

        var bounds = rect.GetWorldBoundingBox();
        var view = renderer.CurrentViewTransform.WorldToView(bounds.MaxX, bounds.MinY);
        var screen = renderer.CurrentViewTransform.ViewToScreen(view.X, view.Y);
        var hit = renderer.HitTestSelection(screen.X, screen.Y, selection);

        Assert.Null(hit.Handle);
        Assert.False(rect.CanResizeInWorld);
    }

    [Fact]
    public void CssToWorld_RoundTripsThroughViewTransform()
    {
        var renderer = new CanvasRenderer();
        renderer.Render(NewSurface(), NewInfo(), 1f, Array.Empty<Primitive>());

        var world = renderer.CssToWorld(100f, 200f);

        Assert.Equal(100f - Center, world.X, 3);
        Assert.Equal(Center - 200f, world.Y, 3); // centered origin + y-flip
    }

    [Fact]
    public void SelectionOverlay_DrawsHandlesAtCorners()
    {
        var renderer = new CanvasRenderer();
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f }, Width = { Value = 50f }, Height = { Value = 50f } };
        var bitmap = new SKBitmap(new SKImageInfo(CanvasSize, CanvasSize));
        var canvas = new SKCanvas(bitmap);
        renderer.Render(canvas, NewInfo(), 1f, new Primitive[] { rect }, new[] { rect });

        // Bottom-right handle: world (60, 10) → pixel (316, 246), colored with
        // the selection color (0xFF4D9FFF).
        var pixel = bitmap.GetPixel((int)(Center + 60f), (int)(Center - 10f));
        Assert.Equal(0xFF4D9FFFu, (uint)pixel);
    }

    private static SKCanvas NewSurface()
    {
        var bitmap = new SKBitmap(new SKImageInfo(CanvasSize, CanvasSize));
        return new SKCanvas(bitmap);
    }

    private static SKImageInfo NewInfo() => new(CanvasSize, CanvasSize);
}
