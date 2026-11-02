using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class PatchHistoryTests
{
    [Fact]
    public void DeleteNestedContainer_UndoRedo_PreservesIdentityAndOrder()
    {
        var service = new PrimitiveService();
        var child = new Rectangle { Name = { Value = "Leaf" } };
        var container = Container.CreateOffset("Group", 0f, 0f);
        container.AddChild(child);
        var sibling = new Circle();
        service.ReplaceAll(new Primitive[] { container, sibling });

        service.Remove(container);
        service.Undo();

        Assert.Equal(new Primitive[] { container, sibling }, service.GetAll());
        Assert.Same(child, Assert.Single(container.Children));
        Assert.Same(container, child.Parent);
        Assert.Equal(container.Id, service.GetAll()[0].Id);

        service.Redo();
        Assert.Equal(new Primitive[] { sibling }, service.GetAll());

        service.Undo();
        Assert.Same(container, service.GetAll()[0]);
        Assert.Equal(container.Id, service.GetAll()[0].Id);
    }

    [Fact]
    public void AddToContainer_UndoRedo_RestoresRootIndexAndChildIndex()
    {
        var service = new PrimitiveService();
        var first = new Rectangle { Name = { Value = "A" } };
        var second = new Rectangle { Name = { Value = "B" } };
        var container = Container.CreateOffset("Group", 0f, 0f);
        service.ReplaceAll(new Primitive[] { first, container, second });

        service.AddToContainer(container, second);
        Assert.Equal(new Primitive[] { first, container }, service.GetAll());
        Assert.Same(second, Assert.Single(container.Children));

        service.Undo();
        Assert.Equal(new Primitive[] { first, container, second }, service.GetAll());
        Assert.Null(second.Parent);

        service.Redo();
        Assert.Equal(new Primitive[] { first, container }, service.GetAll());
        Assert.Same(second, Assert.Single(container.Children));
    }

    [Fact]
    public void Selection_IsRestoredPerEntry()
    {
        var service = new PrimitiveService();
        var first = new Circle();
        var second = new Rectangle();
        service.Add(first);
        service.Add(second);
        service.SetSelected(first);
        Assert.True(service.TrySetProperty(first, first.Radius, "20", out _));

        service.SetSelected(second);
        service.Undo();

        Assert.Same(first, Assert.Single(service.GetSelection()));

        service.Redo();
        Assert.Same(first, Assert.Single(service.GetSelection()));
    }

    [Fact]
    public void CancelTransaction_RestoresPropertiesAndSelection()
    {
        var service = new PrimitiveService();
        var rectangle = new Rectangle { PosX = { Value = 0f } };
        service.Add(rectangle);
        service.SetSelected(rectangle);
        service.BeginTransaction();
        service.RecordBeforeChange(new Primitive[] { rectangle });
        rectangle.Translate(40f, 0f);
        service.SetSelectedRange(Array.Empty<Primitive>());

        service.CancelTransaction();

        Assert.Equal(0f, rectangle.PosX.Value);
        Assert.Same(rectangle, Assert.Single(service.GetSelection()));
        Assert.True(service.CanUndo);
    }

    [Fact]
    public void HistoryBudget_EvictsOldestEntries()
    {
        var service = new PrimitiveService();
        service.ConfigureHistoryBudget(2, long.MaxValue);

        service.Add(new Circle { Name = { Value = "A" } });
        service.Add(new Rectangle { Name = { Value = "B" } });
        service.Add(new Triangle { Name = { Value = "C" } });

        Assert.Equal(2, service.UndoDepth);
        service.Undo();
        service.Undo();

        Assert.False(service.CanUndo);
        Assert.Equal("A", Assert.Single(service.GetAll()).Name.Value);
    }

    [Fact]
    public void HistoryBudget_EvictsWhenEstimatedBytesExceedLimit()
    {
        var service = new PrimitiveService();
        service.ConfigureHistoryBudget(500, 2_000);

        service.Add(new Circle());
        service.Add(new Rectangle());

        Assert.Equal(1, service.UndoDepth);
        Assert.InRange(service.EstimatedBytes, 1, 2_000);
    }

    [Fact]
    public void PropertyEdits_CoalesceWithinWindow()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);

        Assert.True(service.TrySetProperty(circle, circle.Radius, "20", out _));
        Assert.True(service.TrySetProperty(circle, circle.Radius, "30", out _));

        Assert.Equal(2, service.UndoDepth);
        service.Undo();
        Assert.Equal(5f, circle.Radius.Value);
    }

    [Fact]
    public void HistoryLabels_ReflectActionsAndRedo()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);
        Assert.Equal("Add circle", service.UndoLabel);

        Assert.True(service.TrySetProperty(circle, circle.Radius, "20", out _));
        Assert.Equal("Set Radius", service.UndoLabel);

        service.Undo();
        Assert.Equal("Set Radius", service.RedoLabel);
    }

    [Fact]
    public void GestureTransaction_RecordsRecordedDirectMutation()
    {
        var service = new PrimitiveService();
        var rectangle = new Rectangle
        {
            PosX = { Value = 2f },
            PosY = { Value = 2f },
        };
        service.Add(rectangle);

        service.BeginTransaction(DocumentCommands.Gesture, "Move selection");
        service.RecordBeforeChange(new Primitive[] { rectangle });
        rectangle.Translate(10f, 3f);
        service.CommitTransaction();

        Assert.Equal("Move selection", service.UndoLabel);
        service.Undo();
        Assert.Equal(2f, rectangle.PosX.Value);
        Assert.Equal(2f, rectangle.PosY.Value);

        service.Redo();
        Assert.Equal(12f, rectangle.PosX.Value);
        Assert.Equal(5f, rectangle.PosY.Value);
    }

    [Fact]
    public void GroupUngroup_MultipleUndoRedo_RestoresStructure()
    {
        var service = new PrimitiveService();
        var first = new Rectangle { Name = { Value = "A" } };
        var second = new Rectangle { Name = { Value = "B" } };
        var third = new Circle { Name = { Value = "C" } };
        service.ReplaceAll(new Primitive[] { first, second, third });
        service.SetSelectedRange(new Primitive[] { first, second });

        var group = service.GroupSelection(out var error);
        Assert.NotNull(group);
        Assert.Null(error);
        Assert.Equal(new Primitive[] { group, third }, service.GetAll());

        service.Undo();
        Assert.Equal(new Primitive[] { first, second, third }, service.GetAll());
        Assert.Empty(group.Children);

        service.Redo();
        Assert.Equal(new Primitive[] { group, third }, service.GetAll());
        Assert.Equal(new Primitive[] { first, second }, group.Children);

        Assert.True(service.TryUngroup(group, out error));
        Assert.Null(error);
        Assert.Equal(new Primitive[] { first, second, third }, service.GetAll());

        service.Undo();
        Assert.Equal(new Primitive[] { group, third }, service.GetAll());
        Assert.Equal(new Primitive[] { first, second }, group.Children);
    }

    [Fact]
    public void AlignAndUndo_RestoresAllAffectedProperties()
    {
        var service = new PrimitiveService();
        var a = new Rectangle { PosX = { Value = 0f } };
        var b = new Rectangle { PosX = { Value = 40f } };
        service.Add(a);
        service.Add(b);
        service.SetSelectedRange(new Primitive[] { a, b });

        Assert.True(service.Align(AlignType.Left));
        Assert.Equal(0f, b.PosX.Value);
        Assert.Equal("Align Left", service.UndoLabel);

        service.Undo();
        Assert.Equal(40f, b.PosX.Value);
    }

    [Fact]
    public void DeleteLargeSubtree_UndoRestoresSameInstanceSelectionAndQuickly()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("root", 0f, 0f);
        var children = new Rectangle[5_000];
        for (var index = 0; index < children.Length; index++)
        {
            children[index] = new Rectangle { Name = { Value = $"R{index}" } };
            container.AddChild(children[index]);
        }

        service.ReplaceAll(new Primitive[] { container });
        var target = children[2_500];
        service.SetSelected(target);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        service.Remove(target);
        service.Undo();
        stopwatch.Stop();

        Assert.Same(target, Assert.Single(service.GetSelection()));
        Assert.Contains(target, container.Children);
        Assert.Same(container, target.Parent);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1_000,
            $"Delete + undo of a 5k subtree took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void PropertyEdit_OnLargeDocument_StoresOnlyTheChangeAndUndoesQuickly()
    {
        var service = new PrimitiveService();
        var roots = new List<Primitive>(10_000);
        for (var index = 0; index < 10_000; index++)
        {
            roots.Add(new Rectangle { Name = { Value = $"R{index}" } });
        }

        service.ReplaceAll(roots);
        var target = (Rectangle)roots[5_000];
        service.SetSelected(target);

        Assert.True(service.TrySetProperty(target, target.PosX, "42", out _));
        Assert.InRange(service.EstimatedBytes, 1, 4_096);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < 20; index++)
        {
            service.Undo();
            service.Redo();
        }

        stopwatch.Stop();
        Assert.Equal(42f, target.PosX.Value);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 1_000,
            $"20 undo/redo cycles on a 10k document took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void DeleteSelectedPrimitive_UndoRestoresSelection()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        var rectangle = new Rectangle();
        service.ReplaceAll(new Primitive[] { circle, rectangle });
        service.SetSelected(circle);

        service.Remove(circle);
        Assert.Empty(service.GetSelection());

        service.Undo();
        Assert.Same(circle, Assert.Single(service.GetSelection()));
    }
}
