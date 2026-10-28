using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Components;
using Xunit;

namespace Penelopa.Web.Tests;

public class WorkspaceChromeTests : TestContext
{
    private readonly PrimitiveService _service;

    public WorkspaceChromeTests()
    {
        _service = new PrimitiveService();
        Services.AddSingleton<IPrimitiveService>(_service);
        Services.AddSingleton<SimulationConfigStore>();
    }

    [Fact]
    public void TopToolbar_UndoAndRedoFollowDocumentHistory()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        var cut = RenderComponent<PenelopaTopToolbar>();

        var undo = cut.Find("button[aria-label='Undo document change']");
        var redo = cut.Find("button[aria-label='Redo document change']");
        Assert.False(undo.HasAttribute("disabled"));
        Assert.True(redo.HasAttribute("disabled"));

        undo.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(_service.GetAll());
            Assert.True(cut.Find("button[aria-label='Undo document change']")
                .HasAttribute("disabled"));
            Assert.False(cut.Find("button[aria-label='Redo document change']")
                .HasAttribute("disabled"));
        });

        cut.Find("button[aria-label='Redo document change']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Same(rectangle, Assert.Single(_service.GetAll()));
            Assert.False(cut.Find("button[aria-label='Undo document change']")
                .HasAttribute("disabled"));
        });

        var simulate = cut.Find("a");
        Assert.Equal("Simulate", simulate.TextContent.Trim());
        Assert.Equal("simulation", simulate.GetAttribute("href"));
    }

    [Fact]
    public async Task StatusBar_ShowsLiveDocumentSelectionAndTrackCounts()
    {
        var container = Container.CreateOffset("Group", 0f, 0f);
        var child = new Rectangle();
        container.AddChild(child);
        _service.Add(container);
        _service.SetSelected(child);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(store.TryAddTrack(child.Id, "PosX", 0d, 10f, out var trackError), trackError);

        var cut = RenderComponent<PenelopaStatusBar>();

        Assert.Equal("polite", cut.Find("footer").GetAttribute("aria-live"));
        Assert.Contains("2 primitives", cut.Markup);
        Assert.Contains("1 selected", cut.Markup);
        Assert.Contains("1 track", cut.Markup);

        var circle = new Circle();
        await cut.InvokeAsync(() => _service.Add(circle));
        await cut.InvokeAsync(() => _service.ClearSelection());
        Assert.True(store.TryAddTrack(circle.Id, "Radius", 0d, 5f, out var radiusError), radiusError);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("3 primitives", cut.Markup);
            Assert.DoesNotContain("1 selected", cut.Markup);
            Assert.Contains("2 tracks", cut.Markup);
        });
    }
}
