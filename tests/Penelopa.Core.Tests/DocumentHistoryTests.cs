using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class DocumentHistoryTests
{
    [Fact]
    public void Undo_AfterAdd_RemovesPrimitive()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle { PosX = { Value = 1f } };

        service.Add(rect);
        service.Undo();

        Assert.Empty(service.GetAll());
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void Undo_AfterRemove_RestoresPrimitive()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 20f } };
        service.Add(rect);
        service.Undo(); // rewind the Add so the history state is clean

        service.Add(rect);
        service.Remove(rect);
        service.Undo();

        Assert.Contains(rect, service.GetAll());
        Assert.Equal(10f, rect.PosX.Value);
    }

    [Fact]
    public void Undo_AfterGesture_RestoresGeometry()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        service.Add(rect);
        service.Undo(); // clean history
        service.Add(rect);

        service.BeginTransaction();
        service.RecordBeforeChange(new Primitive[] { rect });
        rect.Translate(30f, 0f);
        service.CommitTransaction();
        service.Undo();

        Assert.Equal(0f, rect.PosX.Value);
    }

    [Fact]
    public void Redo_RestoresAfterUndo()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle { PosX = { Value = 0f } };
        service.Add(rect);
        service.Undo();
        service.Redo();

        Assert.Contains(rect, service.GetAll());
        Assert.False(service.CanRedo);
    }

    [Fact]
    public void Undo_EmptyHistory_IsNoOp()
    {
        var service = new PrimitiveService();

        service.Undo();
        service.Redo();

        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void Undo_AfterAddToContainer_RestoresStructure()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle();
        service.Add(container);
        service.Undo();
        service.Add(container);

        service.AddToContainer(container, child);
        service.Undo();

        Assert.Empty(container.Children);
        Assert.Null(child.Parent);
    }

    [Fact]
    public void Undo_AfterGestureOnContainerChild_RestoresChildGeometry()
    {
        var service = new PrimitiveService();
        var container = Container.CreateRotation("r", 90f);
        var child = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        service.Add(container);
        service.Undo();
        service.Add(container);
        service.AddToContainer(container, child);

        service.BeginTransaction();
        service.RecordBeforeChange(new Primitive[] { child });
        child.Translate(0f, 10f);
        service.CommitTransaction();
        service.Undo();

        Assert.Equal(0f, child.PosX.Value);
        Assert.Equal(0f, child.PosY.Value);
    }

    [Fact]
    public void NewCapture_ClearsRedoStack()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle();
        service.Add(rect);
        service.Undo();
        var circle = new Circle();

        service.Add(circle); // new action after undo

        Assert.False(service.CanRedo);
    }
}
