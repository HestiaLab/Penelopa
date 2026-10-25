using System.Text;
using System.Text.Json;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class DocumentSerializerTests
{
    [Fact]
    public void RoundTrip_FlatPrimitives_PreservesGeometry()
    {
        var circle = new Circle
        {
            Name = { Value = "c" },
            CenterX = { Value = 12.5f },
            CenterY = { Value = -3f },
            Radius = { Value = 7f },
        };
        var rect = new Rectangle
        {
            Name = { Value = "r" },
            PosX = { Value = 1f },
            PosY = { Value = 2f },
            Width = { Value = 30f },
            Height = { Value = 20f },
        };

        var json = DocumentSerializer.Serialize(new Primitive[] { circle, rect }, new Primitive[] { circle });
        var result = DocumentSerializer.Deserialize(json)!;

        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(circle.Id, result.SelectionIds[0]);

        var c = Assert.IsType<Circle>(result.Roots[0]);
        Assert.Equal("c", c.Name.Value);
        Assert.Equal(12.5f, c.CenterX.Value);
        Assert.Equal(-3f, c.CenterY.Value);
        Assert.Equal(7f, c.Radius.Value);
    }

    [Fact]
    public void RoundTrip_NestedContainer_PreservesHierarchyAndKind()
    {
        var container = Container.CreateRotation("rot", 45f);
        var child = new Rectangle { PosX = { Value = 5f }, PosY = { Value = 6f }, Width = { Value = 10f }, Height = { Value = 10f } };
        container.AddChild(child);
        var outer = Container.CreateOffset("outer", 20f, 30f);
        outer.AddChild(container);

        var json = DocumentSerializer.Serialize(new Primitive[] { outer }, Array.Empty<Primitive>());
        var result = DocumentSerializer.Deserialize(json)!;

        var loadedOuter = Assert.IsType<Container>(result.Roots[0]);
        Assert.Equal(ContainerKind.Offset, loadedOuter.Kind);
        Assert.Equal(20f, loadedOuter.OffsetX.Value);
        var loadedInner = Assert.IsType<Container>(loadedOuter.Children[0]);
        Assert.Equal(ContainerKind.Rotation, loadedInner.Kind);
        Assert.Equal(45f, loadedInner.Rotation.Value);
        var loadedChild = Assert.IsType<Rectangle>(loadedInner.Children[0]);
        Assert.Equal(5f, loadedChild.PosX.Value);
        Assert.Equal(6f, loadedChild.PosY.Value);
        Assert.Same(loadedInner, loadedChild.Parent);
    }

    [Fact]
    public void Deserialize_InvalidJson_ReturnsNull()
    {
        Assert.Null(DocumentSerializer.Deserialize("not json {"));
    }

    [Fact]
    public void Deserialize_UnknownType_ReturnsNull()
    {
        var json = "{\"Primitives\": [{\"Id\": \"00000000-0000-0000-0000-000000000001\", \"Type\": \"Hexagon\", \"Props\": {}}]}";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_WrongPropType_ReturnsNull()
    {
        // A string where a number is expected: applying the property throws on
        // the type mismatch, and Deserialize must swallow it rather than crash.
        var json = "{\"Primitives\": [{\"Id\": \"00000000-0000-0000-0000-000000000001\", \"Type\": \"Rectangle\", \"Props\": {\"Width\": \"wide\"}}]}";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Serialize_IncludesCurrentVersion()
    {
        var json = DocumentSerializer.Serialize(new Primitive[] { new Rectangle() }, Array.Empty<Primitive>());

        using var document = JsonDocument.Parse(json);
        Assert.Equal(DocumentSerializer.CurrentVersion, document.RootElement.GetProperty("Version").GetInt32());
    }

    [Fact]
    public void Deserialize_LegacyDocumentWithoutVersion_IsAccepted()
    {
        const string json = @"{
          ""Primitives"": [
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Rectangle"" }
          ],
          ""SelectionIds"": []
        }";

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.IsType<Rectangle>(Assert.Single(result!.Roots));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    public void Deserialize_UnknownOrNegativeVersion_ReturnsNull(string version)
    {
        var json = $@"{{
          ""Version"": {version},
          ""Primitives"": [
            {{ ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Rectangle"" }}
          ],
          ""SelectionIds"": []
        }}";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_DuplicateIds_ReturnsNull()
    {
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Rectangle"" },
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Circle"" }
          ],
          ""SelectionIds"": []
        }";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_EmptyId_ReturnsNull()
    {
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            { ""Id"": ""00000000-0000-0000-0000-000000000000"", ""Type"": ""Rectangle"" }
          ],
          ""SelectionIds"": []
        }";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_ChildrenOnNonContainer_ReturnsNull()
    {
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            {
              ""Id"": ""00000000-0000-0000-0000-000000000001"",
              ""Type"": ""Rectangle"",
              ""Children"": [
                { ""Id"": ""00000000-0000-0000-0000-000000000002"", ""Type"": ""Circle"" }
              ]
            }
          ],
          ""SelectionIds"": []
        }";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_TooDeep_ReturnsNull()
    {
        var root = Container.CreateOffset("root", 0f, 0f);
        var current = root;
        for (var i = 0; i < DocumentSerializer.MaxDepth + 2; i++)
        {
            var child = Container.CreateOffset($"c{i}", 0f, 0f);
            current.AddChild(child);
            current = child;
        }

        var json = DocumentSerializer.Serialize(new Primitive[] { root }, Array.Empty<Primitive>());

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_TooManyNodes_ReturnsNull()
    {
        var builder = new StringBuilder("{\"Version\":1,\"Primitives\":[");
        for (var i = 0; i <= DocumentSerializer.MaxNodes; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"Id\":\"").Append(Guid.NewGuid()).Append("\",\"Type\":\"Rectangle\"}");
        }

        builder.Append("],\"SelectionIds\":[]}");

        Assert.Null(DocumentSerializer.Deserialize(builder.ToString()));
    }

    [Fact]
    public void Deserialize_TooLarge_ReturnsNull()
    {
        var json = new string('x', DocumentSerializer.MaxDocumentBytes + 1);

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void Deserialize_DuplicateSelectionIds_AreDeduplicated()
    {
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            { ""Id"": ""00000000-0000-0000-0000-000000000001"", ""Type"": ""Rectangle"" }
          ],
          ""SelectionIds"": [
            ""00000000-0000-0000-0000-000000000001"",
            ""00000000-0000-0000-0000-000000000001""
          ]
        }";

        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.Single(result!.SelectionIds);
    }

    [Fact]
    public void ResolveSelection_MapsKnownIdsAndIgnoresUnknown()
    {
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            {
              ""Id"": ""00000000-0000-0000-0000-000000000001"",
              ""Type"": ""Container"",
              ""Kind"": ""Offset"",
              ""Children"": [
                { ""Id"": ""00000000-0000-0000-0000-000000000002"", ""Type"": ""Circle"" }
              ]
            }
          ],
          ""SelectionIds"": [
            ""00000000-0000-0000-0000-000000000002"",
            ""00000000-0000-0000-0000-000000000099""
          ]
        }";

        var result = DocumentSerializer.Deserialize(json)!;
        var selection = result.ResolveSelection();

        var circle = Assert.IsType<Circle>(Assert.Single(selection));
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000002"), circle.Id);
    }
}
