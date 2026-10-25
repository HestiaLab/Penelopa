using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
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

    private readonly SKPath _trianglePath = new();
    private readonly SKPath _xArrowPath = new();
    private readonly SKPath _yArrowPath = new();

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

    /// <summary>绘制当前帧；调用方负责清空两张画布。</summary>
    public void Paint(
        SKCanvas canvas,
        SKCanvas hitCanvas,
        ViewTransform view,
        IReadOnlyList<Primitive> primitives,
        HitTestBuffer hitBuffer)
    {
        // 坐标轴在屏幕空间绘制：缩放/平移只移动原点，轴线始终跨越整个可见视口。
        // 先于图元绘制以保持轴位于内容下方，且不进入命中缓冲区。
        canvas.Save();
        try
        {
            DrawCoordinateSystem(canvas, view);
        }
        finally
        {
            canvas.Restore();
        }

        canvas.Save();
        hitCanvas.Save();
        try
        {
            view.ApplyTo(canvas);
            view.ApplyTo(hitCanvas);

            hitBuffer.BeginFrame();
            foreach (var primitive in primitives)
            {
                DrawNode(canvas, hitCanvas, primitive, hitBuffer);
            }
        }
        finally
        {
            canvas.Restore();
            hitCanvas.Restore();
        }
    }

    /// <summary>
    /// 绘制图元树节点。容器把变换同时施加到可见与命中画布，递归后恢复；
    /// 渲染顺序即 Z 序（后绘制覆盖先绘制）。
    /// </summary>
    private void DrawNode(SKCanvas canvas, SKCanvas hitCanvas, Primitive node, HitTestBuffer hitBuffer)
    {
        if (node is Container container)
        {
            var matrix = ToSkMatrix(container.LocalTransform);
            canvas.Save();
            hitCanvas.Save();
            canvas.Concat(ref matrix);
            hitCanvas.Concat(ref matrix);
            foreach (var child in container.Children)
            {
                DrawNode(canvas, hitCanvas, child, hitBuffer);
            }

            canvas.Restore();
            hitCanvas.Restore();
            return;
        }

        switch (node)
        {
            case Circle circle:
                _visibleFill.Color = new SKColor(circle.Color.Value);
                canvas.DrawCircle(circle.CenterX.Value, circle.CenterY.Value, circle.Radius.Value, _visibleFill);
                _hitFill.Color = ToHitColor(hitBuffer.Register(circle));
                hitCanvas.DrawCircle(circle.CenterX.Value, circle.CenterY.Value, circle.Radius.Value, _hitFill);
                break;

            case Rectangle rectangle:
                var rect = new SKRect(
                    rectangle.PosX.Value,
                    rectangle.PosY.Value,
                    rectangle.PosX.Value + rectangle.Width.Value,
                    rectangle.PosY.Value + rectangle.Height.Value);
                _visibleFill.Color = new SKColor(rectangle.Color.Value);
                canvas.DrawRect(rect, _visibleFill);
                _hitFill.Color = ToHitColor(hitBuffer.Register(rectangle));
                hitCanvas.DrawRect(rect, _hitFill);
                break;

            case Triangle triangle:
                _trianglePath.Reset();
                _trianglePath.MoveTo(triangle.Vertex1X.Value, triangle.Vertex1Y.Value);
                _trianglePath.LineTo(triangle.Vertex2X.Value, triangle.Vertex2Y.Value);
                _trianglePath.LineTo(triangle.Vertex3X.Value, triangle.Vertex3Y.Value);
                _trianglePath.Close();
                _visibleFill.Color = new SKColor(triangle.Color.Value);
                canvas.DrawPath(_trianglePath, _visibleFill);
                _hitFill.Color = ToHitColor(hitBuffer.Register(triangle));
                hitCanvas.DrawPath(_trianglePath, _hitFill);
                break;
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
        _textPaint.TextSize = 12f * dpr;

        if (xAxisVisible)
        {
            float arrowX = width - margin;
            _xArrowPath.Reset();
            _xArrowPath.MoveTo(arrowX, origin.Y);
            _xArrowPath.LineTo(arrowX - arrowSize, origin.Y - arrowSize * 0.6f);
            _xArrowPath.LineTo(arrowX - arrowSize, origin.Y + arrowSize * 0.6f);
            _xArrowPath.Close();
            canvas.DrawPath(_xArrowPath, _arrowPaint);

            float labelY = Math.Clamp(origin.Y + 16f * dpr, 16f * dpr, height - margin);
            float labelX = width - margin - arrowSize - _textPaint.MeasureText("X");
            canvas.DrawText("X", labelX, labelY, _textFont, _textPaint);
        }

        if (yAxisVisible)
        {
            float arrowY = margin;
            _yArrowPath.Reset();
            _yArrowPath.MoveTo(origin.X, arrowY);
            _yArrowPath.LineTo(origin.X - arrowSize * 0.6f, arrowY + arrowSize);
            _yArrowPath.LineTo(origin.X + arrowSize * 0.6f, arrowY + arrowSize);
            _yArrowPath.Close();
            canvas.DrawPath(_yArrowPath, _arrowPaint);

            float labelX = Math.Clamp(origin.X + 8f * dpr, margin, width - 16f * dpr);
            canvas.DrawText("Y", labelX, 16f * dpr, _textFont, _textPaint);
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
        _trianglePath.Dispose();
        _xArrowPath.Dispose();
        _yArrowPath.Dispose();
        _axisPaint.Dispose();
        _arrowPaint.Dispose();
        _textPaint.Dispose();
        _textFont.Dispose();
    }
}
