using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class ServiceMutationTests
{
    [Fact]
    public void RemoveRange_RemovesAllItemsWithOneUndoEntry()
    {
        var service = new PrimitiveService();
        var first = new Circle();
        var second = new Rectangle();
        var third = new Triangle();
        service.Add(first);
        service.Add(second);
        service.Add(third);

        service.RemoveRange(new Primitive[] { first, third });

        Assert.Same(second, Assert.Single(service.GetAll()));
        service.Undo();
        Assert.Equal(3, service.GetAll().Count);
        Assert.Contains(first, service.GetAll());
        Assert.Contains(third, service.GetAll());
    }

    [Fact]
    public void RemoveRange_ClearsSelectionIncludingDescendantsOfRemovedContainers()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Circle();
        container.AddChild(child);
        var external = new Rectangle();
        service.Add(container);
        service.Add(external);
        service.SetSelected(child);
        service.AppendSelected(external);

        service.RemoveRange(new Primitive[] { container });

        Assert.Same(external, Assert.Single(service.GetSelection()));
        Assert.DoesNotContain(container, service.GetAll());
    }

    [Fact]
    public void RemoveRange_IgnoresPrimitivesThatAreNotInTheDocument()
    {
        var service = new PrimitiveService();
        var present = new Circle();
        service.ReplaceAll(new Primitive[] { present });

        service.RemoveRange(new Primitive[] { new Rectangle() });

        Assert.Same(present, Assert.Single(service.GetAll()));
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void MoveSelection_TranslatesTopLevelSelectionOnceAndUndoes()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 20f, 20f);
        var child = new Rectangle { PosX = { Value = 10f }, PosY = { Value = 10f } };
        container.AddChild(child);
        var external = new Rectangle { PosX = { Value = 100f }, PosY = { Value = 0f } };
        service.Add(container);
        service.Add(external);
        service.SetSelected(container);
        service.AppendSelected(external);

        Assert.True(service.MoveSelection(5f, -3f));

        Assert.Equal(25f, container.OffsetX.Value);
        Assert.Equal(17f, container.OffsetY.Value);
        Assert.Equal(105f, external.PosX.Value);
        Assert.Equal(-3f, external.PosY.Value);
        Assert.Equal(10f, child.PosX.Value);

        service.Undo();
        Assert.Equal(20f, container.OffsetX.Value);
        Assert.Equal(100f, external.PosX.Value);
    }

    [Fact]
    public void MoveSelection_WithoutSelectionDoesNothing()
    {
        var service = new PrimitiveService();
        service.ReplaceAll(new Primitive[] { new Circle() });

        Assert.False(service.MoveSelection(1f, 0f));
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void TrySetProperty_MultipleOwners_WritesAllWithOneUndoEntry()
    {
        var service = new PrimitiveService();
        var first = new Circle();
        var second = new Circle();
        service.Add(first);
        service.Add(second);
        service.SetSelectedRange(new Primitive[] { first, second });

        Assert.True(service.TrySetProperty(new Primitive[] { first, second }, "Radius", "12", out var error));

        Assert.Null(error);
        Assert.Equal(12f, first.Radius.Value);
        Assert.Equal(12f, second.Radius.Value);

        service.Undo();
        Assert.Equal(5f, first.Radius.Value);
        Assert.Equal(5f, second.Radius.Value);
    }

    [Fact]
    public void TrySetProperty_MultipleOwners_InvalidValueRollsBackEveryOwner()
    {
        var service = new PrimitiveService();
        var first = new Circle();
        var second = new Circle();
        service.ReplaceAll(new Primitive[] { first, second });

        Assert.False(service.TrySetProperty(new Primitive[] { first, second }, "Radius", "-5", out var error));

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(5f, first.Radius.Value);
        Assert.Equal(5f, second.Radius.Value);
        Assert.False(service.CanUndo);
    }

    [Fact]
    public void TrySetProperty_MultipleOwners_MissingPropertyIsRejected()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        var container = Container.CreateOffset("c", 0f, 0f);
        service.Add(circle);
        service.Add(container);

        Assert.False(service.TrySetProperty(new Primitive[] { circle, container }, "Radius", "10", out var error));

        Assert.Contains("Radius", error);
        Assert.Equal(5f, circle.Radius.Value);
    }

    [Fact]
    public void TrySetProperty_MultipleOwners_DifferentValueTypesAreRejected()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);
        var custom = new CustomOwner();
        service.Add(custom);

        Assert.False(service.TrySetProperty(
            new Primitive[] { circle, custom },
            "Color",
            "1",
            out var error));

        Assert.Contains("different", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0xFFFFFFFFu, circle.Color.Value);
    }

    [Fact]
    public void TrySetProperty_MultipleOwners_UnchangedValueDoesNotCreateHistory()
    {
        var service = new PrimitiveService();
        var first = new Circle();
        var second = new Circle();
        service.ReplaceAll(new Primitive[] { first, second });

        Assert.True(service.TrySetProperty(new Primitive[] { first, second }, "Radius", "5", out var error));

        Assert.Null(error);
        Assert.False(service.CanUndo);
    }

    private sealed class CustomOwner : Primitive
    {
        public CustomOwner() : base("Custom")
        {
            Color = new IntPropValue("Color", 7);
            AddProp(Color);
        }

        public IntPropValue Color { get; }

        protected override Penelopa.Core.Alignment.Box GetLocalBoundingBox()
            => new(0f, 0f, 1f, 1f);

        protected override void TranslateLocal(float deltaX, float deltaY)
        {
        }

        protected override Penelopa.Core.Alignment.Box SetBoundsLocal(
            Penelopa.Core.Alignment.Box bounds,
            Penelopa.Core.Alignment.Point anchor) => bounds;

        protected override bool ContainsLocalPoint(Penelopa.Core.Alignment.Point point) => false;
    }
}
