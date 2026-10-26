using Penelopa.Core.Primitives;
using SkiaSharp;
using Xunit;

namespace Penelopa.Rendering.Tests;

public class AxisAndColorKeyTests
{
    private const int Size = 512;

    [Fact]
    public void ColorKeys_StopAtCapacityInsteadOfWrapping()
    {
        var buffer = new HitTestBuffer(4, 4, 0xFFFFFFFE);
        var circle = new Circle();
        buffer.BeginFrame();

        Assert.Equal(0xFFFFFFFEu, buffer.Register(circle));
        Assert.Equal(0xFFFFFFFFu, buffer.Register(circle));

        var error = Assert.Throws<InvalidOperationException>(() => buffer.Register(circle));
        Assert.Contains("capacity", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ColorKeys_RestartFromFirstKeyOnTheNextFrame()
    {
        var buffer = new HitTestBuffer(4, 4);
        var circle = new Circle();
        buffer.BeginFrame();
        Assert.Equal(HitTestBuffer.FirstColorKey, buffer.Register(circle));

        buffer.BeginFrame();

        Assert.Equal(HitTestBuffer.FirstColorKey, buffer.Register(circle));
        Assert.Equal(0x00FFFFFF, HitTestBuffer.MaxColorKeys);
    }

    [Fact]
    public void ZoomedOutAxesStillSpanTheVisibleViewport()
    {
        var bitmap = Render(
            size => new ViewTransform(size, size, 1f, zoom: 0.5f),
            out _);

        // 旧实现按 halfW/halfH 世界长度绘制，0.5 倍缩放时轴线只覆盖中间一半。
        AssertRed(bitmap, 4, Size / 2);
        AssertRed(bitmap, Size - 4, Size / 2);
        AssertRed(bitmap, Size / 2, 4);
        AssertRed(bitmap, Size / 2, Size - 4);
    }

    [Fact]
    public void PannedAxesFollowTheOriginAndStillReachViewportEdges()
    {
        var bitmap = Render(
            size => new ViewTransform(size, size, 1f, zoom: 1f, panX: 100f, panY: 40f),
            out _);

        int originX = Size / 2 + 100;
        int originY = Size / 2 + 40;

        AssertRed(bitmap, 4, originY);
        AssertRed(bitmap, Size - 4, originY);
        AssertRed(bitmap, originX, 4);
        AssertRed(bitmap, originX, Size - 4);
        Assert.False(IsRed(bitmap.GetPixel(4, Size / 2)), "X axis must move with the pan");
    }

    [Fact]
    public void DevicePixelRatio2AxesSpanThePhysicalViewport()
    {
        var bitmap = Render(
            _ => new ViewTransform(Size * 2, Size * 2, 2f),
            out var info);

        Assert.Equal(Size * 2, info.Width);
        AssertRed(bitmap, 4, info.Height / 2);
        AssertRed(bitmap, info.Width - 4, info.Height / 2);
        AssertRed(bitmap, info.Width / 2, 4);
        AssertRed(bitmap, info.Width / 2, info.Height - 4);
    }

    [Fact]
    public void AxesAreNotInteractive()
    {
        using var renderer = new CanvasRenderer();
        renderer.Render(
            new SKCanvas(new SKBitmap(new SKImageInfo(Size, Size))),
            new SKImageInfo(Size, Size),
            1f,
            Array.Empty<Primitive>());
        renderer.SetViewTransform(new ViewTransform(Size, Size, 1f, zoom: 0.5f));
        renderer.Render(
            new SKCanvas(new SKBitmap(new SKImageInfo(Size, Size))),
            new SKImageInfo(Size, Size),
            1f,
            Array.Empty<Primitive>());

        Assert.Null(renderer.HitTest(4f, Size / 2f));
        Assert.Null(renderer.HitTest(Size / 2f, 4f));
        Assert.Null(renderer.HitTest(Size / 2f, Size / 2f));
    }

    [Fact]
    public void ReadOnlyPresentationCanHideTheDebugAxes()
    {
        using var renderer = new CanvasRenderer();
        var bitmap = new SKBitmap(new SKImageInfo(Size, Size));

        renderer.Render(
            new SKCanvas(bitmap),
            new SKImageInfo(Size, Size),
            1f,
            Array.Empty<Primitive>(),
            drawAxes: false);

        Assert.False(IsRed(bitmap.GetPixel(4, Size / 2)));
        Assert.False(IsRed(bitmap.GetPixel(Size / 2, 4)));
        Assert.False(IsRed(bitmap.GetPixel(Size / 2, Size / 2)));
    }

    private static SKBitmap Render(
        Func<int, ViewTransform> createView,
        out SKImageInfo info,
        out CanvasRenderer renderer)
    {
        renderer = new CanvasRenderer();
        var first = new SKBitmap(new SKImageInfo(Size, Size));
        renderer.Render(new SKCanvas(first), new SKImageInfo(Size, Size), 1f, Array.Empty<Primitive>());

        var view = createView(Size);
        renderer.SetViewTransform(view);
        info = new SKImageInfo(view.ViewWidth, view.ViewHeight);
        var bitmap = new SKBitmap(info);
        renderer.Render(new SKCanvas(bitmap), info, view.DevicePixelRatio, Array.Empty<Primitive>());
        return bitmap;
    }

    private static SKBitmap Render(Func<int, ViewTransform> createView, out SKImageInfo info)
    {
        var bitmap = Render(createView, out info, out var renderer);
        renderer.Dispose();
        return bitmap;
    }

    private static void AssertRed(SKBitmap bitmap, int x, int y)
        => Assert.True(IsRed(bitmap.GetPixel(x, y)), $"Expected a red axis pixel at ({x},{y}).");

    private static bool IsRed(SKColor color)
        => color.Red > 200 && color.Green < 80 && color.Blue < 80;
}
