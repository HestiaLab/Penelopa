using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using SkiaSharp;

namespace Penelopa.Rendering;

/// <summary>
/// 渲染编排器：持有绘制位图与命中缓冲区，协调视口、场景绘制和选择覆盖层。
/// 可见画布与命中缓冲区共享 <see cref="ViewTransform"/>，因此指针坐标、渲染与命中始终一致。
/// 渲染目标尺寸由宿主从 SKGLView 事件 (<c>e.Info</c>) 提供。
/// </summary>
public sealed class CanvasRenderer : IDisposable
{
    private const int FallbackCanvasSize = 512;

    private bool _disposed;

    private SKBitmap _drawBitmap = new(new SKImageInfo(FallbackCanvasSize, FallbackCanvasSize));
    private SKCanvas _drawCanvas;
    private SKCanvas _hitCanvas;

    private readonly ViewportController _viewport = new(FallbackCanvasSize, FallbackCanvasSize, 1f);
    private readonly HitTestBuffer _hitBuffer = new(FallbackCanvasSize, FallbackCanvasSize);
    private readonly ScenePainter _scenePainter = new();
    private readonly SelectionOverlay _selectionOverlay = new();

    public CanvasRenderer()
    {
        _drawCanvas = new SKCanvas(_drawBitmap);
        _hitCanvas = new SKCanvas(_hitBuffer.Bitmap);
    }

    /// <summary>将图元渲染到画布并刷新命中缓冲区。</summary>
    /// <param name="canvas">SKGLView 绘制事件的目标画布。</param>
    /// <param name="info">用户可见的渲染目标尺寸
    /// (<c>SKPaintGLSurfaceEventArgs.Info</c>，<c>IgnorePixelScaling</c> 为 false 时为物理像素)。
    /// 不要传缩放表面的原始 info，否则坐标空间会不一致。</param>
    /// <param name="devicePixelRatio">CSS 到物理像素的比率（<c>window.devicePixelRatio</c>）。</param>
    /// <param name="primitives">要绘制的图元。</param>
    /// <param name="selection">当前选区；非空时在最上层绘制选区框（单项时含角柄）。</param>
    public void Render(
        SKCanvas canvas,
        SKImageInfo info,
        float devicePixelRatio,
        IReadOnlyList<Primitive> primitives,
        IReadOnlyList<Primitive>? selection = null)
    {
        EnsureBuffersFor(info.Width, info.Height, devicePixelRatio);
        canvas.Clear(SKColors.Black);
        _drawCanvas.Clear(SKColors.Black);
        _hitCanvas.Clear(SKColors.Black);

        _scenePainter.Paint(_drawCanvas, _hitCanvas, _viewport.Current, primitives, _hitBuffer);
        canvas.DrawBitmap(_drawBitmap, 0, 0);

        if (selection is { Count: > 0 })
        {
            var bounds = selection.Count == 1
                ? selection[0].GetWorldBoundingBox()
                : MergeBounds(selection);
            var drawHandles = selection.Count == 1 && selection[0].CanResizeInWorld;
            _selectionOverlay.Draw(canvas, _viewport.Current, bounds, drawHandles);
        }
    }

    /// <summary>当前视口变换（缩放/平移状态）。</summary>
    public ViewTransform CurrentViewTransform => _viewport.Current;

    /// <summary>替换视口变换（缩放/平移）。</summary>
    public void SetViewTransform(ViewTransform transform) => _viewport.SetViewTransform(transform);

    /// <summary>缩放到 <paramref name="newZoom"/>，保持 CSS 光标下的世界点固定（围绕指针滚轮缩放）。</summary>
    public void ZoomAt(float cssX, float cssY, float newZoom)
        => _viewport.ZoomAt(cssX, cssY, newZoom);

    /// <summary>按 CSS 像素增量平移视口。</summary>
    public void PanBy(float cssDx, float cssDy) => _viewport.PanBy(cssDx, cssDy);

    /// <summary>将内容边界适配到视口（带内边距），居中显示内容（空内容不变）。</summary>
    public void FitToContent(IReadOnlyList<Primitive> primitives, float paddingCss = 40f)
    {
        if (primitives.Count == 0)
        {
            return;
        }

        _viewport.FitToContent(MergeBounds(primitives), paddingCss);
    }

    /// <summary>更新命中测试使用的设备像素比；保留当前缩放/平移。</summary>
    public void SetDevicePixelRatio(float devicePixelRatio)
        => _viewport.SetDevicePixelRatio(devicePixelRatio);

    private void EnsureBuffersFor(int width, int height, float devicePixelRatio)
    {
        var safeWidth = width > 0 ? width : FallbackCanvasSize;
        var safeHeight = height > 0 ? height : FallbackCanvasSize;
        if (_drawBitmap.Width != safeWidth || _drawBitmap.Height != safeHeight)
        {
            _drawCanvas.Dispose();
            _hitCanvas.Dispose();
            _drawBitmap.Dispose();
            _drawBitmap = new SKBitmap(new SKImageInfo(safeWidth, safeHeight));
            _hitBuffer.EnsureSize(safeWidth, safeHeight);
            _drawCanvas = new SKCanvas(_drawBitmap);
            _hitCanvas = new SKCanvas(_hitBuffer.Bitmap);
        }

        _viewport.EnsureView(safeWidth, safeHeight, devicePixelRatio);
    }

    /// <summary>返回 CSS 屏幕像素下的图元，像素为空时返回 null。</summary>
    public Primitive? HitTest(float screenCssX, float screenCssY)
    {
        var view = _viewport.Current.ScreenToView(screenCssX, screenCssY);
        return _hitBuffer.HitTest((int)view.X, (int)view.Y);
    }

    /// <summary>测试 CSS 屏幕点是否命中选区框的角柄。</summary>
    public ResizeHandle? HitTestHandles(float screenCssX, float screenCssY, Box worldBounds)
        => _selectionOverlay.HitTestHandles(screenCssX, screenCssY, worldBounds, _viewport.Current);

    /// <summary>测试 CSS 屏幕点是否在多选并集框内（整组拖拽柄）。</summary>
    public bool HitTestUnionBox(float screenCssX, float screenCssY, Box worldBounds)
        => _selectionOverlay.HitTestUnionBox(screenCssX, screenCssY, worldBounds, _viewport.Current);

    /// <summary>交互层的分层命中测试：角柄（单选）→ 图元颜色键 → 多选并集框。</summary>
    public HitTestResult HitTestSelection(float screenCssX, float screenCssY, IReadOnlyList<Primitive> selection)
    {
        if (selection.Count == 1 && selection[0].CanResizeInWorld)
        {
            var bounds = selection[0].GetWorldBoundingBox();
            var handle = _selectionOverlay.HitTestHandles(screenCssX, screenCssY, bounds, _viewport.Current);
            if (handle is not null)
            {
                return new HitTestResult { Handle = handle, Primitive = selection[0] };
            }
        }

        var primitive = HitTest(screenCssX, screenCssY);
        if (primitive is not null)
        {
            return new HitTestResult
            {
                Primitive = primitive,
                Candidates = PickAll(screenCssX, screenCssY),
            };
        }

        if (selection.Count > 1)
        {
            return new HitTestResult
            {
                InUnionBox = _selectionOverlay.HitTestUnionBox(
                    screenCssX,
                    screenCssY,
                    MergeBounds(selection),
                    _viewport.Current),
            };
        }

        return default;
    }

    /// <summary>CSS 屏幕点 → 世界坐标。</summary>
    public Point CssToWorld(float screenCssX, float screenCssY)
        => _viewport.CssToWorld(screenCssX, screenCssY);

    /// <summary>
    /// 收集某点的钻取候选：颜色键缓冲区的最顶层图元 + 其祖先链，
    /// 从最外层容器（根）到最深叶子排序。
    /// </summary>
    public IReadOnlyList<Primitive> PickAll(float screenCssX, float screenCssY)
    {
        var leaf = HitTest(screenCssX, screenCssY);
        if (leaf is null)
        {
            return Array.Empty<Primitive>();
        }

        var chain = new List<Primitive>();
        for (var node = leaf; node is not null; node = node.Parent)
        {
            chain.Add(node);
        }

        chain.Reverse();
        return chain;
    }

    private static Box MergeBounds(IReadOnlyList<Primitive> primitives)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var primitive in primitives)
        {
            var box = primitive.GetWorldBoundingBox();
            minX = MathF.Min(minX, box.MinX);
            minY = MathF.Min(minY, box.MinY);
            maxX = MathF.Max(maxX, box.MaxX);
            maxY = MathF.Max(maxY, box.MaxY);
        }

        return new Box(minX, minY, maxX, maxY);
    }

    /// <summary>释放位图、画布、画笔、字体与命中映射；重复调用安全。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _drawCanvas.Dispose();
        _hitCanvas.Dispose();
        _drawBitmap.Dispose();
        _hitBuffer.Dispose();
        _scenePainter.Dispose();
        _selectionOverlay.Dispose();
    }
}
