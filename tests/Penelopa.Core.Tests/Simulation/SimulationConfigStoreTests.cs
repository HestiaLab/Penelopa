using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Xunit;
using static Penelopa.Core.Tests.Simulation.SimulationTestHelpers;

namespace Penelopa.Core.Tests.Simulation;

public class SimulationConfigStoreTests
{
    private readonly PrimitiveService _service = new();

    [Fact]
    public void AddTrack_CreatesTwoKeyframesAtPlayheadAndPlusOneSecond()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        var changed = 0;
        store.Changed += () => changed++;

        Assert.True(store.TryAddTrack(rectangle.Id, "PosX", 0.5d, 10f, out var error), error);

        var track = Assert.Single(store.Config.Tracks);
        Assert.Equal(rectangle.Id, track.PrimitiveId);
        Assert.Equal("PosX", track.PropertyName);
        Assert.Equal(2, track.Keyframes.Count);
        Assert.Equal(0.5d, track.Keyframes[0].Time, 9);
        Assert.Equal(1.5d, track.Keyframes[1].Time, 9);
        Assert.All(track.Keyframes, keyframe =>
        {
            Assert.Equal(10f, keyframe.Value);
            Assert.Equal(InterpolationKind.Linear, keyframe.Interpolation);
        });
        Assert.Equal(1, changed);
    }

    [Fact]
    public void AddTrack_AtTimelineEndFallsBackBeforePlayhead()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();

        Assert.True(store.TryAddTrack(rectangle.Id, "PosX", 3_600d, 10f, out _));

        var keyframes = Assert.Single(store.Config.Tracks).Keyframes;
        Assert.Equal(3_599d, keyframes[0].Time, 9);
        Assert.Equal(3_600d, keyframes[1].Time, 9);
    }

    [Fact]
    public void AddTrack_NearTimelineEndKeepsMinimumGap()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        const double time = 3_599.9995d;

        Assert.True(store.TryAddTrack(rectangle.Id, "PosX", time, 10f, out _));

        var keyframes = Assert.Single(store.Config.Tracks).Keyframes;
        Assert.Equal(time, keyframes[1].Time, 6);
        Assert.True(
            keyframes[1].Time - keyframes[0].Time
            >= SimulationLimits.MinKeyframeGapSeconds);
    }

    [Fact]
    public void AddTrack_RejectsDuplicateMissingAndInvalidValues()
    {
        var rectangle = AddRectangle();
        var circle = new Circle();
        _service.Add(circle);
        var store = CreateStore();

        Assert.True(store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out _));

        Assert.False(store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var error));
        Assert.NotNull(error);

        Assert.False(store.TryAddTrack(Guid.NewGuid(), "PosX", 0d, 10f, out error));
        Assert.NotNull(error);

        Assert.False(store.TryAddTrack(rectangle.Id, "Nope", 0d, 10f, out error));
        Assert.NotNull(error);

        Assert.False(store.TryAddTrack(rectangle.Id, "Color", 0d, 10f, out error));
        Assert.NotNull(error);

        Assert.False(store.TryAddTrack(circle.Id, "Radius", 0d, -1f, out error));
        Assert.NotNull(error);

        Assert.False(store.TryAddTrack(rectangle.Id, "PosY", -1d, 10f, out error));
        Assert.NotNull(error);

        Assert.False(store.TryAddTrack(rectangle.Id, "Width", 0d, 0f, out error));
        Assert.NotNull(error);

        Assert.Single(store.Config.Tracks);
    }

    [Fact]
    public void SetKeyframeValue_InsertsAndUpdatesExistingTime()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");

        Assert.True(store.TrySetKeyframeValue(
            rectangle.Id,
            "PosX",
            2d,
            50f,
            out var error),
            error);

        var track = SingleTrack(store, rectangle, "PosX");
        Assert.Equal(3, track.Keyframes.Count);
        Assert.Equal(2d, track.Keyframes[2].Time, 9);
        Assert.Equal(50f, track.Keyframes[2].Value);

        Assert.True(store.TrySetKeyframeValue(
            rectangle.Id,
            "PosX",
            1.0004d,
            30f,
            out error),
            error);

        track = SingleTrack(store, rectangle, "PosX");
        Assert.Equal(3, track.Keyframes.Count);
        Assert.Equal(30f, track.Keyframes[1].Value);
        Assert.Equal(1d, track.Keyframes[1].Time, 9);
    }

    [Fact]
    public void SetKeyframeValue_RejectsTooCloseAndInvalidInputs()
    {
        var rectangle = AddRectangle();
        var circle = new Circle();
        _service.Add(circle);
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");

        Assert.False(store.TrySetKeyframeValue(
            rectangle.Id,
            "PosX",
            1.0006d,
            30f,
            out var error));
        Assert.NotNull(error);

        Assert.False(store.TrySetKeyframeValue(
            rectangle.Id,
            "PosX",
            -1d,
            30f,
            out error));
        Assert.NotNull(error);

        Assert.False(store.TrySetKeyframeValue(
            Guid.NewGuid(),
            "PosX",
            2d,
            30f,
            out error));
        Assert.NotNull(error);

        Assert.True(store.TryAddTrack(circle.Id, "Radius", 0d, 5f, out error), error);
        Assert.False(store.TrySetKeyframeValue(
            circle.Id,
            "Radius",
            1d,
            -1f,
            out error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UpdateKeyframe_ChangesFieldsAndUndoRestores()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");

        Assert.True(store.TryUpdateKeyframe(
            rectangle.Id,
            "PosX",
            1,
            1.5d,
            50f,
            InterpolationKind.EaseIn,
            null,
            out var error),
            error);

        var track = SingleTrack(store, rectangle, "PosX");
        Assert.Equal(1.5d, track.Keyframes[1].Time, 9);
        Assert.Equal(50f, track.Keyframes[1].Value);
        Assert.Equal(InterpolationKind.EaseIn, track.Keyframes[1].Interpolation);

        store.Undo();
        track = SingleTrack(store, rectangle, "PosX");
        Assert.Equal(1d, track.Keyframes[1].Time, 9);
        Assert.Equal(100f, track.Keyframes[1].Value);
        Assert.Equal(InterpolationKind.Linear, track.Keyframes[1].Interpolation);

        store.Redo();
        track = SingleTrack(store, rectangle, "PosX");
        Assert.Equal(1.5d, track.Keyframes[1].Time, 9);
        Assert.Equal(50f, track.Keyframes[1].Value);
    }

    [Fact]
    public void UpdateKeyframe_RejectsCrossingAndInvalidBezier()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");
        Assert.True(store.TrySetKeyframeValue(rectangle.Id, "PosX", 2d, 100f, out _));
        var before = store.Config;

        Assert.False(store.TryUpdateKeyframe(
            rectangle.Id,
            "PosX",
            1,
            2.5d,
            50f,
            InterpolationKind.Linear,
            null,
            out var error));
        Assert.NotNull(error);

        Assert.False(store.TryUpdateKeyframe(
            rectangle.Id,
            "PosX",
            0,
            0d,
            10f,
            InterpolationKind.CubicBezier,
            new CubicBezier(1.1d, 0d, 0d, 1d),
            out error));
        Assert.NotNull(error);

        Assert.False(store.TryUpdateKeyframe(
            rectangle.Id,
            "PosX",
            0,
            0d,
            10f,
            InterpolationKind.CubicBezier,
            null,
            out error));
        Assert.NotNull(error);

        Assert.False(store.TryUpdateKeyframe(
            rectangle.Id,
            "PosX",
            0,
            0d,
            10f,
            InterpolationKind.Linear,
            new CubicBezier(0d, 0d, 1d, 1d),
            out error));
        Assert.NotNull(error);

        Assert.False(store.TryUpdateKeyframe(
            rectangle.Id,
            "PosX",
            0,
            0d,
            10f,
            (InterpolationKind)999,
            null,
            out error));
        Assert.NotNull(error);

        Assert.False(store.TryUpdateKeyframe(
            rectangle.Id,
            "PosX",
            9,
            0d,
            10f,
            InterpolationKind.Linear,
            null,
            out error));
        Assert.NotNull(error);

        Assert.Same(before, store.Config);
    }

    [Fact]
    public void RemoveKeyframe_RemovesTrackWhenLastFrameIsDeleted()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");

        Assert.True(store.TryRemoveKeyframe(rectangle.Id, "PosX", 0, out var error), error);
        Assert.Single(SingleTrack(store, rectangle, "PosX").Keyframes);

        Assert.True(store.TryRemoveKeyframe(rectangle.Id, "PosX", 0, out error), error);
        Assert.Empty(store.Config.Tracks);
        Assert.True(store.CanUndo);

        store.Undo();
        Assert.Single(SingleTrack(store, rectangle, "PosX").Keyframes);

        Assert.False(store.TryRemoveKeyframe(rectangle.Id, "PosX", 5, out error));
        Assert.NotNull(error);
    }

    [Fact]
    public void RemoveTrack_UndoAndRedoRestoreIt()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");

        Assert.True(store.TryRemoveTrack(rectangle.Id, "PosX", out var error), error);
        Assert.Empty(store.Config.Tracks);

        store.Undo();
        Assert.Single(store.Config.Tracks);

        store.Redo();
        Assert.Empty(store.Config.Tracks);

        Assert.False(store.TryRemoveTrack(Guid.NewGuid(), "PosX", out error));
        Assert.NotNull(error);
    }

    [Fact]
    public void UndoRedo_RespectsFiftyStepLimitAndClearsRedo()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");

        for (var value = 1; value <= 55; value++)
        {
            Assert.True(store.TrySetKeyframeValue(
                rectangle.Id,
                "PosX",
                1d,
                value,
                out var error),
                error);
        }

        for (var step = 0; step < SimulationLimits.ConfigUndoDepth; step++)
        {
            store.Undo();
        }

        Assert.False(store.CanUndo);
        Assert.True(store.CanRedo);
        Assert.Equal(5f, SingleTrack(store, rectangle, "PosX").Keyframes[1].Value);

        for (var step = 0; step < SimulationLimits.ConfigUndoDepth; step++)
        {
            store.Redo();
        }

        Assert.False(store.CanRedo);
        Assert.Equal(55f, SingleTrack(store, rectangle, "PosX").Keyframes[1].Value);

        Assert.True(store.TrySetKeyframeValue(
            rectangle.Id,
            "PosX",
            1d,
            99f,
            out var error2),
            error2);
        Assert.False(store.CanRedo);
    }

    [Fact]
    public void LoopChangeIsNotPartOfConfigurationUndo()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        Assert.True(store.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var error), error);

        store.SetLoop(true);
        Assert.True(store.Config.Loop);

        store.Undo();
        Assert.Empty(store.Config.Tracks);
        Assert.True(store.Config.Loop);

        store.Redo();
        Assert.Single(store.Config.Tracks);
        Assert.True(store.Config.Loop);
    }

    [Fact]
    public void ExportActive_SortsAndExcludesDormantTracks()
    {
        var first = AddRectangle("First");
        var second = AddRectangle("Second");
        var store = CreateStore();

        Assert.True(store.TryAddTrack(second.Id, "PosY", 0d, 10f, out _));
        Assert.True(store.TryAddTrack(first.Id, "PosY", 0d, 10f, out _));
        Assert.True(store.TryAddTrack(second.Id, "PosX", 0d, 10f, out _));
        Assert.True(store.TryAddTrack(first.Id, "PosX", 0d, 10f, out _));

        var exported = store.ExportActive();
        Assert.Equal(
            new[] { "PosX", "PosY", "PosX", "PosY" },
            exported.Tracks.Select(track => track.PropertyName));
        Assert.Equal(
            new[] { first.Id, first.Id, second.Id, second.Id },
            exported.Tracks.Select(track => track.PrimitiveId));

        _service.Remove(second);

        Assert.Equal(2, store.GetDormantTracks().Count);
        exported = store.ExportActive();
        Assert.All(exported.Tracks, track => Assert.Equal(first.Id, track.PrimitiveId));
    }

    [Fact]
    public void RemoveDormantTracks_RemovesAndUndoRestores()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");
        _service.Remove(rectangle);

        Assert.Single(store.GetDormantTracks());
        Assert.Equal(1, store.RemoveDormantTracks());
        Assert.Empty(store.Config.Tracks);
        Assert.True(store.CanUndo);

        store.Undo();
        Assert.Single(store.Config.Tracks);
        Assert.Single(store.GetDormantTracks());

        Assert.Equal(1, store.RemoveDormantTracks());
        Assert.Empty(store.Config.Tracks);
        Assert.Equal(0, store.RemoveDormantTracks());
    }

    [Fact]
    public void ClearHistory_KeepsConfigAndClearsStacks()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");
        Assert.True(store.CanUndo);

        store.ClearHistory();

        Assert.False(store.CanUndo);
        Assert.False(store.CanRedo);
        Assert.Single(store.Config.Tracks);
    }

    [Fact]
    public void ReplaceConfig_ClearsUndoRedoAndRaisesChanged()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");
        var changed = 0;
        store.Changed += () => changed++;

        var replacement = Config(
            true,
            Track(rectangle, "PosY", Frame(0, 20), Frame(1, 30)));
        store.ReplaceConfig(replacement);

        Assert.Same(replacement, store.Config);
        Assert.False(store.CanUndo);
        Assert.False(store.CanRedo);
        Assert.Equal(1, changed);
    }


    [Fact]
    public void CaptureRestoreState_RestoresConfigAndUndoRedo()
    {
        var rectangle = AddRectangle();
        var store = CreateStore();
        AddLinearTrack(store, rectangle, "PosX");
        store.Undo();
        store.SetLoop(true);

        var state = store.CaptureState();
        store.ReplaceConfig(SimulationConfig.Empty);
        Assert.False(store.CanUndo);
        Assert.False(store.CanRedo);

        store.RestoreState(state);

        Assert.True(store.Config.Loop);
        var track = Assert.Single(store.Config.Tracks);
        Assert.Equal(10f, track.Keyframes[1].Value);
        Assert.True(store.CanUndo);
        Assert.True(store.CanRedo);

        store.Redo();
        Assert.Equal(100f, Assert.Single(store.Config.Tracks).Keyframes[1].Value);

        store.Undo();
        Assert.Equal(10f, Assert.Single(store.Config.Tracks).Keyframes[1].Value);
    }

    private SimulationConfigStore CreateStore() => new(_service);

    private Rectangle AddRectangle(string name = "Rectangle")
    {
        var rectangle = CreateRectangle();
        rectangle.Name.Value = name;
        _service.Add(rectangle);
        return rectangle;
    }

    private static void AddLinearTrack(
        SimulationConfigStore store,
        Primitive primitive,
        string propertyName,
        float start = 10f,
        float end = 100f)
    {
        Assert.True(
            store.TryAddTrack(primitive.Id, propertyName, 0d, start, out var error),
            error);
        Assert.True(
            store.TryUpdateKeyframe(
                primitive.Id,
                propertyName,
                1,
                1d,
                end,
                InterpolationKind.Linear,
                null,
                out error),
            error);
    }

    private static SimulationTrack SingleTrack(
        SimulationConfigStore store,
        Primitive primitive,
        string propertyName)
        => store.Config.Tracks.Single(track =>
            track.PrimitiveId == primitive.Id
            && track.PropertyName == propertyName);
}
