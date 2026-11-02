using System.Text.Json;
using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Xunit;

namespace Penelopa.Core.Tests;

public class DocumentFormatV3Tests
{
    [Fact]
    public void Serialize_WritesStableTypeIdsAndDocumentIdentity()
    {
        var container = Container.CreateRotation("Rotate", 15f);
        container.AddChild(new Circle());
        var documentId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var json = DocumentSerializer.Serialize(
            new Primitive[] { container },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: 7);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(3, root.GetProperty("Version").GetInt32());
        Assert.Equal(documentId, root.GetProperty("DocumentId").GetGuid());
        Assert.Equal(7, root.GetProperty("Revision").GetInt64());

        var primitives = root.GetProperty("Primitives");
        var outer = primitives[0];
        Assert.Equal("penelopa.container.rotation", outer.GetProperty("Type").GetString());
        Assert.False(outer.TryGetProperty("Kind", out _));
        Assert.Equal(
            "penelopa.circle",
            outer.GetProperty("Children")[0].GetProperty("Type").GetString());
    }

    [Fact]
    public void RoundTrip_PreservesIdentityRevisionAndExtensions()
    {
        var documentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        using var extensionDocument = JsonDocument.Parse("""{"acme.plugin":{"enabled":true}}""");
        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["acme.plugin"] = extensionDocument.RootElement
                .GetProperty("acme.plugin")
                .Clone(),
        };
        var circle = new Circle();
        var config = new SimulationConfig(
            false,
            new[]
            {
                new SimulationTrack(
                    circle.Id,
                    "Radius",
                    new[] { new SimulationKeyframe(0d, 5f, InterpolationKind.Linear) }),
            });

        var json = DocumentSerializer.Serialize(
            new Primitive[] { circle },
            new[] { circle },
            config,
            documentId,
            12,
            extensions);
        var result = DocumentSerializer.Deserialize(json)!;

        Assert.Equal(documentId, result.DocumentId);
        Assert.Equal(12, result.Revision);
        Assert.True(result.Extensions.ContainsKey("acme.plugin"));
        using (var extension = JsonDocument.Parse(
                   result.Extensions["acme.plugin"].GetRawText()))
        {
            Assert.True(extension.RootElement.GetProperty("enabled").GetBoolean());
        }

        Assert.Equal(circle.Id, Assert.Single(result.Roots).Id);
        Assert.Equal(5f, ((Circle)result.Roots[0]).Radius.Value);

        var roundTrip = DocumentSerializer.Serialize(
            result.Roots,
            result.ResolveSelection(),
            result.Simulation,
            result.DocumentId,
            result.Revision,
            result.Extensions);
        using var document = JsonDocument.Parse(roundTrip);
        Assert.True(document.RootElement.GetProperty("Extensions")
            .TryGetProperty("acme.plugin", out _));
    }

    [Fact]
    public void CustomPrimitiveType_RegisteredInRegistry_RoundTrips()
    {
        PrimitiveTypeRegistry.Default.Register<TestGauge>("test.gauge");

        var gauge = new TestGauge { Name = { Value = "Gauge" } };
        gauge.Length.Value = 42f;
        var json = DocumentSerializer.Serialize(
            new Primitive[] { gauge },
            Array.Empty<Primitive>(),
            documentId: Guid.Parse("33333333-3333-3333-3333-333333333333"));
        var result = DocumentSerializer.Deserialize(json)!;

        var loaded = Assert.IsType<TestGauge>(Assert.Single(result.Roots));
        Assert.Equal(42f, loaded.Length.Value);
    }

    [Fact]
    public void CustomPropValue_RegisteredConversion_RoundTrips()
    {
        PropValueRegistry.Default.Register<CustomMeterPropValue>(
            value => value.Meters,
            (value, boxed) => value.Meters = boxed is JsonElement element
                ? element.GetSingle()
                : Convert.ToSingle(boxed));
        PrimitiveTypeRegistry.Default.Register<TestMeter>("test.meter");

        var meter = new TestMeter();
        meter.Meters.Meters = 3.5f;
        var json = DocumentSerializer.Serialize(
            new Primitive[] { meter },
            Array.Empty<Primitive>(),
            documentId: Guid.Parse("44444444-4444-4444-4444-444444444444"));
        var result = DocumentSerializer.Deserialize(json)!;

        var loaded = Assert.IsType<TestMeter>(Assert.Single(result.Roots));
        Assert.Equal(3.5f, loaded.Meters.Meters);
    }

    [Fact]
    public void MissingDocumentId_GeneratesOne_WhenLoading()
    {
        const string json = """
        {
          "Version": 3,
          "Primitives": [
            { "Id": "00000000-0000-0000-0000-000000000001", "Type": "penelopa.rect" }
          ],
          "SelectionIds": []
        }
        """;

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.DocumentId);
    }

    [Fact]
    public void UnregisteredPropValueType_ReturnsNullInsteadOfThrowing()
    {
        PrimitiveTypeRegistry.Default.Register<UnregisteredMeter>("test.unregisteredMeter");
        const string json = """
        {
          "Version": 3,
          "DocumentId": "99999999-9999-9999-9999-999999999999",
          "Primitives": [
            {
              "Id": "00000000-0000-0000-0000-000000000001",
              "Type": "test.unregisteredMeter",
              "Props": { "Value": 1 }
            }
          ],
          "SelectionIds": []
        }
        """;

        var exception = Record.Exception(() => DocumentSerializer.Deserialize(json));

        Assert.Null(exception);
        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Revision_IncrementsOnEditUndoAndRedo()
    {
        var service = new PrimitiveService();
        var documentId = service.DocumentId;

        service.Add(new Circle());
        Assert.Equal(1, service.Revision);

        service.Undo();
        Assert.Equal(2, service.Revision);
        service.Redo();
        Assert.Equal(3, service.Revision);

        service.SetDocumentIdentity(Guid.Parse("55555555-5555-5555-5555-555555555555"), 42);
        Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555555"), service.DocumentId);
        Assert.Equal(42, service.Revision);
        Assert.NotEqual(Guid.Empty, documentId);
    }

    [Fact]
    public void CommandRegistry_KnowsEveryDocumentCommand()
    {
        foreach (var field in typeof(DocumentCommands)
                     .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            var commandId = Assert.IsType<string>(field.GetValue(null));
            Assert.True(
                CommandRegistry.Default.TryGetDisplayName(commandId, out var displayName),
                $"{commandId} is not registered.");
            Assert.False(string.IsNullOrWhiteSpace(displayName));
        }
    }

    private sealed class UnregisteredMeter : Primitive
    {
        public UnregisteredMeter() : base("Meter")
        {
            Value = new UnregisteredPropValue("Value");
            AddProp(Value);
        }

        public UnregisteredPropValue Value { get; }

        protected override Box GetLocalBoundingBox(
            IEffectiveValueSource? values)
            => new(0f, 0f, 1f, 1f);

        protected override void TranslateLocal(float deltaX, float deltaY)
        {
        }

        protected override Box SetBoundsLocal(Box bounds, Point anchor) => bounds;

        protected override bool ContainsLocalPoint(Point point) => false;
    }

    private sealed class UnregisteredPropValue : PropValue
    {
        public UnregisteredPropValue(string name) : base(name)
        {
        }

        public float Number { get; set; }
    }

    private sealed class TestGauge : Primitive
    {
        public TestGauge() : base("Gauge")
        {
            Length = new FloatPropValue("Length", 10f);
            AddProp(Length);
        }

        public FloatPropValue Length { get; }

        protected override Box GetLocalBoundingBox(
            IEffectiveValueSource? values)
            => new(0f, 0f, Length.Value, 10f);

        protected override void TranslateLocal(float deltaX, float deltaY)
        {
        }

        protected override Box SetBoundsLocal(Box bounds, Point anchor) => bounds;

        protected override bool ContainsLocalPoint(Point point) => false;
    }

    private sealed class TestMeter : Primitive
    {
        public TestMeter() : base("Meter")
        {
            Meters = new CustomMeterPropValue("Meters", 1f);
            AddProp(Meters);
        }

        public CustomMeterPropValue Meters { get; }

        protected override Box GetLocalBoundingBox(
            IEffectiveValueSource? values)
            => new(0f, 0f, 1f, 1f);

        protected override void TranslateLocal(float deltaX, float deltaY)
        {
        }

        protected override Box SetBoundsLocal(Box bounds, Point anchor) => bounds;

        protected override bool ContainsLocalPoint(Point point) => false;
    }

    private sealed class CustomMeterPropValue : PropValue
    {
        public CustomMeterPropValue(string name, float meters) : base(name)
        {
            Meters = meters;
        }

        public float Meters { get; set; }
    }
}
