using Penelopa.Core.Alignment;
using SkiaSharp;

namespace Penelopa.Rendering;

/// <summary>
/// 视口状态：渲染目标尺寸、DPR、缩放与平移。所有世界/屏幕/视图坐标换算都经由此类，
/// 渲染、命中测试和选择覆盖层共享同一个 <see cref="ViewTransform"/>。
/// </summary>
internal sealed class ViewportController
{
    private ViewTransform _transform;

    public ViewportController(int viewWidth, int viewHeight, float devicePixelRatio)
    {
        _transform = new ViewTransform(viewWidth, viewHeight, devicePixelRatio);
    }

    /// <summary>当前视口变换。</summary>
    public ViewTransform Current => _transform;

    /// <summary>替换视口变换（供宿主恢复/注入视图）。</summary>
    public void SetViewTransform(ViewTransform transform) => _transform = transform;

    /// <summary>同步渲染目标尺寸/DPR；尺寸变化时保留缩放与平移。</summary>
    public void EnsureView(int viewWidth, int viewHeight, float devicePixelRatio)
    {
        if (_transform.ViewWidth == viewWidth
            && _transform.ViewHeight == viewHeight
            && _transform.DevicePixelRatio == devicePixelRatio)
        {
            return;
        }

        _transform = new ViewTransform(
            viewWidth,
            viewHeight,
            devicePixelRatio,
            _transform.Zoom,
            _transform.PanX,
            _transform.PanY);
    }

    /// <summary>DPR 变化只更新像素比，保留用户当前的缩放/平移。</summary>
    public void SetDevicePixelRatio(float devicePixelRatio)
    {
        if (_transform.DevicePixelRatio == devicePixelRatio)
        {
            return;
        }

        _transform = new ViewTransform(
            _transform.ViewWidth,
            _transform.ViewHeight,
            devicePixelRatio,
            _transform.Zoom,
            _transform.PanX,
            _transform.PanY);
    }

    /// <summary>缩放到 <paramref name="newZoom"/>，保持 CSS 光标下的世界点固定。</summary>
    public void ZoomAt(float cssX, float cssY, float newZoom)
    {
        var view = _transform.ScreenToView(cssX, cssY);
        var world = _transform.ViewToWorld(view.X, view.Y);

        float halfW = _transform.ViewWidth / (2f * _transform.DevicePixelRatio);
        float halfH = _transform.ViewHeight / (2f * _transform.DevicePixelRatio);
        float panX = cssX - halfW - world.X * newZoom;
        float panY = cssY - halfH + world.Y * newZoom;
        _transform = new ViewTransform(
            _transform.ViewWidth,
            _transform.ViewHeight,
            _transform.DevicePixelRatio,
            newZoom,
            panX,
            panY);
    }

    /// <summary>按 CSS 像素增量平移视口。</summary>
    public void PanBy(float cssDx, float cssDy)
    {
        _transform = new ViewTransform(
            _transform.ViewWidth,
            _transform.ViewHeight,
            _transform.DevicePixelRatio,
            _transform.Zoom,
            _transform.PanX + cssDx,
            _transform.PanY + cssDy);
    }

    /// <summary>将内容边界适配到视口（带内边距）并居中显示。</summary>
    public void FitToContent(Box bounds, float paddingCss = 40f)
    {
        float viewportW = _transform.ViewWidth / _transform.DevicePixelRatio;
        float viewportH = _transform.ViewHeight / _transform.DevicePixelRatio;
        float contentW = MathF.Max(bounds.Width, 1f);
        float contentH = MathF.Max(bounds.Height, 1f);

        float zoom = MathF.Min(
            viewportW / (contentW + 2f * paddingCss),
            viewportH / (contentH + 2f * paddingCss));
        zoom = Math.Clamp(zoom, 0.05f, 4f);

        _transform = new ViewTransform(
            _transform.ViewWidth,
            _transform.ViewHeight,
            _transform.DevicePixelRatio,
            zoom,
            -bounds.CenterX * zoom,
            bounds.CenterY * zoom);
    }

    /// <summary>CSS 屏幕点 → 世界坐标。</summary>
    public Point CssToWorld(float screenCssX, float screenCssY)
    {
        var view = _transform.ScreenToView(screenCssX, screenCssY);
        var world = _transform.ViewToWorld(view.X, view.Y);
        return new Point(world.X, world.Y);
    }

    /// <summary>世界坐标 → CSS 屏幕点。</summary>
    public SKPoint ToCss(Point world)
    {
        var view = _transform.WorldToView(world.X, world.Y);
        return _transform.ViewToScreen(view.X, view.Y);
    }
}
