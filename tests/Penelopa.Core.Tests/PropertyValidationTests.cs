using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class PropertyValidationTests
{
    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e8")]
    [InlineData("not-a-number")]
    public void CircleRadius_RejectsInvalidText(string text)
    {
        var circle = new Circle();

        var accepted = circle.Radius.TrySetText(text, out var error);

        Assert.False(accepted);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(5f, circle.Radius.Value);
        Assert.True(circle.Radius.IsValidValue(out _));
    }

    [Theory]
    [InlineData("Width")]
    [InlineData("Height")]
    public void RectangleDimensions_RejectNonPositiveAndNonFinite(string propName)
    {
        var rectangle = new Rectangle();
        var prop = Assert.IsType<FloatPropValue>(rectangle.Props.Single(p => p.Name == propName));
        var original = prop.Value;

        Assert.False(prop.TrySetText("-1", out _));
        Assert.False(prop.TrySetText("0", out _));
        Assert.False(prop.TrySetText("NaN", out _));
        Assert.False(prop.TrySetText("Infinity", out _));

        Assert.Equal(original, prop.Value);
        Assert.True(prop.IsValidValue(out _));
    }

    [Theory]
    [InlineData("12.5", 12.5f)]
    [InlineData("-4", -4f)]
    [InlineData("10000000", 10000000f)]
    public void FiniteNumbers_AreAccepted(string text, float expected)
    {
        var circle = new Circle();

        var accepted = circle.CenterX.TrySetText(text, out var error);

        Assert.True(accepted);
        Assert.Null(error);
        Assert.Equal(expected, circle.CenterX.Value);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("4294967296")]
    [InlineData("abc")]
    public void Color_RejectsOutOfRangeOrInvalidText(string text)
    {
        var circle = new Circle();

        Assert.False(circle.Color.TrySetText(text, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(0xFFFFFFFFu, circle.Color.Value);
    }

    [Fact]
    public void Color_AcceptsUintRange()
    {
        var circle = new Circle();

        Assert.True(circle.Color.TrySetText("4294967295", out _));
        Assert.Equal(uint.MaxValue, circle.Color.Value);
        Assert.True(circle.Color.TrySetText("0", out _));
        Assert.Equal(0u, circle.Color.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void PrimitiveName_RejectsEmptyOrWhitespace(string text)
    {
        var circle = new Circle { Name = { Value = "Circle" } };

        Assert.False(circle.Name.TrySetText(text, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal("Circle", circle.Name.Value);
    }

    [Fact]
    public void PrimitiveName_TrimsInputAndRejectsOverlongNames()
    {
        var circle = new Circle();

        Assert.True(circle.Name.TrySetText("  Renamed Circle  ", out _));
        Assert.Equal("Renamed Circle", circle.Name.Value);
        Assert.False(circle.Name.TrySetText(new string('x', 129), out var error));
        Assert.Contains("128", error);
        Assert.Equal("Renamed Circle", circle.Name.Value);
    }

    [Fact]
    public void LoadDocument_WithEmptyName_IsRejected()
    {
        const string json = @"{
          ""Primitives"": [
            {
              ""Id"": ""11111111-1111-1111-1111-111111111111"",
              ""Type"": ""Circle"",
              ""Props"": {
                ""Name"": ""   "",
                ""CenterX"": 10,
                ""CenterY"": 10,
                ""Radius"": 5,
                ""Color"": 4294967295
              }
            }
          ],
          ""SelectionIds"": []
        }";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void TypedProperties_ParseValidTextAndRejectInvalidText()
    {
        var intProp = new IntPropValue("i", 1);
        Assert.True(intProp.TrySetText("2", out _));
        Assert.Equal(2, intProp.Value);
        Assert.False(intProp.TrySetText("nope", out _));

        var boolProp = new BoolPropValue("b", false);
        Assert.True(boolProp.TrySetText("true", out _));
        Assert.True(boolProp.Value);
        Assert.False(boolProp.TrySetText("maybe", out _));

        var doubleProp = new DoublePropValue("d", 1d);
        Assert.True(doubleProp.TrySetText("2.5", out _));
        Assert.Equal(2.5d, doubleProp.Value);
        Assert.False(doubleProp.TrySetText("NaN", out _));
        Assert.False(doubleProp.TrySetText("Infinity", out _));
        Assert.False(doubleProp.TrySetText("1e9", out _));

        var stringProp = new StringPropValue("s", "old");
        Assert.True(stringProp.TrySetText("new", out _));
        Assert.Equal("new", stringProp.Value);
    }

    [Fact]
    public void LoadDocument_WithNegativeRadius_IsRejected()
    {
        const string json = @"{
          ""Primitives"": [
            {
              ""Id"": ""11111111-1111-1111-1111-111111111111"",
              ""Type"": ""Circle"",
              ""Props"": {
                ""Name"": ""Circle"",
                ""CenterX"": 10,
                ""CenterY"": 10,
                ""Radius"": -5,
                ""Color"": 4294967295
              }
            }
          ],
          ""SelectionIds"": []
        }";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void LoadDocument_WithZeroWidth_IsRejected()
    {
        const string json = @"{
          ""Primitives"": [
            {
              ""Id"": ""11111111-1111-1111-1111-111111111111"",
              ""Type"": ""Rectangle"",
              ""Props"": {
                ""Name"": ""Rectangle"",
                ""PosX"": 0,
                ""PosY"": 0,
                ""Width"": 0,
                ""Height"": 10,
                ""Color"": 4294967295
              }
            }
          ],
          ""SelectionIds"": []
        }";

        Assert.Null(DocumentSerializer.Deserialize(json));
    }

    [Fact]
    public void LoadDocument_WithValidValues_IsAccepted()
    {
        const string json = @"{
          ""Primitives"": [
            {
              ""Id"": ""11111111-1111-1111-1111-111111111111"",
              ""Type"": ""Circle"",
              ""Props"": {
                ""Name"": ""Circle"",
                ""CenterX"": 10,
                ""CenterY"": 10,
                ""Radius"": 5,
                ""Color"": 4294967295
              }
            }
          ],
          ""SelectionIds"": []
        }";

        var result = DocumentSerializer.Deserialize(json);

        var circle = Assert.IsType<Circle>(Assert.Single(result!.Roots));
        Assert.Equal(5f, circle.Radius.Value);
    }
}
