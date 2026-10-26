using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Penelopa.Web.Components.Simulation;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Xunit;

namespace Penelopa.Web.Tests.Simulation;

public class SimulationDormantTracksTests : TestContext
{
    private readonly PrimitiveService _service;
    private readonly SimulationConfigStore _store;

    public SimulationDormantTracksTests()
    {
        _service = new PrimitiveService();
        Services.AddSingleton<IPrimitiveService>(_service);
        Services.AddSingleton<SimulationConfigStore>();
        _store = Services.GetRequiredService<SimulationConfigStore>();
    }

    [Fact]
    public void RendersDetailsAndRemovesSingleDormantTrackWithUndo()
    {
        var first = new Rectangle();
        var second = new Rectangle();
        _service.Add(first);
        _service.Add(second);
        Assert.True(
            _store.TryAddTrack(first.Id, "PosX", 0d, 10f, out var firstError),
            firstError);
        Assert.True(
            _store.TryAddTrack(second.Id, "PosY", 0d, 20f, out var secondError),
            secondError);
        _service.Remove(first);
        _service.Remove(second);
        var dormant = _store.GetDormantTracks();
        Assert.Equal(2, dormant.Count);

        SimulationTrack? removed = null;
        var cut = RenderComponent<SimulationDormantTracks>(parameters => parameters
            .Add(component => component.Tracks, dormant)
            .Add(component => component.CanEdit, true)
            .Add(
                component => component.OnRemove,
                EventCallback.Factory.Create<SimulationTrack>(
                    this,
                    track =>
                    {
                        removed = track;
                        Assert.True(_store.TryRemoveTrack(
                            track.PrimitiveId,
                            track.PropertyName,
                            out var error), error);
                    })));

        Assert.Contains("2 inactive track(s)", cut.Markup);
        Assert.Contains("Show inactive tracks", cut.Markup);
        Assert.Equal(
            2,
            cut.FindAll(".penelopa-simulation-dormant-list li").Count);
        Assert.Contains("PosX", cut.Markup);
        Assert.Contains("PosY", cut.Markup);

        var removeButtons = cut.FindAll(
            "button[aria-label^='Remove inactive track']");
        Assert.Equal(2, removeButtons.Count);
        removeButtons[0].Click();

        Assert.NotNull(removed);
        Assert.Equal("PosX", removed!.PropertyName);
        var remaining = Assert.Single(_store.Config.Tracks);
        Assert.Equal("PosY", remaining.PropertyName);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Tracks, _store.GetDormantTracks()));
        Assert.Contains("1 inactive track(s)", cut.Markup);
        var row = Assert.Single(cut.FindAll(".penelopa-simulation-dormant-list li"));
        Assert.Contains("PosY", row.TextContent);
        Assert.DoesNotContain("PosX", row.TextContent);

        _store.Undo();
        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Tracks, _store.GetDormantTracks()));
        Assert.Equal(2, _store.Config.Tracks.Count);
        Assert.Equal(
            2,
            cut.FindAll(".penelopa-simulation-dormant-list li").Count);
    }

    [Fact]
    public void RemoveAllCallbackRunsOnlyWhenEditingIsAllowed()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        Assert.True(
            _store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var trackError),
            trackError);
        _service.Remove(rectangle);
        var calls = 0;

        var cut = RenderComponent<SimulationDormantTracks>(parameters => parameters
            .Add(component => component.Tracks, _store.GetDormantTracks())
            .Add(component => component.CanEdit, false)
            .Add(
                component => component.OnRemoveAll,
                EventCallback.Factory.Create(this, () => calls++)));

        Assert.True(cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Remove inactive")
            .HasAttribute("disabled"));

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.CanEdit, true));
        cut.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Remove inactive")
            .Click();

        Assert.Equal(1, calls);
    }
}
