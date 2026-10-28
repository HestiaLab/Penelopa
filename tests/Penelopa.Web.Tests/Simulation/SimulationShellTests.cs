using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Penelopa.Web.Components;
using Penelopa.Web.Components.Simulation;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;
using Xunit;
using SimulationTimelineComponent = Penelopa.Web.Components.Simulation.SimulationTimeline;

namespace Penelopa.Web.Tests.Simulation;

public class SimulationShellTests : TestContext
{
    private readonly PrimitiveService _service;

    public SimulationShellTests()
    {
        _service = new PrimitiveService();
        Services.AddSingleton<IPrimitiveService>(_service);
        Services.AddSingleton<SimulationConfigStore>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void TopToolbar_ShowsBaseRelativeSimulationLink()
    {
        var cut = RenderComponent<PenelopaTopToolbar>();

        var link = cut.Find("a");

        Assert.Equal("Simulate", link.TextContent.Trim());
        Assert.Equal("simulation", link.GetAttribute("href"));
        Assert.Equal(
            "penelopa-simulate-link",
            link.GetAttribute("data-testid"));
    }

    [Fact]
    public void SimulationTree_RendersHierarchySelectionAndBadge()
    {
        var container = Container.CreateOffset("Group", 0f, 0f);
        var child = new Rectangle { Name = { Value = "Child" } };
        container.AddChild(child);
        var track = new SimulationTrack(
            child.Id,
            "PosX",
            new[] { Keyframe(0, 10), Keyframe(1, 20) });
        Primitive? selected = null;

        var cut = RenderComponent<SimulationTree>(parameters => parameters
            .Add(component => component.Roots, new Primitive[] { container })
            .Add(component => component.SelectedId, child.Id)
            .Add(component => component.Tracks, new[] { track })
            .Add(
                component => component.OnSelect,
                EventCallback.Factory.Create<Primitive>(
                    this,
                    primitive => selected = primitive)));

        var items = cut.FindAll("[role='treeitem']");
        Assert.Equal(2, items.Count);
        Assert.Contains("Group", items[0].TextContent);
        Assert.Contains("Child", items[1].TextContent);
        Assert.Equal("false", items[0].GetAttribute("aria-selected"));
        Assert.Equal("true", items[1].GetAttribute("aria-selected"));
        Assert.Contains("selected", items[1].ClassList);
        Assert.Single(cut.FindAll(".penelopa-simulation-badge"));

        items[0].QuerySelector(".tree-node-content__main")!.Click();
        cut.WaitForAssertion(() => Assert.Same(container, selected));
    }

    [Fact]
    public void SimulationTree_ShowsEmptyState()
    {
        var cut = RenderComponent<SimulationTree>(parameters => parameters
            .Add(component => component.Roots, Array.Empty<Primitive>()));

        Assert.Contains("No primitives yet", cut.Markup);
    }

    [Fact]
    public void SimulationTransport_ReflectsStateAndInvokesCallbacks()
    {
        var channel = new SimulationReadoutChannel();
        channel.Publish(Readout(SimulationState.Ready, 0.123d));
        var playCalls = 0;
        var pauseCalls = 0;
        var stopCalls = 0;
        var loopCalls = 0;

        var cut = RenderComponent<SimulationTransport>(parameters => parameters
            .Add(component => component.State, SimulationState.Ready)
            .Add(component => component.CanPlay, true)
            .Add(component => component.CanStop, false)
            .Add(component => component.CanLoop, true)
            .Add(component => component.Loop, false)
            .Add(component => component.Channel, channel)
            .Add(
                component => component.OnPlay,
                EventCallback.Factory.Create(this, () => playCalls++))
            .Add(
                component => component.OnPause,
                EventCallback.Factory.Create(this, () => pauseCalls++))
            .Add(
                component => component.OnStop,
                EventCallback.Factory.Create(this, () => stopCalls++))
            .Add(
                component => component.OnToggleLoop,
                EventCallback.Factory.Create(this, () => loopCalls++)));

        Assert.Equal("Play", Button(cut, "Play").TextContent.Trim());
        Assert.True(Button(cut, "Stop").HasAttribute("disabled"));
        Assert.Equal("false", Button(cut, "Loop").GetAttribute("aria-pressed"));
        Assert.Equal("Ready", cut.Find(".penelopa-simulation-status").TextContent.Trim());
        Assert.Contains("0.123s", cut.Markup);

        Button(cut, "Play").Click();
        Button(cut, "Loop").Click();

        Assert.Equal(1, playCalls);
        Assert.Equal(1, loopCalls);
        Assert.Equal(0, pauseCalls);
        Assert.Equal(0, stopCalls);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.State, SimulationState.Playing)
            .Add(component => component.CanPlay, true)
            .Add(component => component.CanStop, true)
            .Add(component => component.CanLoop, true)
            .Add(component => component.Loop, true));

        Assert.Equal("Pause", Button(cut, "Pause").TextContent.Trim());
        Assert.False(Button(cut, "Stop").HasAttribute("disabled"));
        Assert.Equal("true", Button(cut, "Loop").GetAttribute("aria-pressed"));

        Button(cut, "Pause").Click();
        Button(cut, "Stop").Click();
        Assert.Equal(1, pauseCalls);
        Assert.Equal(1, stopCalls);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.State, SimulationState.Finished)
            .Add(component => component.CanPlay, true)
            .Add(component => component.CanStop, true)
            .Add(component => component.CanLoop, true)
            .Add(component => component.Loop, false));

        Assert.Equal("Replay", Button(cut, "Replay").TextContent.Trim());
        Button(cut, "Replay").Click();
        Assert.Equal(2, playCalls);

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.State, SimulationState.Ready)
            .Add(component => component.CanPlay, false)
            .Add(component => component.CanStop, false)
            .Add(component => component.CanLoop, false)
            .Add(component => component.Loop, false));

        Assert.True(Button(cut, "Play").HasAttribute("disabled"));
        Assert.True(Button(cut, "Loop").HasAttribute("disabled"));
    }

    [Fact]
    public void SimulationTransport_UpdatesTimeReadoutFromChannel()
    {
        var channel = new SimulationReadoutChannel();
        channel.Publish(Readout(SimulationState.Playing, 0.25d));
        var cut = RenderComponent<SimulationTransport>(parameters => parameters
            .Add(component => component.State, SimulationState.Playing)
            .Add(component => component.CanPlay, true)
            .Add(component => component.CanStop, true)
            .Add(component => component.CanLoop, true)
            .Add(component => component.Channel, channel));

        Assert.Contains("0.250s", cut.Markup);

        cut.InvokeAsync(() =>
            channel.Publish(Readout(SimulationState.Playing, 1.5d)));

        cut.WaitForAssertion(() => Assert.Contains("1.500s", cut.Markup));
    }

    [Fact]
    public void SimulationInspector_ShowsFloatProperties()
    {
        var rectangle = new Rectangle
        {
            PosX = { Value = 12f },
            PosY = { Value = 34f },
        };
        var channel = new SimulationReadoutChannel();
        channel.Publish(Readout(SimulationState.Ready, 0d));

        var cut = RenderComponent<SimulationInspector>(parameters => parameters
            .Add(component => component.Primitive, rectangle)
            .Add(component => component.Channel, channel));

        Assert.Contains("Rectangle", cut.Markup);
        Assert.Contains("PosX", cut.Markup);
        Assert.Contains("12", cut.Markup);
        Assert.Contains("34", cut.Markup);
        Assert.Contains("Values are relative to the parent.", cut.Markup);
    }

    [Fact]
    public void SimulationInspector_UpdatesCurrentValueFromReadoutChannel()
    {
        var rectangle = new Rectangle();
        var track = new SimulationTrack(
            rectangle.Id,
            "PosX",
            new[] { Keyframe(0, 10), Keyframe(2, 30) });
        var config = new SimulationConfig(false, new[] { track });
        var channel = new SimulationReadoutChannel();
        channel.Publish(Readout(SimulationState.Ready, 0d));

        var cut = RenderComponent<SimulationInspector>(parameters => parameters
            .Add(component => component.Primitive, rectangle)
            .Add(component => component.Config, config)
            .Add(component => component.Channel, channel));

        var current = cut.FindAll(".penelopa-simulation-property")
            .First(row => row.TextContent.Contains("PosX"))
            .QuerySelector(".penelopa-simulation-current");
        Assert.NotNull(current);
        Assert.Equal("10.000", current!.TextContent.Trim());

        cut.InvokeAsync(() => channel.Publish(Readout(
            SimulationState.Playing,
            1d,
            new Dictionary<SimulationTrackKey, float> { [track.Key] = 23.5f })));

        cut.WaitForAssertion(() => Assert.Equal(
            "23.500",
            cut.FindAll(".penelopa-simulation-property")
                .First(row => row.TextContent.Contains("PosX"))
                .QuerySelector(".penelopa-simulation-current")!
                .TextContent
                .Trim()));
    }

    [Fact]
    public void SimulationInspector_AnimateCreatesTrackAndSelectsIt()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        var channel = new SimulationReadoutChannel();
        channel.Publish(Readout(SimulationState.Ready, 0d));
        string? selected = null;

        var cut = RenderComponent<SimulationInspector>(parameters => parameters
            .Add(component => component.Primitive, rectangle)
            .Add(component => component.Channel, channel)
            .Add(component => component.CanEdit, true)
            .Add(
                component => component.OnSelectTrack,
                EventCallback.Factory.Create<string>(this, name => selected = name)));

        var animate = cut.FindAll("button").First(button =>
            button.TextContent.Trim() == "Animate");
        animate.Click();

        var track = Assert.Single(store.Config.Tracks);
        Assert.Equal("PosX", track.PropertyName);
        Assert.Equal("PosX", selected);
    }

    [Fact]
    public void SimulationKeyframeEditor_CommitsTime()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var error), error);
        var keyframe = store.Config.Tracks.Single().Keyframes[0];

        var cut = RenderComponent<SimulationKeyframeEditor>(parameters => parameters
            .Add(component => component.PrimitiveId, rectangle.Id)
            .Add(component => component.PropertyName, "PosX")
            .Add(component => component.KeyframeIndex, 0)
            .Add(component => component.Keyframe, keyframe)
            .Add(component => component.CanEdit, true)
            .Add(component => component.OnGoToRequested, EventCallback.Factory.Create(this, () => { })));

        Assert.Contains("Values are relative to the parent.", cut.Markup);

        var timeInput = cut.FindAll("input")[0];
        timeInput.Input("0.5");
        timeInput.Change("0.5");

        Assert.Equal(
            0.5d,
            store.Config.Tracks.Single().Keyframes[0].Time,
            9);
    }

    [Fact]
    public void SimulationKeyframeEditor_ChangesInterpolationAndBezier()
    {
        var rectangle = new Rectangle();
        _service.Add(rectangle);
        var store = Services.GetRequiredService<SimulationConfigStore>();
        Assert.True(store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var error), error);
        var keyframe = store.Config.Tracks.Single().Keyframes[0];

        var cut = RenderComponent<SimulationKeyframeEditor>(parameters => parameters
            .Add(component => component.PrimitiveId, rectangle.Id)
            .Add(component => component.PropertyName, "PosX")
            .Add(component => component.KeyframeIndex, 0)
            .Add(component => component.Keyframe, keyframe)
            .Add(component => component.IsLast, false)
            .Add(component => component.CanEdit, true)
            .Add(component => component.OnGoToRequested, EventCallback.Factory.Create(this, () => { })));

        cut.Find("select").Change("EaseIn");
        Assert.Equal(
            InterpolationKind.EaseIn,
            store.Config.Tracks.Single().Keyframes[0].Interpolation);

        keyframe = store.Config.Tracks.Single().Keyframes[0];
        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Keyframe, keyframe));
        cut.Find("select").Change("CubicBezier");
        Assert.Equal(
            InterpolationKind.CubicBezier,
            store.Config.Tracks.Single().Keyframes[0].Interpolation);

        keyframe = store.Config.Tracks.Single().Keyframes[0];
        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Keyframe, keyframe));
        var bezierInputs = cut.FindAll(".penelopa-simulation-bezier-grid input");
        Assert.Equal(4, bezierInputs.Count);
        bezierInputs[0].Input("0.2");
        bezierInputs[0].Change("0.2");

        Assert.Equal(
            0.2d,
            store.Config.Tracks.Single().Keyframes[0].Bezier!.Value.X1,
            9);
    }

    [Fact]
    public void SimulationTimeline_RendersSelectedTracks()
    {
        var rectangle = new Rectangle();
        var config = new SimulationConfig(
            false,
            new[]
            {
                new SimulationTrack(
                    rectangle.Id,
                    "PosX",
                    new[] { Keyframe(0, 10), Keyframe(2, 30) }),
                new SimulationTrack(
                    rectangle.Id,
                    "PosY",
                    new[] { Keyframe(0, 20), Keyframe(1, 40) }),
            });
        var channel = new SimulationReadoutChannel();
        channel.Publish(Readout(SimulationState.Ready, 0d));

        var cut = RenderComponent<SimulationTimelineComponent>(parameters => parameters
            .Add(component => component.Primitive, rectangle)
            .Add(component => component.Config, config)
            .Add(component => component.Duration, 2d)
            .Add(component => component.Channel, channel));

        Assert.Contains("PosX", cut.Markup);
        Assert.Contains("PosY", cut.Markup);
        Assert.Equal(4, cut.FindAll(".penelopa-simulation-keyframe").Count);
        Assert.Contains("0s", cut.Markup);
    }

    [Fact]
    public void SimulationTimeline_TickClickSeeksAndPlayheadFollowsChannel()
    {
        var rectangle = new Rectangle();
        var config = new SimulationConfig(
            false,
            new[]
            {
                new SimulationTrack(
                    rectangle.Id,
                    "PosX",
                    new[] { Keyframe(0, 10), Keyframe(2, 30) }),
            });
        var channel = new SimulationReadoutChannel();
        channel.Publish(Readout(SimulationState.Ready, 0d));
        double? seeked = null;

        var cut = RenderComponent<SimulationTimelineComponent>(parameters => parameters
            .Add(component => component.Primitive, rectangle)
            .Add(component => component.Config, config)
            .Add(component => component.Duration, 2d)
            .Add(component => component.Channel, channel)
            .Add(
                component => component.OnSeekRequested,
                EventCallback.Factory.Create<double>(this, time => seeked = time)));

        var ticks = cut.FindAll(".penelopa-simulation-tick");
        Assert.True(ticks.Count >= 3);
        ticks[2].Click();
        Assert.Equal(1d, seeked);

        cut.InvokeAsync(() => channel.Publish(Readout(SimulationState.Playing, 1d)));
        cut.WaitForAssertion(() =>
        {
            var playhead = cut.Find(".penelopa-simulation-playhead");
            Assert.Contains("50%", playhead.GetAttribute("style"));
        });
    }

    [Fact]
    public void SimulationIssueSummary_ListsEveryIssueAndIsPolite()
    {
        var primitiveId = Guid.NewGuid();
        var issues = new[]
        {
            new SimulationValidationIssue(
                SimulationIssueKind.Structure,
                primitiveId,
                "PosX",
                0,
                "Keyframe time must be between 0 and 3600 seconds."),
            new SimulationValidationIssue(
                SimulationIssueKind.Value,
                primitiveId,
                "Radius",
                1,
                "Radius must be greater than zero."),
        };

        var cut = RenderComponent<SimulationIssueSummary>(parameters => parameters
            .Add(component => component.Issues, issues));

        Assert.Contains(
            "PosX (keyframe 1): Keyframe time must be between 0 and 3600 seconds.",
            cut.Markup);
        Assert.Contains(
            "Radius (keyframe 2): Radius must be greater than zero.",
            cut.Markup);
        var summary = cut.Find(".penelopa-simulation-issues");
        Assert.Equal("status", summary.GetAttribute("role"));
        Assert.Equal("polite", summary.GetAttribute("aria-live"));
        Assert.Null(summary.GetAttribute("alert"));
    }

    [Fact]
    public void SimulationIssueSummary_RendersNothingWithoutIssues()
    {
        var cut = RenderComponent<SimulationIssueSummary>();

        Assert.Empty(cut.FindAll(".penelopa-simulation-issues"));
    }

    [Fact]
    public void SimulationTransport_StepAndUndoCallbacks()
    {
        var channel = new SimulationReadoutChannel();
        var calls = new List<string>();

        var cut = RenderComponent<SimulationTransport>(parameters => parameters
            .Add(component => component.State, SimulationState.Paused)
            .Add(component => component.CanPlay, true)
            .Add(component => component.CanStop, true)
            .Add(component => component.CanLoop, true)
            .Add(component => component.CanStep, true)
            .Add(component => component.CanUndo, true)
            .Add(component => component.CanRedo, true)
            .Add(component => component.Channel, channel)
            .Add(
                component => component.OnStepBack,
                EventCallback.Factory.Create(this, () => calls.Add("back")))
            .Add(
                component => component.OnStepForward,
                EventCallback.Factory.Create(this, () => calls.Add("forward")))
            .Add(
                component => component.OnUndo,
                EventCallback.Factory.Create(this, () => calls.Add("undo")))
            .Add(
                component => component.OnRedo,
                EventCallback.Factory.Create(this, () => calls.Add("redo"))));

        cut.Find("button[aria-label='Step back 1/60 second']").Click();
        cut.Find("button[aria-label='Step forward 1/60 second']").Click();
        Button(cut, "Undo").Click();
        Button(cut, "Redo").Click();

        Assert.Equal(new[] { "back", "forward", "undo", "redo" }, calls);
    }

    private static SimulationReadout Readout(
        SimulationState state,
        double currentTime)
        => Readout(state, currentTime, new Dictionary<SimulationTrackKey, float>());

    private static SimulationReadout Readout(
        SimulationState state,
        double currentTime,
        IReadOnlyDictionary<SimulationTrackKey, float> values)
        => new(state, currentTime, 2d, false, values);

    private static SimulationKeyframe Keyframe(double time, float value)
        => new(time, value, InterpolationKind.Linear);

    private static AngleSharp.Dom.IElement Button(
        IRenderedComponent<SimulationTransport> cut,
        string text)
        => cut.FindAll("button").Single(button => button.TextContent.Trim() == text);
}
