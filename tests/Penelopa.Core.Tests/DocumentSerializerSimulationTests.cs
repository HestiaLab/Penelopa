using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests;

public class DocumentSerializerSimulationTests
{
    [Fact]
    public void RoundTrip_SimulationConfig_PreservesTracksAndInterpolation()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            true,
            Track(
                rectangle,
                "PosX",
                Frame(0, 10, InterpolationKind.EaseInOut),
                Frame(
                    1,
                    50,
                    InterpolationKind.CubicBezier,
                    new CubicBezier(0.42d, 0d, 0.58d, 1d)),
                Frame(2, 100)),
            Track(
                rectangle,
                "PosY",
                Frame(0, 20, InterpolationKind.Step),
                Frame(1, 30)));

        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            config);
        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.NotNull(result!.Simulation);
        Assert.Empty(result.Warnings);
        var loaded = result.Simulation!;
        Assert.True(loaded.Loop);
        Assert.Equal(2, loaded.Tracks.Count);

        var posX = loaded.Tracks[0];
        Assert.Equal(rectangle.Id, posX.PrimitiveId);
        Assert.Equal("PosX", posX.PropertyName);
        Assert.Equal(3, posX.Keyframes.Count);
        Assert.Equal(InterpolationKind.EaseInOut, posX.Keyframes[0].Interpolation);
        Assert.Equal(InterpolationKind.CubicBezier, posX.Keyframes[1].Interpolation);
        Assert.Equal(0.42d, posX.Keyframes[1].Bezier!.Value.X1);
        Assert.Equal(1d, posX.Keyframes[1].Time);

        var posY = loaded.Tracks[1];
        Assert.Equal("PosY", posY.PropertyName);
        Assert.Equal(InterpolationKind.Step, posY.Keyframes[0].Interpolation);
    }

    [Fact]
    public void Serialize_WithoutSimulation_OmitsSection()
    {
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Rectangle() },
            Array.Empty<Primitive>());

        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("Version").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("Simulation", out _));
    }

    [Fact]
    public void Deserialize_VersionOne_IgnoresSimulationSection()
    {
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Rectangle"" }
          ],
          ""Simulation"": { ""Loop"": true, ""Tracks"": [ { ""PrimitiveId"": ""bad"" } ] },
          ""SelectionIds"": []
        }";

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Deserialize_InvalidSimulationSection_IsDroppedAndDocumentLoads()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)));
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            config);
        var node = JsonNode.Parse(json)!;
        node["Simulation"]!["Tracks"]![0]!["Keyframes"]![0]!["Time"] = -1;

        var result = DocumentSerializer.Deserialize(node.ToJsonString());

        Assert.NotNull(result);
        Assert.Single(result!.Roots);
        Assert.Null(result.Simulation);
        Assert.Single(result.Warnings);
        Assert.Contains("ignored", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_UnknownInterpolation_IsDroppedWithWarning()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)));
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            config);
        var node = JsonNode.Parse(json)!;
        node["Simulation"]!["Tracks"]![0]!["Keyframes"]![0]!["Interpolation"] = "bogus";

        var result = DocumentSerializer.Deserialize(node.ToJsonString());

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Single(result.Warnings);
        Assert.Contains("Unknown interpolation", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_OrphanTracks_ArePrunedWithWarning()
    {
        var rectangle = CreateRectangle();
        var active = Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100));
        var orphan = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[] { Frame(0, 10), Frame(1, 100) });
        var config = Config(false, active, orphan);
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            config);

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.NotNull(result!.Simulation);
        var track = Assert.Single(result.Simulation!.Tracks);
        Assert.Equal(rectangle.Id, track.PrimitiveId);
        Assert.Single(result.Warnings);
        Assert.Contains("Removed 1", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_ValidActiveAndStructurallyInvalidOrphan_KeepsActiveTracks()
    {
        var rectangle = CreateRectangle();
        var active = Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100));
        var orphan = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[] { Frame(0, 10), Frame(-1, 100) });
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            Config(true, active, orphan));

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.NotNull(result!.Simulation);
        Assert.True(result.Simulation!.Loop);
        var track = Assert.Single(result.Simulation.Tracks);
        Assert.Equal(rectangle.Id, track.PrimitiveId);
        Assert.Single(result.Warnings);
        Assert.Contains("Removed 1", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_ValidActiveAndMalformedOrphan_PrunesOrphanBeforeParsing()
    {
        var rectangle = CreateRectangle();
        var active = Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100));
        var orphan = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[] { Frame(0, 10), Frame(1, 100) });
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            Config(false, active, orphan));
        var node = JsonNode.Parse(json)!;
        node["Simulation"]!["Tracks"]![1]!["Keyframes"]![0]!["Interpolation"] = "bogus";

        var result = DocumentSerializer.Deserialize(node.ToJsonString());

        Assert.NotNull(result);
        Assert.NotNull(result!.Simulation);
        var track = Assert.Single(result.Simulation!.Tracks);
        Assert.Equal(rectangle.Id, track.PrimitiveId);
        Assert.Single(result.Warnings);
        Assert.Contains("Removed 1", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_StructurallyInvalidActive_StillDropsWholeSection()
    {
        var rectangle = CreateRectangle();
        var active = Track(rectangle, "PosX", Frame(0, 10), Frame(-1, 100));
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            Config(false, active));

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Single(result.Warnings);
        Assert.Contains("ignored", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_TooManyTracks_IsRejectedBeforePruning()
    {
        var tracks = new StringBuilder();
        for (var index = 0; index <= SimulationLimits.MaxTracks; index++)
        {
            if (index > 0)
            {
                tracks.Append(',');
            }

            tracks.Append(
                $"{{\"PrimitiveId\":\"{Guid.NewGuid()}\",\"Property\":\"PosX\",\"Keyframes\":[]}}");
        }

        var result = DocumentSerializer.Deserialize(SimulationDocument(
            $"{{\"Loop\":false,\"Tracks\":[{tracks}]}}"));

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Contains("at most", result.Warnings[0]);
        Assert.Contains("tracks", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_TooManyRawKeyframes_IsRejectedBeforePruning()
    {
        var keyframes = string.Join(
            ",",
            Enumerable.Range(0, SimulationLimits.MaxKeyframesTotal + 1)
                .Select(_ => "{\"Time\":0,\"Value\":0,\"Interpolation\":\"linear\"}"));

        var result = DocumentSerializer.Deserialize(SimulationDocument(
            $"{{\"Loop\":false,\"Tracks\":[{{\"PrimitiveId\":\"{Guid.NewGuid()}\",\"Property\":\"PosX\",\"Keyframes\":[{keyframes}]}}]}}"));

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Contains("at most", result.Warnings[0]);
        Assert.Contains("keyframes", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_NullTrackEntry_IsRejected()
    {
        var result = DocumentSerializer.Deserialize(SimulationDocument(
            "{\"Loop\":false,\"Tracks\":[null]}"));

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Contains("track is null", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_TrackWithoutProperty_IsRejected()
    {
        var result = DocumentSerializer.Deserialize(SimulationDocument(
            $"{{\"Loop\":false,\"Tracks\":[{{\"PrimitiveId\":\"{Guid.NewGuid()}\",\"Keyframes\":[]}}]}}"));

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Contains("missing a property name", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_ValueViolatingProperty_IsDroppedWithWarning()
    {
        var circle = new Circle();
        var config = Config(
            false,
            Track(circle, "Radius", Frame(0, 5), Frame(1, -1)));
        var json = DocumentSerializer.Serialize(
            new Primitive[] { circle },
            Array.Empty<Primitive>(),
            config);

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Single(result.Warnings);
        Assert.Contains("greater than", result.Warnings[0]);
    }

    [Fact]
    public void Deserialize_InvalidBezier_IsDroppedWithWarning()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(
                rectangle,
                "PosX",
                Frame(
                    0,
                    10,
                    InterpolationKind.CubicBezier,
                    new CubicBezier(1.5d, 0d, 0d, 1d)),
                Frame(1, 100)));
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            Array.Empty<Primitive>(),
            config);

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Single(result.Warnings);
        Assert.Contains("control points", result.Warnings[0]);
    }

    [Theory]
    [InlineData(@"{ ""Version"": 2, ""Primitives"": [null], ""SelectionIds"": [] }")]
    [InlineData(@"{ ""Version"": 2, ""Primitives"": [{ ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Container"", ""Kind"": ""Offset"", ""Children"": [null] }], ""SelectionIds"": [] }")]
    [InlineData(@"{ ""Version"": 2, ""Primitives"": [], ""SelectionIds"": [null] }")]
    public void Deserialize_NullArrayEntries_ReturnsNullWithoutThrowing(string json)
    {
        var exception = Record.Exception(() => DocumentSerializer.Deserialize(json));

        Assert.Null(exception);
        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    private static string SimulationDocument(string simulationJson)
        => $@"{{
          ""Version"": 2,
          ""Primitives"": [
            {{ ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Rectangle"" }}
          ],
          ""SelectionIds"": [],
          ""Simulation"": {simulationJson}
        }}";

    [Fact]
    public void Deserialize_VersionTwoWithoutSimulation_HasNoConfig()
    {
        const string json = @"{
          ""Version"": 2,
          ""Primitives"": [
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Rectangle"" }
          ],
          ""SelectionIds"": []
        }";

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.Null(result!.Simulation);
        Assert.Empty(result.Warnings);
    }
}
