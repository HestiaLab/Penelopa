using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using SkiaSharp;

namespace Penelopa.Rendering;

/// <summary>
/// 选择框与角柄的绘制/命中测试。角柄是固定屏幕尺寸元素，命中测试使用 CSS 像素，
/// 与世界缩放无关；旋转/剪切选区由调用方禁用角柄。
/// </summary>
internal sealed class SelectionOverlay : IDisposable
{
    private const float HandleSizeCss = 8f;
    private static readonly SKColor SelectionColor = new(0x4D, 0x9F, 0xFF);

    private readonly SKPaint _outline = new()
    {
        Color = SelectionColor,
        Style = SKPaintStyle.Stroke,
        IsAntialias = true,
    };

    private readonly SKPaint _handleFill = new()
    {
        Color = SelectionColor,
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    private bool _disposed;

    /// <summary>绘制选择框；<paramref name="drawHandles"/> 为 false 时只画框。</summary>
    public void Draw(SKCanvas canvas, ViewTransform view, Box bounds, bool drawHandles)
    {
        canvas.Save();
        try
        {
            // 世界模式：1 世界单位 = 1 CSS 像素 × Zoom，除以 Zoom 保持轮廓/角柄屏幕尺寸固定
            view.ApplyTo(canvas);
            float inverseZoom = 1f / view.Zoom;
            _outline.StrokeWidth = 1.5f * inverseZoom;
            canvas.DrawRect(new SKRect(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY), _outline);

            if (!drawHandles)
            {
                return;
            }

            foreach (var handle in SelectionBox.AllHandles)
            {
                var point = SelectionBox.HandlePoint(bounds, handle);
                float half = HandleSizeCss / 2f * inverseZoom;
                canvas.DrawRect(
                    new SKRect(point.X - half, point.Y - half, point.X + half, point.Y + half),
                    _handleFill);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>
    /// 测试 CSS 屏幕点是否命中角柄。命中半径随选区屏幕尺寸缩小：
    /// 大图元 5 CSS px，小图元缩到短边 20%（下限 1.5px），避免四角吞掉整个本体。
    /// </summary>
    public ResizeHandle? HitTestHandles(float screenCssX, float screenCssY, Box worldBounds, ViewTransform view)
    {
        var topLeft = ToCss(view, new Point(worldBounds.MinX, worldBounds.MaxY));
        var bottomRight = ToCss(view, new Point(worldBounds.MaxX, worldBounds.MinY));
        float hitRadiusCss = HandleHitRadius(
            MathF.Abs(bottomRight.X - topLeft.X),
            MathF.Abs(bottomRight.Y - topLeft.Y));

        foreach (var handle in SelectionBox.AllHandles)
        {
            var screen = ToCss(view, SelectionBox.HandlePoint(worldBounds, handle));
            if (MathF.Abs(screen.X - screenCssX) <= hitRadiusCss
                && MathF.Abs(screen.Y - screenCssY) <= hitRadiusCss)
            {
                return handle;
            }
        }

        return null;
    }

    /// <summary>测试 CSS 屏幕点是否在投影后的世界包围盒内（多选并集框拖拽柄）。</summary>
    public bool HitTestUnionBox(float screenCssX, float screenCssY, Box worldBounds, ViewTransform view)
    {
        var topLeft = ToCss(view, new Point(worldBounds.MinX, worldBounds.MaxY));
        var bottomRight = ToCss(view, new Point(worldBounds.MaxX, worldBounds.MinY));
        return screenCssX >= topLeft.X && screenCssX <= bottomRight.X
            && screenCssY >= topLeft.Y && screenCssY <= bottomRight.Y;
    }

    private static float HandleHitRadius(float screenWidth, float screenHeight)
    {
        const float maxHitRadiusCss = 5f;
        const float minHitRadiusCss = 1.5f;
        float shortSide = MathF.Min(screenWidth, screenHeight);
        return MathF.Min(maxHitRadiusCss, MathF.Max(minHitRadiusCss, shortSide * 0.2f));
    }

    private static SKPoint ToCss(ViewTransform view, Point world)
    {
        var screen = view.WorldToView(world.X, world.Y);
        return view.ViewToScreen(screen.X, screen.Y);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _outline.Dispose();
        _handleFill.Dispose();
    }
}
