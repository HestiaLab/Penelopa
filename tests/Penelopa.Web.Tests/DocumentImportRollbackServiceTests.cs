using Microsoft.JSInterop;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentImportRollbackServiceTests
{
    private readonly PrimitiveService _primitives = new();
    private readonly SimulationConfigStore _config;
    private readonly DocumentStorage _storage;
    private readonly DocumentImportRollbackService _rollback;

    public DocumentImportRollbackServiceTests()
    {
        _config = new SimulationConfigStore(_primitives);
        _storage = new DocumentStorage(new TestJsRuntime());
        _rollback = new DocumentImportRollbackService(_primitives, _config, _storage);
    }

    [Fact]
    public void UndoImport_RestoresDocumentSelectionAndDocumentHistory()
    {
        var circle = new Circle();
        _primitives.Add(circle);
        _primitives.SetSelected(circle);
        Assert.True(
            _primitives.TrySetProperty(circle, circle.Radius, "20", out var propertyError),
            propertyError);
        _primitives.Undo();
        Assert.Equal(5f, circle.Radius.Value);

        var imported = new Rectangle { Name = { Value = "Imported" } };
        Assert.True(
            _rollback.TryApplyImport(
                "scene.penelopa.json",
                ImportAction(imported, selectImported: true),
                out var applyError),
            applyError);

        Assert.True(_rollback.CanUndo);
        Assert.Equal("scene.penelopa.json", _rollback.ImportedFileName);
        Assert.Equal("Imported", Assert.Single(_primitives.GetAll()).Name.Value);
        Assert.False(_primitives.CanUndo);
        Assert.False(_primitives.CanRedo);

        Assert.True(_rollback.UndoImport());

        Assert.Same(circle, Assert.Single(_primitives.GetAll()));
        Assert.Same(circle, Assert.Single(_primitives.GetSelection()));
        Assert.Equal(5f, circle.Radius.Value);
        Assert.True(_primitives.CanUndo);
        Assert.True(_primitives.CanRedo);
        _primitives.Redo();
        Assert.Equal(20f, circle.Radius.Value);
        _primitives.Undo();
        Assert.Equal(5f, circle.Radius.Value);
        Assert.False(_rollback.CanUndo);
        Assert.Null(_rollback.ImportedFileName);
    }

    [Fact]
    public void UndoImport_RestoresSimulationConfigAndConfigHistory()
    {
        var rectangle = new Rectangle();
        _primitives.Add(rectangle);
        Assert.True(
            _config.TryAddTrack(rectangle.Id, "PosX", 0d, 10f, out var addError),
            addError);
        Assert.True(
            _config.TryUpdateKeyframe(
                rectangle.Id,
                "PosX",
                1,
                1d,
                100f,
                InterpolationKind.Linear,
                null,
                out var updateError),
            updateError);
        _config.Undo();
        _config.SetLoop(true);
        Assert.Equal(10f, Assert.Single(_config.Config.Tracks).Keyframes[1].Value);

        var imported = new Rectangle { Name = { Value = "Imported" } };
        Assert.True(
            _rollback.TryApplyImport("scene.penelopa.json", ImportAction(imported), out var applyError),
            applyError);
        Assert.Empty(_config.Config.Tracks);
        Assert.False(_config.Config.Loop);

        Assert.True(_rollback.UndoImport());

        Assert.True(_config.Config.Loop);
        var track = Assert.Single(_config.Config.Tracks);
        Assert.Equal(10f, track.Keyframes[1].Value);
        Assert.True(_config.CanUndo);
        Assert.True(_config.CanRedo);
        _config.Redo();
        Assert.Equal(100f, Assert.Single(_config.Config.Tracks).Keyframes[1].Value);
        _config.Undo();
        Assert.Equal(10f, Assert.Single(_config.Config.Tracks).Keyframes[1].Value);
    }

    [Fact]
    public void UndoImport_IsClearedByContentConfigSelectionAndTransientRules()
    {
        var imported = new Rectangle { Name = { Value = "Imported" } };
        Assert.True(
            _rollback.TryApplyImport("one.penelopa.json", ImportAction(imported), out _));

        _primitives.ClearSelection();
        _primitives.NotifyTransientValuesChanged();
        Assert.True(_rollback.CanUndo);

        _primitives.Add(new Circle());
        Assert.False(_rollback.CanUndo);

        Assert.True(
            _rollback.TryApplyImport("two.penelopa.json", ImportAction(new Circle()), out _));
        _config.SetLoop(true);
        Assert.False(_rollback.CanUndo);
    }

    [Fact]
    public async Task UndoImport_IsClearedByASuccessfulSave()
    {
        var imported = new Rectangle { Name = { Value = "Imported" } };
        Assert.True(
            _rollback.TryApplyImport("scene.penelopa.json", ImportAction(imported), out _));
        Assert.True(_rollback.CanUndo);

        var result = await _storage.SaveAsync(
            _primitives.GetAll(),
            _primitives.GetSelection(),
            _config.ExportActive());

        Assert.True(result.Success);
        Assert.False(_rollback.CanUndo);
    }

    [Fact]
    public void FailedImport_RestoresTheApplyStateAndKeepsThePreviousSnapshot()
    {
        var first = new Rectangle { Name = { Value = "First" } };
        Assert.True(
            _rollback.TryApplyImport("first.penelopa.json", ImportAction(first), out _));

        var failure = _rollback.TryApplyImport(
            "second.penelopa.json",
            () =>
            {
                _primitives.Add(new Circle { Name = { Value = "Partial" } });
                throw new InvalidOperationException("apply failed");
            },
            out var error);

        Assert.False(failure);
        Assert.NotNull(error);
        Assert.Equal("First", Assert.Single(_primitives.GetAll()).Name.Value);
        Assert.True(_rollback.CanUndo);
        Assert.Equal("first.penelopa.json", _rollback.ImportedFileName);

        Assert.True(_rollback.UndoImport());
        Assert.Empty(_primitives.GetAll());
    }

    private Action ImportAction(Primitive imported, bool selectImported = true)
    {
        var json = DocumentSerializer.Serialize(
            new[] { imported },
            selectImported ? new[] { imported } : Array.Empty<Primitive>());
        var document = DocumentSerializer.Deserialize(json)
            ?? throw new InvalidOperationException("Test document is invalid.");
        return () =>
        {
            var selected = document.ResolveSelection();
            _primitives.ReplaceAll(document.Roots);
            _primitives.SetSelectedRange(selected);
            _config.ReplaceConfig(document.Simulation ?? SimulationConfig.Empty);
        };
    }

    private sealed class TestJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args)
        {
            object result = identifier switch
            {
                "penelopa.readStorage" => new StorageReadResult(true, null, null),
                "penelopa.writeStorage" => new StorageWriteResult(true, null),
                _ => throw new InvalidOperationException(
                    $"Unexpected JS invocation '{identifier}'."),
            };
            return ValueTask.FromResult((TValue)result);
        }
    }
}
