using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Penelopa.Components;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Ui.Tests;

public class ToolPanelTests : TestContext
{
    private readonly PrimitiveService _service;

    public ToolPanelTests()
    {
        _service = new PrimitiveService();
        Services.AddSingleton<IPrimitiveService>(_service);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Load_InvalidDocument_ShowsErrorAndKeepsCurrentDocument()
    {
        var circle = new Circle();
        _service.Add(circle);
        JSInterop.Setup<string>("localStorage.getItem", "penelopa.document").SetResult("not json");
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();

        cut.WaitForAssertion(() => Assert.Contains("invalid or unsupported", cut.Markup));
        Assert.Contains(circle, _service.GetAll());
    }

    [Fact]
    public void Load_ValidDocument_RequiresConfirmationAndResolvesEmptySelection()
    {
        var existing = new Circle();
        _service.Add(existing);
        var loaded = new Rectangle { Name = { Value = "Loaded" } };
        var json = DocumentSerializer.Serialize(new Primitive[] { loaded }, Array.Empty<Primitive>());
        JSInterop.Setup<string>("localStorage.getItem", "penelopa.document").SetResult(json);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();

        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Replace"));
        Assert.Contains(existing, _service.GetAll());

        FindButton(cut, "Replace").Click();

        var primitive = Assert.Single(_service.GetAll());
        Assert.Equal("Loaded", primitive.Name.Value);
        Assert.Empty(_service.GetSelection());
        Assert.Contains("Document loaded.", cut.Markup);
    }

    [Fact]
    public void Load_ConfirmationCanBeCancelled()
    {
        var existing = new Circle();
        _service.Add(existing);
        var json = DocumentSerializer.Serialize(new Primitive[] { new Rectangle() }, Array.Empty<Primitive>());
        JSInterop.Setup<string>("localStorage.getItem", "penelopa.document").SetResult(json);
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Load").Click();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Trim() == "Cancel"));

        FindButton(cut, "Cancel").Click();

        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Replace");
        Assert.Contains(existing, _service.GetAll());
    }

    [Fact]
    public async Task DeleteButton_RemovesSelectionAndUndoRestoresIt()
    {
        var circle = new Circle();
        _service.Add(circle);
        _service.SetSelected(circle);
        var cut = RenderComponent<ToolPanel>();

        var delete = FindButton(cut, "Delete");
        Assert.False(delete.HasAttribute("disabled"));
        delete.Click();

        cut.WaitForAssertion(() => Assert.Empty(_service.GetAll()));
        cut.WaitForAssertion(() => Assert.True(FindButton(cut, "Delete").HasAttribute("disabled")));

        await cut.InvokeAsync(() => _service.Undo());
        cut.WaitForAssertion(() => Assert.Single(_service.GetAll()));
    }

    [Fact]
    public void DeleteButton_IsDisabledWithoutSelection()
    {
        _service.Add(new Circle());
        var cut = RenderComponent<ToolPanel>();

        Assert.True(FindButton(cut, "Delete").HasAttribute("disabled"));
    }

    [Fact]
    public async Task GroupAndUngroupButtons_ReflectSelectionEligibility()
    {
        var first = new Circle();
        var second = new Rectangle();
        _service.Add(first);
        _service.Add(second);
        var cut = RenderComponent<ToolPanel>();

        Assert.True(FindButton(cut, "Group").HasAttribute("disabled"));
        Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        Assert.Contains("identity", cut.Find("#penelopa-group-hint").TextContent);

        await cut.InvokeAsync(() => _service.SetSelected(first));
        cut.WaitForAssertion(() => Assert.True(FindButton(cut, "Group").HasAttribute("disabled")));

        await cut.InvokeAsync(() => _service.SetSelectedRange(new Primitive[] { first, second }));
        cut.WaitForAssertion(() => Assert.False(FindButton(cut, "Group").HasAttribute("disabled")));
        Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
    }

    [Fact]
    public void GroupButton_GroupsRootsAndUngroupRestoresThem()
    {
        var first = new Circle { Name = { Value = "First" } };
        var second = new Rectangle { Name = { Value = "Second" } };
        _service.Add(first);
        _service.Add(second);
        _service.SetSelectedRange(new Primitive[] { first, second });
        var cut = RenderComponent<ToolPanel>();

        var group = FindButton(cut, "Group");
        Assert.False(group.HasAttribute("disabled"));
        group.Click();

        cut.WaitForAssertion(() =>
        {
            var container = Assert.IsType<Container>(Assert.Single(_service.GetAll()));
            Assert.Equal(new Primitive[] { first, second }, container.Children);
            Assert.Same(container, Assert.Single(_service.GetSelection()));
            Assert.False(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        });

        FindButton(cut, "Ungroup").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(new Primitive[] { first, second }, _service.GetAll());
            Assert.Equal(new Primitive[] { first, second }, _service.GetSelection());
            Assert.Null(first.Parent);
            Assert.Null(second.Parent);
            Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task GroupAndUngroup_AreDisabledForDrilledChildAndNonIdentityContainer()
    {
        var container = Container.CreateOffset("Offset", 20f, 20f);
        var child = new Circle();
        container.AddChild(child);
        _service.Add(container);
        _service.SetSelected(child);
        var cut = RenderComponent<ToolPanel>();

        Assert.True(FindButton(cut, "Group").HasAttribute("disabled"));
        Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));

        await cut.InvokeAsync(() => _service.SetSelected(container));
        cut.WaitForAssertion(() =>
        {
            Assert.True(FindButton(cut, "Group").HasAttribute("disabled"));
            Assert.True(FindButton(cut, "Ungroup").HasAttribute("disabled"));
        });

        var identity = Container.CreateOffset("Identity", 0f, 0f);
        await cut.InvokeAsync(() =>
        {
            _service.Add(identity);
            _service.SetSelected(identity);
        });
        cut.WaitForAssertion(() => Assert.False(FindButton(cut, "Ungroup").HasAttribute("disabled")));
    }

    [Fact]
    public void Save_BacksUpPreviousDocument()
    {
        _service.Add(new Circle());
        JSInterop.Setup<string>("localStorage.getItem", "penelopa.document").SetResult("previous-document");
        var cut = RenderComponent<ToolPanel>();

        FindButton(cut, "Save").Click();

        cut.WaitForAssertion(() => Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "localStorage.setItem"
                && invocation.Arguments.Count == 2
                && Equals(invocation.Arguments[0], "penelopa.document.backup")
                && Equals(invocation.Arguments[1], "previous-document")));
        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "localStorage.setItem"
                && Equals(invocation.Arguments[0], "penelopa.document"));
    }

    private static AngleSharp.Dom.IElement FindButton(IRenderedFragment cut, string text)
        => cut.FindAll("button").Single(b => b.TextContent.Trim() == text);
}
