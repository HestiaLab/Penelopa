using System.Collections.Concurrent;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using SkiaSharp;

namespace Penelopa.Rendering;

/// <summary>按稳定 type id 绘制的图元渲染器；扩展图元注册自己的实现即可。</summary>
public interface IPrimitiveRenderer
{
    SKColor GetColor(Primitive primitive);

    void Draw(
        SKCanvas canvas,
        Primitive primitive,
        SKPaint paint,
        IEffectiveValueSource? values);
}

/// <summary>稳定 type id 到渲染器的注册表；核心类型在静态构造中注册。</summary>
public sealed class PrimitiveRendererRegistry
{
    private readonly ConcurrentDictionary<string, IPrimitiveRenderer> _renderers =
        new(StringComparer.Ordinal);

    public static PrimitiveRendererRegistry Default { get; } = CreateDefault();

    public void Register(string typeId, IPrimitiveRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(renderer);
        _renderers[typeId] = renderer;
    }

    public bool TryGet(string typeId, out IPrimitiveRenderer renderer)
        => _renderers.TryGetValue(typeId, out renderer!);

    private static PrimitiveRendererRegistry CreateDefault()
    {
        var registry = new PrimitiveRendererRegistry();
        registry.Register("penelopa.circle", new CircleRenderer());
        registry.Register("penelopa.rect", new RectangleRenderer());
        registry.Register("penelopa.triangle", new TriangleRenderer());
        return registry;
    }
}

internal static class PrimitiveRenderHelpers
{
    public static float Effective(
        IEffectiveValueSource? values,
        FloatPropValue property)
        => values?.GetFloat(property, property.Value) ?? property.Value;

    public static SKColor ColorOf(ColorPropValue color) => new(color.Value);
}

internal sealed class CircleRenderer : IPrimitiveRenderer
{
    public SKColor GetColor(Primitive primitive)
        => PrimitiveRenderHelpers.ColorOf(((Circle)primitive).Color);

    public void Draw(
        SKCanvas canvas,
        Primitive primitive,
        SKPaint paint,
        IEffectiveValueSource? values)
    {
        var circle = (Circle)primitive;
        canvas.DrawCircle(
            PrimitiveRenderHelpers.Effective(values, circle.CenterX),
            PrimitiveRenderHelpers.Effective(values, circle.CenterY),
            PrimitiveRenderHelpers.Effective(values, circle.Radius),
            paint);
    }
}

internal sealed class RectangleRenderer : IPrimitiveRenderer
{
    public SKColor GetColor(Primitive primitive)
        => PrimitiveRenderHelpers.ColorOf(((Rectangle)primitive).Color);

    public void Draw(
        SKCanvas canvas,
        Primitive primitive,
        SKPaint paint,
        IEffectiveValueSource? values)
    {
        var rectangle = (Rectangle)primitive;
        float posX = PrimitiveRenderHelpers.Effective(values, rectangle.PosX);
        float posY = PrimitiveRenderHelpers.Effective(values, rectangle.PosY);
        var rect = new SKRect(
            posX,
            posY,
            posX + PrimitiveRenderHelpers.Effective(values, rectangle.Width),
            posY + PrimitiveRenderHelpers.Effective(values, rectangle.Height));
        canvas.DrawRect(rect, paint);
    }
}

internal sealed class TriangleRenderer : IPrimitiveRenderer
{
    public SKColor GetColor(Primitive primitive)
        => PrimitiveRenderHelpers.ColorOf(((Triangle)primitive).Color);

    public void Draw(
        SKCanvas canvas,
        Primitive primitive,
        SKPaint paint,
        IEffectiveValueSource? values)
    {
        var triangle = (Triangle)primitive;
        using var builder = new SKPathBuilder();
        builder.MoveTo(
            PrimitiveRenderHelpers.Effective(values, triangle.Vertex1X),
            PrimitiveRenderHelpers.Effective(values, triangle.Vertex1Y));
        builder.LineTo(
            PrimitiveRenderHelpers.Effective(values, triangle.Vertex2X),
            PrimitiveRenderHelpers.Effective(values, triangle.Vertex2Y));
        builder.LineTo(
            PrimitiveRenderHelpers.Effective(values, triangle.Vertex3X),
            PrimitiveRenderHelpers.Effective(values, triangle.Vertex3Y));
        builder.Close();
        using var path = builder.Detach();
        canvas.DrawPath(path, paint);
    }
}
