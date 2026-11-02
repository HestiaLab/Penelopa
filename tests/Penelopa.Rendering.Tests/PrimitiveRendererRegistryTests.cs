using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Rendering;
using SkiaSharp;
using Xunit;

namespace Penelopa.Rendering.Tests;

public class PrimitiveRendererRegistryTests
{
    [Theory]
    [InlineData("penelopa.circle")]
    [InlineData("penelopa.rect")]
    [InlineData("penelopa.triangle")]
    public void BuiltInRenderer_IsRegistered(string typeId)
    {
        Assert.True(
            PrimitiveRendererRegistry.Default.TryGet(typeId, out var renderer));
        Assert.NotNull(renderer);
    }

    [Fact]
    public void CustomPrimitiveTypeAndRenderer_AreDiscoverableByStableId()
    {
        PrimitiveTypeRegistry.Default.Register<Gauge>("test.gauge");
        var renderer = new GaugeRenderer();
        PrimitiveRendererRegistry.Default.Register("test.gauge", renderer);
        var gauge = new Gauge();

        Assert.True(PrimitiveTypeRegistry.Default.TryCreate("test.gauge", out var created));
        Assert.IsType<Gauge>(created);
        Assert.True(PrimitiveTypeRegistry.Default.TryResolveTypeId(gauge, out var typeId));
        Assert.Equal("test.gauge", typeId);
        Assert.True(PrimitiveRendererRegistry.Default.TryGet(typeId, out var found));
        Assert.Same(renderer, found);
    }

    private sealed class Gauge : Primitive
    {
        public Gauge() : base("Gauge")
        {
            Length = new FloatPropValue("Length", 10f);
            AddProp(Length);
        }

        public FloatPropValue Length { get; }

        protected override Box GetLocalBoundingBox(IEffectiveValueSource? values)
            => new(0f, 0f, Length.Value, 10f);

        protected override void TranslateLocal(float deltaX, float deltaY)
        {
        }

        protected override Box SetBoundsLocal(Box bounds, Point anchor) => bounds;

        protected override bool ContainsLocalPoint(Point point) => false;
    }

    private sealed class GaugeRenderer : IPrimitiveRenderer
    {
        public SKColor GetColor(Primitive primitive) => SKColors.White;

        public void Draw(
            SKCanvas canvas,
            Primitive primitive,
            SKPaint paint,
            IEffectiveValueSource? values)
        {
        }
    }
}
