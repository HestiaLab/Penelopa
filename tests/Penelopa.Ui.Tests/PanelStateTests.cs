using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Penelopa.Components;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Ui.Tests;

public class PanelStateTests : TestContext
{
    private readonly PrimitiveService _service;

    public PanelStateTests()
    {
        _service = new PrimitiveService();
        Services.AddSingleton<IPrimitiveService>(_service);
    }

    [Fact]
    public void PropertyPanel_InitializesFromExistingSelection()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);

        var cut = RenderComponent<PropertyPanel>();

        Assert.Contains("Circle", cut.Markup);
        Assert.Contains("Radius", cut.Markup);
    }

    [Fact]
    public async Task PropertyPanel_ClearsWhenDocumentIsReplacedWithNoSelection()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<PropertyPanel>();
        Assert.Contains("Circle", cut.Markup);

        await cut.InvokeAsync(() => _service.ReplaceAll(Array.Empty<Primitive>()));

        cut.WaitForAssertion(() => Assert.Contains("Select a primitive", cut.Markup));
        Assert.DoesNotContain("Radius", cut.Markup);
    }

    [Fact]
    public async Task PropertyPanel_FollowsSelectionChanges()
    {
        var circle = new Circle();
        var rectangle = new Rectangle();
        _service.Add(circle);
        _service.Add(rectangle);
        _service.SetSelected(circle);
        var cut = RenderComponent<PropertyPanel>();

        await cut.InvokeAsync(() => _service.SetSelected(rectangle));

        cut.WaitForAssertion(() => Assert.Contains("PosX", cut.Markup));
        Assert.DoesNotContain("Radius", cut.Markup);
    }

    [Fact]
    public void PropertyPanel_CommitsValidInputThroughService()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<PropertyPanel>();

        RadiusInput(cut).Change("20");

        cut.WaitForAssertion(() => Assert.Equal(20f, circle.Radius.Value));
        Assert.Equal("20", RadiusInput(cut).GetAttribute("value"));
    }

    [Fact]
    public void PropertyPanel_AssociatesLabelsAndErrorsWithInputs()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<PropertyPanel>();

        var field = Field(cut, "Radius");
        var input = field.QuerySelector("input")!;
        var label = field.QuerySelector("label")!;

        Assert.False(string.IsNullOrWhiteSpace(input.Id));
        Assert.Equal(input.Id, label.GetAttribute("for"));
        Assert.Null(input.GetAttribute("aria-invalid"));

        input.Change("-5");

        cut.WaitForAssertion(() =>
        {
            var error = Field(cut, "Radius").QuerySelector(".penelopa-prop-error")!;
            var invalidInput = Field(cut, "Radius").QuerySelector("input")!;
            Assert.Equal("true", invalidInput.GetAttribute("aria-invalid"));
            Assert.Equal(error.Id, invalidInput.GetAttribute("aria-describedby"));
            Assert.Equal("alert", error.GetAttribute("role"));
        });
        Assert.Equal(5f, circle.Radius.Value);
    }

    [Fact]
    public void PropertyPanel_RenamesPrimitiveThroughNameField()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<PropertyPanel>();

        Field(cut, "Name").QuerySelector("input")!.Change("  Renamed Circle  ");

        cut.WaitForAssertion(() => Assert.Equal("Renamed Circle", circle.Name.Value));
        Assert.Equal("Renamed Circle", cut.Find(".penelopa-properties-title").TextContent.Trim());
    }

    [Fact]
    public async Task PropertyPanel_RenameIsUndoable()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<PropertyPanel>();

        Field(cut, "Name").QuerySelector("input")!.Change("Renamed Circle");
        cut.WaitForAssertion(() => Assert.Equal("Renamed Circle", circle.Name.Value));

        await cut.InvokeAsync(() => _service.Undo());

        Assert.Equal("Circle", circle.Name.Value);
        cut.WaitForAssertion(
            () => Assert.Equal("Circle", cut.Find(".penelopa-properties-title").TextContent.Trim()));
    }

    [Fact]
    public void PropertyPanel_RejectsEmptyNameAndKeepsPreviousValue()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<PropertyPanel>();

        Field(cut, "Name").QuerySelector("input")!.Change("   ");

        cut.WaitForAssertion(() => Assert.NotNull(Field(cut, "Name").QuerySelector(".penelopa-prop-error")));
        Assert.Equal("Circle", circle.Name.Value);
    }

    [Fact]
    public void PropertyPanel_MultiSelectionShowsOnlyCommonProperties()
    {
        var circle = new Circle();
        var rectangle = new Rectangle();
        _service.Add(circle);
        _service.Add(rectangle);
        _service.SetSelectedRange(new Primitive[] { circle, rectangle });

        var cut = RenderComponent<PropertyPanel>();

        Assert.Equal("2 primitives selected", cut.Find(".penelopa-properties-title").TextContent.Trim());
        Assert.NotNull(FieldOrNull(cut, "Name"));
        Assert.NotNull(FieldOrNull(cut, "Color"));
        Assert.Null(FieldOrNull(cut, "Radius"));
        Assert.Null(FieldOrNull(cut, "PosX"));
    }

    [Fact]
    public void PropertyPanel_MultiSelectionShowsConflictPlaceholder()
    {
        var first = new Circle { Name = { Value = "First" } };
        var second = new Circle { Name = { Value = "Second" } };
        _service.Add(first);
        _service.Add(second);
        _service.SetSelectedRange(new Primitive[] { first, second });

        var cut = RenderComponent<PropertyPanel>();

        var input = Field(cut, "Name").QuerySelector("input")!;
        Assert.Equal(string.Empty, input.GetAttribute("value"));
        Assert.Equal("Multiple values", input.GetAttribute("placeholder"));
    }

    [Fact]
    public async Task PropertyPanel_MultiSelectionWritesEveryOwnerWithOneUndoEntry()
    {
        var first = new Circle();
        var second = new Circle();
        _service.Add(first);
        _service.Add(second);
        _service.SetSelectedRange(new Primitive[] { first, second });
        var cut = RenderComponent<PropertyPanel>();

        RadiusInput(cut).Change("12");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(12f, first.Radius.Value);
            Assert.Equal(12f, second.Radius.Value);
        });

        await cut.InvokeAsync(() => _service.Undo());
        Assert.Equal(5f, first.Radius.Value);
        Assert.Equal(5f, second.Radius.Value);
    }

    [Fact]
    public void PropertyPanel_MultiSelectionInvalidValueRollsBackEveryOwner()
    {
        var first = new Circle();
        var second = new Circle();
        _service.Add(first);
        _service.Add(second);
        _service.SetSelectedRange(new Primitive[] { first, second });
        var cut = RenderComponent<PropertyPanel>();

        RadiusInput(cut).Change("-1");

        cut.WaitForAssertion(() => Assert.NotNull(Field(cut, "Radius").QuerySelector(".penelopa-prop-error")));
        Assert.Equal(5f, first.Radius.Value);
        Assert.Equal(5f, second.Radius.Value);
    }

    [Fact]
    public void TreePanel_InitializesFromExistingDocument()
    {
        var container = Container.CreateOffset("Offset Container", 0f, 0f);
        container.AddChild(new Rectangle());
        _service.Add(container);

        var cut = RenderComponent<PrimitiveTreePanel>();

        var items = cut.FindAll(".penelopa-tree-item");
        Assert.Equal(2, items.Count);
        Assert.Equal("Offset Container", items[0].TextContent.Trim());
        Assert.Equal("Rectangle", items[1].TextContent.Trim());
    }

    [Fact]
    public async Task TreePanel_SelectionComesFromDocumentStateAndExposesAriaPressed()
    {
        var circle = new Circle();
        _service.Add(circle);
        var cut = RenderComponent<PrimitiveTreePanel>();
        Assert.Equal("false", cut.Find(".penelopa-tree-item").GetAttribute("aria-pressed"));

        await cut.InvokeAsync(() => _service.SetSelected(circle));

        cut.WaitForAssertion(() =>
        {
            var item = cut.Find(".penelopa-tree-item");
            Assert.Contains("selected", item.ClassList);
            Assert.Equal("true", item.GetAttribute("aria-pressed"));
        });
    }

    private static AngleSharp.Dom.IElement Field(IRenderedFragment cut, string name)
        => FieldOrNull(cut, name) ?? throw new InvalidOperationException($"Property field '{name}' was not rendered.");

    private static AngleSharp.Dom.IElement? FieldOrNull(IRenderedFragment cut, string name)
        => cut.FindAll(".penelopa-prop-field").SingleOrDefault(
            field => field.QuerySelector("label")?.TextContent.Trim() == name);

    private static AngleSharp.Dom.IElement RadiusInput(IRenderedFragment cut)
        => Field(cut, "Radius").QuerySelector("input")!;
}
