using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests.Simulation;

public class SimulationRuntimeTests
{
    [Fact]
    public void Ready_UsesBaseValuesAndIsPlayable()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.Equal(SimulationState.Ready, runtime.State);
        Assert.Equal(0d, runtime.CurrentTime);
        Assert.Equal(1d, runtime.Duration);
        Assert.True(runtime.CanPlay);
        Assert.Empty(runtime.ValidationIssues);

        var readout = runtime.CreateReadout();
        Assert.Equal(SimulationState.Ready, readout.State);
        Assert.Equal(10f, readout.TrackValues[Key(rectangle, "PosX")]);
        Assert.Equal(10f, rectangle.PosX.Value);
        Assert.Equal(20f, rectangle.PosY.Value);
    }

    [Fact]
    public void Preview_EvaluatesAndStopRestoresEveryFloatProperty()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryEnterPreview(0.5d, out var issues));
        Assert.Empty(issues);
        Assert.Equal(SimulationState.Preview, runtime.State);
        Assert.Equal(0.5d, runtime.CurrentTime);
        Assert.Equal(55f, rectangle.PosX.Value);
        Assert.Equal(20f, rectangle.PosY.Value);
        Assert.Equal(
            55f,
            runtime.CreateReadout().TrackValues[Key(rectangle, "PosX")]);

        runtime.Stop();

        Assert.Equal(SimulationState.Ready, runtime.State);
        Assert.Equal(0d, runtime.CurrentTime);
        Assert.Equal(10f, rectangle.PosX.Value);
        Assert.Equal(20f, rectangle.PosY.Value);
        Assert.Equal(30f, rectangle.Width.Value);
        Assert.Equal(40f, rectangle.Height.Value);
    }

    [Fact]
    public void Preview_AtZeroAppliesFirstKeyframeValue()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(2, 50), Frame(3, 100)));
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryEnterPreview(0d, out _));
        Assert.Equal(50f, rectangle.PosX.Value);

        runtime.Stop();
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void Play_AdvancesAndFinishesAtDuration()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryPlay(100d, out var issues));
        Assert.Empty(issues);
        Assert.Equal(SimulationState.Playing, runtime.State);

        runtime.Advance(100.25d);
        Assert.Equal(0.25d, runtime.CurrentTime, 9);
        Assert.Equal(32.5f, rectangle.PosX.Value);

        runtime.Advance(101d);
        Assert.Equal(SimulationState.Finished, runtime.State);
        Assert.Equal(1d, runtime.CurrentTime);
        Assert.Equal(100f, rectangle.PosX.Value);

        runtime.Advance(102d);
        Assert.Equal(SimulationState.Finished, runtime.State);
        Assert.Equal(100f, rectangle.PosX.Value);

        runtime.Pause(103d);
        Assert.Equal(SimulationState.Finished, runtime.State);

        runtime.Stop();
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void Pause_FreezesAndResumeContinuesFromFrozenTime()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryPlay(0d, out _));
        runtime.Advance(0.4d);
        runtime.Pause(0.4d);

        Assert.Equal(SimulationState.Paused, runtime.State);
        Assert.Equal(46d, rectangle.PosX.Value, 4);

        runtime.Advance(0.9d);
        Assert.Equal(46d, rectangle.PosX.Value, 4);

        Assert.True(runtime.TryPlay(10d, out _));
        runtime.Advance(10.1d);
        Assert.Equal(55d, rectangle.PosX.Value, 4);

        runtime.Pause(10.5d);
        Assert.Equal(SimulationState.Paused, runtime.State);
        Assert.Equal(91d, rectangle.PosX.Value, 4);

        runtime.Stop();
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void StepAndSeek_FromReadyEnterPreviewAndClamp()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryStep(0.5d, out _));
        Assert.Equal(SimulationState.Preview, runtime.State);
        Assert.Equal(0.5d, runtime.CurrentTime);
        Assert.Equal(55f, rectangle.PosX.Value);

        Assert.True(runtime.TryStep(-1d, out _));
        Assert.Equal(0d, runtime.CurrentTime);
        Assert.Equal(10f, rectangle.PosX.Value);

        Assert.True(runtime.TryStep(2d, out _));
        Assert.Equal(1d, runtime.CurrentTime);
        Assert.Equal(100f, rectangle.PosX.Value);

        Assert.True(runtime.TrySeek(-1d, out _));
        Assert.Equal(0d, runtime.CurrentTime);
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void SeekAndStep_WhilePlayingAreRejected()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryPlay(0d, out _));

        Assert.False(runtime.TryStep(1d / 60d, out var stepIssues));
        Assert.Single(stepIssues);
        Assert.False(runtime.TrySeek(0.5d, out var seekIssues));
        Assert.Single(seekIssues);
        Assert.Equal(SimulationState.Playing, runtime.State);
    }

    [Fact]
    public void Loop_WrapsAndNeverFinishes()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            true,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)));
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryPlay(0d, out _));
        runtime.Advance(0.75d);
        Assert.Equal(77.5f, rectangle.PosX.Value);
        Assert.Equal(SimulationState.Playing, runtime.State);

        runtime.Advance(1.25d);
        Assert.Equal(32.5f, rectangle.PosX.Value);
        Assert.Equal(SimulationState.Playing, runtime.State);

        runtime.Advance(100.5d);
        Assert.Equal(55f, rectangle.PosX.Value);
        Assert.Equal(SimulationState.Playing, runtime.State);
        Assert.True(runtime.CreateReadout().Loop);

        runtime.Stop();
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void Finished_CanSeekBackAndReplayFromZero()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryPlay(0d, out _));
        runtime.Advance(1d);
        Assert.Equal(SimulationState.Finished, runtime.State);

        Assert.True(runtime.TrySeek(0.25d, out _));
        Assert.Equal(SimulationState.Paused, runtime.State);
        Assert.Equal(32.5f, rectangle.PosX.Value);

        Assert.True(runtime.TrySeek(1d, out _));
        Assert.Equal(SimulationState.Finished, runtime.State);

        Assert.True(runtime.TryPlay(2d, out _));
        Assert.Equal(SimulationState.Playing, runtime.State);
        Assert.Equal(0d, runtime.CurrentTime);
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void UpdateConfig_WhilePausedReevaluatesAndClampsTime()
    {
        var rectangle = CreateRectangle();
        var initial = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(2, 100)));
        var runtime = CreateRuntime(rectangle, initial);

        Assert.True(runtime.TryEnterPreview(1.5d, out _));
        Assert.Equal(77.5f, rectangle.PosX.Value);

        var updated = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 0), Frame(1, 20)));
        Assert.True(runtime.TryUpdateConfig(updated, out var issues));
        Assert.Empty(issues);
        Assert.Equal(SimulationState.Preview, runtime.State);
        Assert.Equal(1d, runtime.CurrentTime);
        Assert.Equal(20f, rectangle.PosX.Value);
    }

    [Fact]
    public void UpdateConfig_RemovingAllTracksReturnsToReadyAndBase()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        Assert.True(runtime.TryEnterPreview(0.5d, out _));
        Assert.Equal(55f, rectangle.PosX.Value);

        Assert.True(runtime.TryUpdateConfig(SimulationConfig.Empty, out _));
        Assert.Equal(SimulationState.Ready, runtime.State);
        Assert.Equal(0d, runtime.CurrentTime);
        Assert.Equal(10f, rectangle.PosX.Value);

        Assert.True(runtime.TryUpdateConfig(config, out _));
        Assert.Equal(SimulationState.Ready, runtime.State);
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void UpdateConfig_WhilePlayingOrInvalidIsRejected()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);
        Assert.True(runtime.TryPlay(0d, out _));

        var whilePlaying = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 0), Frame(1, 20)));
        var configBefore = runtime.Config;
        Assert.False(runtime.TryUpdateConfig(whilePlaying, out var playingIssues));
        Assert.Single(playingIssues);
        Assert.Same(configBefore, runtime.Config);
        Assert.Equal(SimulationState.Playing, runtime.State);

        runtime.Stop();
        Assert.True(runtime.TryEnterPreview(0.5d, out _));
        var invalid = Config(
            false,
            Track(rectangle, "Width", Frame(0, 10), Frame(1, 0)));
        Assert.False(runtime.TryUpdateConfig(invalid, out var invalidIssues));
        Assert.Contains(invalidIssues, issue => issue.Kind == SimulationIssueKind.Value);
        Assert.Equal(SimulationState.Preview, runtime.State);
        Assert.Equal(55f, rectangle.PosX.Value);
    }

    [Fact]
    public void UpdateConfig_FromFinishedToLongerTimelineBecomesPaused()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);
        Assert.True(runtime.TryPlay(0d, out _));
        runtime.Advance(1d);
        Assert.Equal(SimulationState.Finished, runtime.State);

        var longer = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 0), Frame(2, 200)));
        Assert.True(runtime.TryUpdateConfig(longer, out _));
        Assert.Equal(SimulationState.Paused, runtime.State);
        Assert.Equal(1d, runtime.CurrentTime);
        Assert.Equal(100f, rectangle.PosX.Value);
    }

    [Fact]
    public void UpdateConfig_ToZeroDurationReturnsToReady()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);
        Assert.True(runtime.TryEnterPreview(0.5d, out _));

        var constant = Config(false, Track(rectangle, "PosX", Frame(0, 10)));
        Assert.True(runtime.TryUpdateConfig(constant, out _));
        Assert.Equal(SimulationState.Ready, runtime.State);
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void DormantTracksAreIgnoredAndDoNotBlockPlayback()
    {
        var rectangle = CreateRectangle();
        var active = Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100));
        var dormant = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[] { Frame(0, 10), Frame(1, 100) });
        var runtime = CreateRuntime(rectangle, Config(false, active, dormant));

        Assert.Single(runtime.DormantTracks);
        Assert.True(runtime.CanPlay);
        Assert.True(runtime.TryPlay(0d, out _));
        runtime.Advance(0.5d);
        Assert.Equal(55f, rectangle.PosX.Value);
        Assert.Single(runtime.CreateReadout().TrackValues);

        runtime.Stop();
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void OnlyDormantTracksBlockPlayback()
    {
        var rectangle = CreateRectangle();
        var dormant = new SimulationTrack(
            Guid.NewGuid(),
            "PosX",
            new[] { Frame(0, 10), Frame(1, 100) });
        var runtime = CreateRuntime(rectangle, Config(false, dormant));

        Assert.False(runtime.CanPlay);
        Assert.False(runtime.TryPlay(0d, out var issues));
        Assert.Contains(
            issues,
            issue => issue.Message.Contains("No active animation tracks"));
    }

    [Fact]
    public void ValidationIssuesBlockPreviewAndPlayback()
    {
        var circle = new Circle();
        var config = Config(
            false,
            Track(circle, "Radius", Frame(0, 5), Frame(1, -1)));
        var runtime = CreateRuntime(circle, config);

        Assert.False(runtime.CanPlay);
        Assert.Contains(
            runtime.ValidationIssues,
            issue => issue.Kind == SimulationIssueKind.Value);
        Assert.False(runtime.TryEnterPreview(0.5d, out var issues));
        Assert.Contains(issues, issue => issue.Kind == SimulationIssueKind.Value);
        Assert.Equal(SimulationState.Ready, runtime.State);
    }

    [Fact]
    public void EmptyAndZeroDurationConfigsBlockPlayback()
    {
        var rectangle = CreateRectangle();
        var emptyRuntime = CreateRuntime(rectangle, SimulationConfig.Empty);

        Assert.False(emptyRuntime.CanPlay);
        Assert.False(emptyRuntime.TryEnterPreview(0d, out var emptyIssues));
        Assert.Contains(
            emptyIssues,
            issue => issue.Message.Contains("No active animation tracks"));
        Assert.False(emptyRuntime.TryStep(1d / 60d, out _));

        var constant = Config(false, Track(rectangle, "PosX", Frame(0, 10)));
        var constantRuntime = CreateRuntime(rectangle, constant);

        Assert.False(constantRuntime.CanPlay);
        Assert.False(constantRuntime.TryPlay(0d, out var constantIssues));
        Assert.Contains(
            constantIssues,
            issue => issue.Message.Contains("no duration"));
    }

    [Fact]
    public void SetLoopAndReapplyCurrentFrameUpdateState()
    {
        var (rectangle, config) = CreateSimpleScene();
        var runtime = CreateRuntime(rectangle, config);

        runtime.Pause(0d);
        Assert.Equal(SimulationState.Ready, runtime.State);

        runtime.SetLoop(true);
        runtime.SetLoop(true);
        Assert.True(runtime.Loop);
        Assert.True(runtime.Config.Loop);
        Assert.True(runtime.CreateReadout().Loop);

        Assert.True(runtime.TryEnterPreview(0.5d, out _));
        rectangle.PosX.Value = 999f;
        runtime.ReapplyCurrentFrame();
        Assert.Equal(55f, rectangle.PosX.Value);

        runtime.Stop();
        Assert.Equal(10f, rectangle.PosX.Value);
        runtime.ReapplyCurrentFrame();
        Assert.Equal(10f, rectangle.PosX.Value);
    }

    [Fact]
    public void ReadoutContainsEveryActiveTrack()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)),
            Track(rectangle, "PosY", Frame(0, 20), Frame(1, 0)));
        var runtime = CreateRuntime(rectangle, config);

        Assert.Equal(2, runtime.ActiveTrackBindings.Count);
        Assert.True(runtime.TryEnterPreview(0.5d, out _));

        var readout = runtime.CreateReadout();
        Assert.Equal(2, readout.TrackValues.Count);
        Assert.Equal(55f, readout.TrackValues[Key(rectangle, "PosX")]);
        Assert.Equal(10f, readout.TrackValues[Key(rectangle, "PosY")]);

        runtime.Stop();
        Assert.Equal(20f, rectangle.PosY.Value);
    }

    [Fact]
    public void RuntimeDoesNotCreateDocumentHistory()
    {
        var service = new PrimitiveService();
        var rectangle = CreateRectangle();
        service.Add(rectangle);
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)));
        var runtime = new SimulationRuntime(service.GetAll(), config);

        Assert.True(runtime.TryEnterPreview(0.5d, out _));
        runtime.Stop();
        service.Undo();

        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void RuntimeRejectsInvalidArguments()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)));

        Assert.Throws<ArgumentNullException>(() =>
            new SimulationRuntime(null!, config));
        Assert.Throws<ArgumentNullException>(() =>
            new SimulationRuntime(new Primitive[] { rectangle }, null!));

        var runtime = new SimulationRuntime(new Primitive[] { rectangle }, config);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = runtime.TryPlay(double.NaN, out _);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = runtime.TryStep(double.NaN, out _);
        });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = runtime.TrySeek(double.NaN, out _);
        });
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = runtime.TryUpdateConfig(null!, out _);
        });

        Assert.True(runtime.TryPlay(0d, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.Advance(double.NaN));
    }

    private static (Rectangle Rectangle, SimulationConfig Config) CreateSimpleScene()
    {
        var rectangle = CreateRectangle();
        var config = Config(
            false,
            Track(rectangle, "PosX", Frame(0, 10), Frame(1, 100)));
        return (rectangle, config);
    }

    private static SimulationRuntime CreateRuntime(
        Primitive primitive,
        SimulationConfig config)
        => new(new[] { primitive }, config);

    private static SimulationTrackKey Key(Primitive primitive, string propertyName)
        => new(primitive.Id, propertyName);
}
