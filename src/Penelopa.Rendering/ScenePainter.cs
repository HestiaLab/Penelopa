using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using SkiaSharp;

namespace Penelopa.Rendering;

/// <summary>
/// 绘制图元树与坐标轴到可见/命中两张画布。画笔、路径和字体在实例内复用，
/// 避免每个图元每帧分配；兼容层仍需保证可见绘制与命中绘制使用同一矩阵。
/// </summary>
internal sealed class ScenePainter : IDisposable
{
    // 单个画笔按图元切换颜色即可；Skia 在 Draw* 调用时消费画笔状态。
    private readonly SKPaint _visibleFill = new()
    {
        Style = SKPaintStyle.Fill,
        IsAntialias = true,
    };

    // 命中缓冲区必须精确：抗锯齿边缘会混入不匹配任何键的颜色，破坏 1px 边缘与顶层一致性。
    private readonly SKPaint _hitFill = new()
    {
        Style = SKPaintStyle.Fill,
        IsAntialias = false,
    };

    private readonly SKPathBuilder _pathBuilder = new();

    private readonly SKPaint _axisPaint = new()
    {
        Color = SKColors.Red,
        StrokeWidth = 2,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
    };

    private readonly SKPaint _arrowPaint = new()
    {
        Color = SKColors.Red,
        StrokeWidth = 2,
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
    };

    private readonly SKPaint _textPaint = new()
    {
        Color = SKColors.Red,
        IsAntialias = true,
    };

    private readonly SKFont _textFont = new()
    {
        Edging = SKFontEdging.Antialias,
    };

    private bool _disposed;

    /// <summary>
    /// 绘制当前帧；调用方负责清空需要绘制的画布。命中画布/缓冲区为 null 时
    /// 只绘制可见内容（仿真页的只读路径），不登记颜色键。
    /// </summary>
    public void Paint(
        SKCanvas canvas,
        SKCanvas? hitCanvas,
        ViewTransform view,
        IReadOnlyList<Primitive> primitives,
        HitTestBuffer? hitBuffer,
        bool drawAxes = true,
        IEffectiveValueSource? effectiveValues = null)
    {
        // 坐标轴在屏幕空间绘制：缩放/平移只移动原点，轴线始终跨越整个可见视口。
        // 先于图元绘制以保持轴位于内容下方，且不进入命中缓冲区。只读展示
        // 路径可关闭坐标轴，避免调试标记穿过成品画面。
        if (drawAxes)
        {
            canvas.Save();
            try
            {
                DrawCoordinateSystem(canvas, view);
            }
            finally
            {
                canvas.Restore();
            }
        }

        canvas.Save();
        if (hitCanvas is not null)
        {
            hitCanvas.Save();
        }

        try
        {
            view.ApplyTo(canvas);
            if (hitCanvas is not null)
            {
                view.ApplyTo(hitCanvas);
            }

            hitBuffer?.BeginFrame();
            foreach (var primitive in primitives)
            {
                DrawNode(canvas, hitCanvas, primitive, hitBuffer, effectiveValues);
            }
        }
        finally
        {
            canvas.Restore();
            if (hitCanvas is not null)
            {
                hitCanvas.Restore();
            }
        }
    }

    /// <summary>
    /// 绘制图元树节点。容器把变换同时施加到可见与命中画布，递归后恢复；
    /// 渲染顺序即 Z 序（后绘制覆盖先绘制）。
    /// </summary>
    private void DrawNode(
        SKCanvas canvas,
        SKCanvas? hitCanvas,
        Primitive node,
        HitTestBuffer? hitBuffer,
        IEffectiveValueSource? effectiveValues)
    {
        if (node is Container container)
        {
            var matrix = ToSkMatrix(container.GetLocalTransform(effectiveValues));
            canvas.Save();
            if (hitCanvas is not null)
            {
                hitCanvas.Save();
            }

            canvas.Concat(in matrix);
            if (hitCanvas is not null)
            {
                hitCanvas.Concat(in matrix);
            }

            foreach (var child in container.Children)
            {
                DrawNode(canvas, hitCanvas, child, hitBuffer, effectiveValues);
            }

            canvas.Restore();
            if (hitCanvas is not null)
            {
                hitCanvas.Restore();
            }

            return;
        }

        var hasHit = hitCanvas is not null && hitBuffer is not null;
        if (!PrimitiveTypeRegistry.Default.TryResolveTypeId(node, out var typeId)
            || !PrimitiveRendererRegistry.Default.TryGet(typeId, out var renderer))
        {
            return;
        }

        _visibleFill.Color = renderer.GetColor(node);
        renderer.Draw(canvas, node, _visibleFill, effectiveValues);
        if (hasHit)
        {
            _hitFill.Color = ToHitColor(hitBuffer!.Register(node));
            renderer.Draw(hitCanvas!, node, _hitFill, effectiveValues);
        }
    }

    private static SKColor ToHitColor(uint colorKey)
    {
        var color = Color.FromUint(colorKey);
        return new SKColor(color.R, color.G, color.B, color.A);
    }

    /// <summary>Core 仿射变换 → Skia 矩阵。</summary>
    private static SKMatrix ToSkMatrix(Transform transform)
    {
        var matrix = new SKMatrix
        {
            ScaleX = transform.A,
            SkewY = transform.B,
            SkewX = transform.C,
            ScaleY = transform.D,
            TransX = transform.Tx,
            TransY = transform.Ty,
        };
        matrix.Persp0 = 0;
        matrix.Persp1 = 0;
        matrix.Persp2 = 1;
        return matrix;
    }

    /// <summary>
    /// 在物理像素（屏幕）空间绘制坐标轴。原点取世界 (0,0) 的投影；
    /// 轴线、箭头和标签始终位于可见视口内，不随缩放缩短或被边缘裁切。
    /// </summary>
    private void DrawCoordinateSystem(SKCanvas canvas, ViewTransform view)
    {
        float width = view.ViewWidth;
        float height = view.ViewHeight;
        float dpr = view.DevicePixelRatio;
        var origin = view.WorldToView(0f, 0f);

        bool xAxisVisible = origin.Y >= 0f && origin.Y <= height;
        bool yAxisVisible = origin.X >= 0f && origin.X <= width;
        if (!xAxisVisible && !yAxisVisible)
        {
            return;
        }

        _axisPaint.StrokeWidth = 2f * dpr;
        if (xAxisVisible)
        {
            canvas.DrawLine(0f, origin.Y, width, origin.Y, _axisPaint);
        }

        if (yAxisVisible)
        {
            canvas.DrawLine(origin.X, 0f, origin.X, height, _axisPaint);
        }

        if (xAxisVisible && yAxisVisible)
        {
            canvas.DrawCircle(origin.X, origin.Y, 2.5f * dpr, _arrowPaint);
        }

        float margin = 4f * dpr;
        float arrowSize = 8f * dpr;
        _textFont.Size = 12f * dpr;

        if (xAxisVisible)
        {
            float arrowX = width - margin;
            _pathBuilder.Reset();
            _pathBuilder.MoveTo(arrowX, origin.Y);
            _pathBuilder.LineTo(arrowX - arrowSize, origin.Y - arrowSize * 0.6f);
            _pathBuilder.LineTo(arrowX - arrowSize, origin.Y + arrowSize * 0.6f);
            _pathBuilder.Close();
            using (var arrowPath = _pathBuilder.Detach())
            {
                canvas.DrawPath(arrowPath, _arrowPaint);
            }

            float labelY = Math.Clamp(origin.Y + 16f * dpr, 16f * dpr, height - margin);
            float labelX = width - margin - arrowSize - _textFont.MeasureText("X", _textPaint);
            canvas.DrawText("X", labelX, labelY, SKTextAlign.Left, _textFont, _textPaint);
        }

        if (yAxisVisible)
        {
            float arrowY = margin;
            _pathBuilder.Reset();
            _pathBuilder.MoveTo(origin.X, arrowY);
            _pathBuilder.LineTo(origin.X - arrowSize * 0.6f, arrowY + arrowSize);
            _pathBuilder.LineTo(origin.X + arrowSize * 0.6f, arrowY + arrowSize);
            _pathBuilder.Close();
            using (var arrowPath = _pathBuilder.Detach())
            {
                canvas.DrawPath(arrowPath, _arrowPaint);
            }

            float labelX = Math.Clamp(origin.X + 8f * dpr, margin, width - 16f * dpr);
            canvas.DrawText("Y", labelX, 16f * dpr, SKTextAlign.Left, _textFont, _textPaint);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _visibleFill.Dispose();
        _hitFill.Dispose();
        _pathBuilder.Dispose();
        _axisPaint.Dispose();
        _arrowPaint.Dispose();
        _textPaint.Dispose();
        _textFont.Dispose();
    }
}
