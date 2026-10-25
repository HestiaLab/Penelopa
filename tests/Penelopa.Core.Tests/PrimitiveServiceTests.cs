using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class PrimitiveServiceTests
{
    [Fact]
    public void Add_StoresPrimitive()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle();

        service.Add(rect);

        Assert.Contains(rect, service.GetAll());
    }

    [Fact]
    public void Add_RaisesChangedWithStructureState()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle();
        DocumentChange? raised = null;
        service.Changed += change => raised = change;

        service.Add(rect);

        Assert.NotNull(raised);
        Assert.True(raised.Value.Kind.HasFlag(DocumentChangeKind.Structure));
        Assert.Contains(rect, raised.Value.State.Roots);
    }

    [Fact]
    public void SetSelected_ReplacesSelection()
    {
        var service = new PrimitiveService();
        var a = new Rectangle();
        var b = new Circle();
        service.Add(a);
        service.Add(b);

        service.SetSelected(a);
        service.SetSelected(b);

        Assert.Single(service.GetSelection());
        Assert.Contains(b, service.GetSelection());
    }

    [Fact]
    public void SetSelectedRange_ReplacesSelectionWithRange()
    {
        var service = new PrimitiveService();
        var a = new Rectangle();
        var b = new Circle();
        var c = new Triangle();

        service.SetSelected(a);
        service.SetSelectedRange(new Primitive[] { b, c });

        Assert.Equal(2, service.GetSelection().Count());
        Assert.DoesNotContain(a, service.GetSelection());
    }

    [Fact]
    public void AppendSelected_AddsToSelection()
    {
        var service = new PrimitiveService();
        var a = new Rectangle();
        var b = new Circle();

        service.SetSelected(a);
        service.AppendSelected(b);

        Assert.Equal(2, service.GetSelection().Count());
    }

    [Fact]
    public void ClearSelection_EmptiesSelection()
    {
        var service = new PrimitiveService();
        var a = new Rectangle();
        service.SetSelected(a);

        service.ClearSelection();

        Assert.Empty(service.GetSelection());
    }

    [Fact]
    public void SelectionChange_RaisesChangedWithSelectionState()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle();
        var events = new List<DocumentChange>();
        service.Changed += change => events.Add(change);

        service.SetSelected(rect);

        var change = Assert.Single(events);
        Assert.True(change.Kind.HasFlag(DocumentChangeKind.Selection));
        Assert.Same(rect, Assert.Single(change.State.Selection));
    }

    [Fact]
    public void ReplaceAll_WithEmptySelection_StillRaisesSelectionChange()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle();
        service.Add(rect);
        service.SetSelected(rect);
        var events = new List<DocumentChange>();
        service.Changed += change => events.Add(change);

        service.ReplaceAll(Array.Empty<Primitive>());

        var change = events.Last();
        Assert.True(change.Kind.HasFlag(DocumentChangeKind.Structure));
        Assert.True(change.Kind.HasFlag(DocumentChangeKind.Selection));
        Assert.Empty(change.State.Roots);
        Assert.Empty(change.State.Selection);
    }

    [Fact]
    public void GetState_ReturnsSnapshotIndependentOfLaterChanges()
    {
        var service = new PrimitiveService();
        var a = new Rectangle();
        var b = new Circle();
        service.Add(a);
        var snapshot = service.GetState();

        service.Add(b);
        service.SetSelected(a);

        Assert.Single(snapshot.Roots);
        Assert.Empty(snapshot.Selection);
        Assert.Equal(2, service.GetState().Roots.Count);
    }

    [Fact]
    public void GetState_AppliesSelectionNormalization()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle();
        container.AddChild(child);
        service.Add(container);

        service.SetSelectedRange(new Primitive[] { container, child });

        Assert.Same(container, Assert.Single(service.GetState().Selection));
    }

    [Fact]
    public void RemoveSelectedPrimitive_RaisesStructureAndSelection()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle();
        service.Add(rect);
        service.SetSelected(rect);
        DocumentChange? raised = null;
        service.Changed += change => raised = change;

        service.Remove(rect);

        Assert.NotNull(raised);
        Assert.True(raised.Value.Kind.HasFlag(DocumentChangeKind.Structure));
        Assert.True(raised.Value.Kind.HasFlag(DocumentChangeKind.Selection));
        Assert.Empty(raised.Value.State.Roots);
        Assert.Empty(raised.Value.State.Selection);
    }

    [Fact]
    public void NotifyPrimitivesChanged_RaisesGeometryKind()
    {
        var service = new PrimitiveService();
        var rect = new Rectangle();
        DocumentChange? raised = null;
        service.Changed += change => raised = change;

        service.NotifyPrimitivesChanged(new Primitive[] { rect });

        Assert.NotNull(raised);
        Assert.Equal(DocumentChangeKind.Geometry, raised.Value.Kind);
    }
}
