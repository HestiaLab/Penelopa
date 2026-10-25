using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class GroupingTests
{
    private static Rectangle Rect(string name, float x, float y, float width = 10f, float height = 10f)
        => new()
        {
            Name = { Value = name },
            PosX = { Value = x },
            PosY = { Value = y },
            Width = { Value = width },
            Height = { Value = height },
        };

    [Fact]
    public void GroupSelection_CreatesIdentityContainerAtFirstSelectedRootAndPreservesWorldGeometry()
    {
        var service = new PrimitiveService();
        var first = Rect("A", 0f, 0f);
        var middle = Rect("B", 30f, 10f, 20f, 20f);
        var last = Rect("C", 100f, 0f);
        service.ReplaceAll(new Primitive[] { first, middle, last });
        service.SetSelectedRange(new Primitive[] { last, first });
        var firstBefore = first.GetWorldBoundingBox();
        var lastBefore = last.GetWorldBoundingBox();
        var changes = new List<DocumentChange>();
        service.Changed += changes.Add;

        var group = service.GroupSelection(out var error);

        Assert.NotNull(group);
        Assert.Null(error);
        Assert.Equal(new Primitive[] { group!, middle }, service.GetAll());
        Assert.Equal(new[] { "A", "C" }, group!.Children.Select(child => child.Name.Value));
        Assert.Equal(ContainerKind.Offset, group.Kind);
        Assert.Equal(0f, group.OffsetX.Value);
        Assert.Equal(0f, group.OffsetY.Value);
        Assert.Equal(0f, group.Rotation.Value);
        Assert.Equal(1f, group.ScaleX.Value);
        Assert.Equal(1f, group.ScaleY.Value);
        Assert.Same(group, first.Parent);
        Assert.Same(group, last.Parent);
        Assert.Same(group, Assert.Single(service.GetSelection()));
        Assert.Equal(firstBefore, first.GetWorldBoundingBox());
        Assert.Equal(lastBefore, last.GetWorldBoundingBox());

        var change = Assert.Single(changes);
        Assert.True(change.Kind.HasFlag(DocumentChangeKind.Structure));
        Assert.True(change.Kind.HasFlag(DocumentChangeKind.Selection));
        Assert.Same(group, Assert.Single(change.State.Selection));
    }

    [Fact]
    public void GroupSelection_UndoRedoRestoresStructureAndFiltersSelection()
    {
        var service = new PrimitiveService();
        var first = Rect("A", 0f, 0f);
        var second = Rect("B", 30f, 0f);
        var third = Rect("C", 60f, 0f);
        service.ReplaceAll(new Primitive[] { first, second, third });
        service.SetSelectedRange(new Primitive[] { first, second });
        var group = service.GroupSelection(out _)!;

        Assert.Equal(new Primitive[] { group, third }, service.GetAll());
        Assert.Same(group, Assert.Single(service.GetSelection()));

        service.Undo();

        Assert.Equal(new Primitive[] { first, second, third }, service.GetAll());
        Assert.Null(first.Parent);
        Assert.Null(second.Parent);
        Assert.Empty(group.Children);
        Assert.Empty(service.GetSelection());
        Assert.False(service.CanUndo);
        Assert.True(service.CanRedo);

        service.Redo();

        Assert.Equal(new Primitive[] { group, third }, service.GetAll());
        Assert.Equal(new Primitive[] { first, second }, group.Children);
        Assert.Same(group, first.Parent);
        Assert.Same(group, second.Parent);
        Assert.Empty(service.GetSelection());
    }

    [Fact]
    public void GroupSelection_RejectsFewerThanTwoRootsAndNonRootSelections()
    {
        var service = new PrimitiveService();
        var first = Rect("A", 0f, 0f);
        var second = Rect("B", 30f, 0f);
        var container = Container.CreateOffset("Container", 20f, 20f);
        var child = Rect("Child", 10f, 10f);
        container.AddChild(child);
        service.ReplaceAll(new Primitive[] { first, second, container });

        Assert.Null(service.GroupSelection(out var noSelectionError));
        Assert.False(string.IsNullOrWhiteSpace(noSelectionError));

        service.SetSelected(first);
        Assert.Null(service.GroupSelection(out var singleError));
        Assert.Contains("two", singleError, StringComparison.OrdinalIgnoreCase);

        service.SetSelected(child);
        Assert.Null(service.GroupSelection(out var childError));
        Assert.Contains("root", childError, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(new Primitive[] { first, second, container }, service.GetAll());
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void GroupSelection_WithNestedContainer_PreservesDescendantWorldBoundsThroughUndoAndRedo()
    {
        var service = new PrimitiveService();
        var inner = Container.CreateRotation("Inner", 45f);
        var descendant = Rect("Leaf", 5f, 5f, 20f, 10f);
        inner.AddChild(descendant);
        var outer = Container.CreateOffset("Outer", 20f, 30f);
        outer.AddChild(inner);
        var external = Rect("External", 100f, 0f);
        service.ReplaceAll(new Primitive[] { outer, external });
        service.SetSelectedRange(new Primitive[] { external, outer });
        var descendantBefore = descendant.GetWorldBoundingBox();
        var externalBefore = external.GetWorldBoundingBox();

        var group = service.GroupSelection(out var error);

        Assert.NotNull(group);
        Assert.Null(error);
        Assert.Equal(descendantBefore, descendant.GetWorldBoundingBox());
        Assert.Equal(externalBefore, external.GetWorldBoundingBox());
        Assert.Same(inner, descendant.Parent);
        Assert.Same(outer, inner.Parent);
        Assert.Same(group, outer.Parent);
        Assert.Same(group, external.Parent);

        service.Undo();
        Assert.Equal(descendantBefore, descendant.GetWorldBoundingBox());
        Assert.Same(inner, descendant.Parent);
        Assert.Same(outer, inner.Parent);
        Assert.Null(outer.Parent);
        Assert.Null(external.Parent);

        service.Redo();
        Assert.Equal(descendantBefore, descendant.GetWorldBoundingBox());
        Assert.Same(group, outer.Parent);
        Assert.Same(group, external.Parent);
    }

    [Fact]
    public void Ungroup_IdentityRoot_RestoresChildrenAtGroupIndexAndSelectsThem()
    {
        var service = new PrimitiveService();
        var first = Rect("A", 0f, 0f);
        var second = Rect("B", 30f, 0f);
        var third = Rect("C", 60f, 0f);
        service.ReplaceAll(new Primitive[] { first, second, third });
        service.SetSelectedRange(new Primitive[] { first, second });
        var group = service.GroupSelection(out _)!;
        var changes = new List<DocumentChange>();
        service.Changed += changes.Add;

        Assert.True(service.TryUngroup(group, out var error));

        Assert.Null(error);
        Assert.Equal(new Primitive[] { first, second, third }, service.GetAll());
        Assert.Null(first.Parent);
        Assert.Null(second.Parent);
        Assert.Empty(group.Children);
        Assert.Equal(new Primitive[] { first, second }, service.GetSelection());
        Assert.DoesNotContain(group, service.GetAll());

        var change = Assert.Single(changes);
        Assert.True(change.Kind.HasFlag(DocumentChangeKind.Structure));
        Assert.True(change.Kind.HasFlag(DocumentChangeKind.Selection));

        service.Undo();
        Assert.Equal(new Primitive[] { group, third }, service.GetAll());
        Assert.Equal(new Primitive[] { first, second }, group.Children);
        Assert.Same(group, first.Parent);
        Assert.Same(group, second.Parent);
        Assert.Equal(new Primitive[] { first, second }, service.GetSelection());

        service.Redo();
        Assert.Equal(new Primitive[] { first, second, third }, service.GetAll());
        Assert.Null(first.Parent);
        Assert.Null(second.Parent);
        Assert.Equal(new Primitive[] { first, second }, service.GetSelection());
    }

    [Fact]
    public void Ungroup_NonIdentityOrNestedContainers_AreRejectedWithoutChangingDocument()
    {
        var cases = new (string Name, Container Container)[]
        {
            ("offset", Container.CreateOffset("Offset", 20f, 20f)),
            ("rotated", Container.CreateRotation("Rotated", 45f)),
            ("flipped", Container.CreateFlip("Flipped", flipX: true, flipY: false)),
        };

        foreach (var (name, container) in cases)
        {
            var service = new PrimitiveService();
            var child = Rect($"{name} child", 10f, 10f);
            container.AddChild(child);
            service.ReplaceAll(new Primitive[] { container });
            service.SetSelected(container);

            Assert.False(service.TryUngroup(container, out var error), name);
            Assert.Contains("identity", error, StringComparison.OrdinalIgnoreCase);
            Assert.Same(container, Assert.Single(service.GetAll()));
            Assert.Same(container, Assert.Single(service.GetSelection()));
            Assert.Same(container, child.Parent);
            Assert.False(service.CanUndo);
        }

        var nestedService = new PrimitiveService();
        var outer = Container.CreateOffset("Outer", 0f, 0f);
        var inner = Container.CreateOffset("Inner", 0f, 0f);
        var nestedChild = Rect("Nested child", 10f, 10f);
        inner.AddChild(nestedChild);
        outer.AddChild(inner);
        nestedService.ReplaceAll(new Primitive[] { outer });
        nestedService.SetSelected(inner);

        Assert.False(nestedService.TryUngroup(inner, out var nestedError));
        Assert.Contains("root", nestedError, StringComparison.OrdinalIgnoreCase);
        Assert.Same(outer, Assert.Single(nestedService.GetAll()));
        Assert.Same(outer, inner.Parent);
        Assert.False(nestedService.CanUndo);
    }

    [Fact]
    public void Ungroup_IdentityRootWithNestedNonIdentityChild_PreservesWorldBounds()
    {
        var service = new PrimitiveService();
        var nested = Container.CreateRotation("Rotated Child", 45f);
        var leaf = Rect("Leaf", 5f, 5f, 20f, 10f);
        nested.AddChild(leaf);
        var other = Rect("Other", 100f, 0f);
        service.ReplaceAll(new Primitive[] { nested, other });
        service.SetSelectedRange(new Primitive[] { nested, other });
        var group = service.GroupSelection(out _)!;
        var leafBefore = leaf.GetWorldBoundingBox();
        var nestedBefore = nested.GetWorldBoundingBox();

        Assert.True(service.TryUngroup(group, out var error));

        Assert.Null(error);
        Assert.Equal(new Primitive[] { nested, other }, service.GetAll());
        Assert.Null(nested.Parent);
        Assert.Same(nested, leaf.Parent);
        Assert.Equal(leafBefore, leaf.GetWorldBoundingBox());
        Assert.Equal(nestedBefore, nested.GetWorldBoundingBox());
        Assert.Equal(new Primitive[] { nested, other }, service.GetSelection());
    }

    [Fact]
    public void Ungroup_EmptyIdentityRoot_RemovesContainerAndIsUndoable()
    {
        var service = new PrimitiveService();
        var group = Container.CreateOffset("Group", 0f, 0f);
        var other = Rect("Other", 10f, 10f);
        service.ReplaceAll(new Primitive[] { group, other });
        service.SetSelected(group);

        Assert.True(service.TryUngroup(group, out var error));

        Assert.Null(error);
        Assert.Same(other, Assert.Single(service.GetAll()));
        Assert.Empty(service.GetSelection());

        service.Undo();
        Assert.Equal(new Primitive[] { group, other }, service.GetAll());
    }

    [Fact]
    public void Group_RoundTripsThroughSerializer()
    {
        var service = new PrimitiveService();
        var first = Rect("A", 0f, 0f);
        var second = Rect("B", 30f, 0f);
        var third = Rect("C", 60f, 0f);
        service.ReplaceAll(new Primitive[] { first, second, third });
        service.SetSelectedRange(new Primitive[] { first, second });
        var group = service.GroupSelection(out _)!;
        var firstBefore = first.GetWorldBoundingBox();
        var secondBefore = second.GetWorldBoundingBox();

        var json = DocumentSerializer.Serialize(service.GetAll(), service.GetSelection());
        var result = DocumentSerializer.Deserialize(json);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Roots.Count);
        var restoredGroup = Assert.IsType<Container>(result.Roots[0]);
        Assert.Equal(ContainerKind.Offset, restoredGroup.Kind);
        Assert.Equal(0f, restoredGroup.OffsetX.Value);
        Assert.Equal(0f, restoredGroup.OffsetY.Value);
        Assert.Equal(new[] { "A", "B" }, restoredGroup.Children.Select(child => child.Name.Value));
        Assert.Same(restoredGroup, Assert.Single(result.ResolveSelection()));
        Assert.Equal(firstBefore, restoredGroup.Children[0].GetWorldBoundingBox());
        Assert.Equal(secondBefore, restoredGroup.Children[1].GetWorldBoundingBox());
        Assert.Same(group, service.GetSelection()[0]);
    }
}
