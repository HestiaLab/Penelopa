using Penelopa.Core.Primitives;
using SkiaSharp;

namespace Penelopa.Rendering;

/// <summary>
/// 屏下颜色键命中缓冲区。每帧重建颜色键映射；键只需在单次“渲染→命中”周期内一致，
/// 因此不需要持久全局注册表。位图由渲染器在尺寸变化时统一重建。
/// </summary>
internal sealed class HitTestBuffer : IDisposable
{
    /// <summary>第一个有效颜色键；黑色 0xFF000000 保留给背景，永远不可命中。</summary>
    internal const uint FirstColorKey = 0xFF000001;

    /// <summary>最后一个有效颜色键；用尽后拒绝绘制，避免回绕到背景色或透明键。</summary>
    internal const uint LastColorKey = 0xFFFFFFFF;

    /// <summary>单帧可分配的颜色键总数（约 1677 万）。</summary>
    internal const int MaxColorKeys = (int)(LastColorKey - FirstColorKey + 1);

    private readonly Dictionary<uint, Primitive> _colorMap = new();
    private readonly uint _firstColorKey;
    private SKBitmap _bitmap;

    // 使用 64 位游标，0xFFFFFFFF 之后不会回绕到 0；越界在 Register 中显式抛错。
    private long _nextColorKey;
    private bool _disposed;

    public HitTestBuffer(int width, int height) : this(width, height, FirstColorKey)
    {
    }

    /// <summary>测试用构造：允许指定起始键以验证容量边界。</summary>
    internal HitTestBuffer(int width, int height, uint firstColorKey)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (firstColorKey < FirstColorKey || firstColorKey > LastColorKey)
        {
            throw new ArgumentOutOfRangeException(nameof(firstColorKey));
        }

        _firstColorKey = firstColorKey;
        _nextColorKey = firstColorKey;
        _bitmap = new SKBitmap(new SKImageInfo(width, height));
    }

    /// <summary>当前命中位图（由渲染器在其上创建 SKCanvas）。</summary>
    public SKBitmap Bitmap => _bitmap;

    /// <summary>调整位图尺寸；由渲染器在尺寸变化时调用。</summary>
    public void EnsureSize(int width, int height)
    {
        if (_bitmap.Width == width && _bitmap.Height == height)
        {
            return;
        }

        _bitmap.Dispose();
        _bitmap = new SKBitmap(new SKImageInfo(width, height));
    }

    /// <summary>开始新一帧：清空映射并从头分配颜色键。</summary>
    public void BeginFrame()
    {
        _colorMap.Clear();
        _nextColorKey = _firstColorKey;
    }

    /// <summary>为图元分配本帧颜色键并登记映射；容量耗尽时抛出可诊断异常。</summary>
    public uint Register(Primitive primitive)
    {
        if (_nextColorKey > LastColorKey)
        {
            throw new InvalidOperationException(
                $"Color-key capacity of {MaxColorKeys} primitives per frame is exhausted.");
        }

        var key = (uint)_nextColorKey++;
        _colorMap[key] = primitive;
        return key;
    }

    /// <summary>按物理像素坐标查找图元；越界或未登记时返回 null。</summary>
    public Primitive? HitTest(int pixelX, int pixelY)
    {
        if (_disposed
            || (uint)pixelX >= (uint)_bitmap.Width
            || (uint)pixelY >= (uint)_bitmap.Height)
        {
            return null;
        }

        var colorKey = (uint)_bitmap.GetPixel(pixelX, pixelY);
        return _colorMap.TryGetValue(colorKey, out var primitive) ? primitive : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _colorMap.Clear();
        _bitmap.Dispose();
    }
}
