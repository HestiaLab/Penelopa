using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class EncapsulationTests
{
    [Fact]
    public void GetAll_ReturnsSnapshotIndependentOfLaterChanges()
    {
        var service = new PrimitiveService();
        var first = new Rectangle();
        var second = new Circle();
        service.Add(first);

        var snapshot = service.GetAll();
        service.Add(second);

        Assert.Single(snapshot);
        Assert.Same(first, snapshot[0]);
        Assert.Equal(2, service.GetAll().Count);
    }

    [Fact]
    public void GetAll_ReturnedCollectionIsReadOnly()
    {
        var service = new PrimitiveService();
        service.Add(new Rectangle());

        var collection = Assert.IsAssignableFrom<IList<Primitive>>(service.GetAll());

        Assert.Throws<NotSupportedException>(() => collection.Add(new Circle()));
    }

    [Fact]
    public void GetSelection_ReturnsSnapshotIndependentOfLaterChanges()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        var rectangle = new Rectangle();
        service.Add(circle);
        service.Add(rectangle);
        service.SetSelected(circle);

        var snapshot = service.GetSelection();
        service.SetSelected(rectangle);

        Assert.Same(circle, Assert.Single(snapshot));
        Assert.Same(rectangle, Assert.Single(service.GetSelection()));
    }

    [Fact]
    public void GetSelection_ReturnedCollectionIsReadOnly()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.SetSelected(circle);

        var collection = Assert.IsAssignableFrom<IList<Primitive>>(service.GetSelection());

        Assert.Throws<NotSupportedException>(() => collection.Add(new Rectangle()));
    }

    [Fact]
    public void Props_ReturnedCollectionIsReadOnly()
    {
        var circle = new Circle();

        var collection = Assert.IsAssignableFrom<IList<PropValue>>(circle.Props);

        Assert.Throws<NotSupportedException>(() => collection.Add(new IntPropValue("extra", 1)));
    }

    [Fact]
    public void ContainerChildren_ReturnedCollectionIsReadOnly()
    {
        var container = Container.CreateOffset("c", 0f, 0f);
        container.AddChild(new Rectangle());

        var collection = Assert.IsAssignableFrom<IList<Primitive>>(container.Children);

        Assert.Throws<NotSupportedException>(() => collection.Add(new Circle()));
    }

    [Fact]
    public void AddToContainer_RejectsSelf()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 0f, 0f);
        service.Add(container);

        Assert.Throws<ArgumentException>(() => service.AddToContainer(container, container));
    }

    [Fact]
    public void AddToContainer_RejectsPrimitiveFromAnotherDocument()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 0f, 0f);
        service.Add(container);
        var otherService = new PrimitiveService();
        var foreign = new Rectangle();
        otherService.Add(foreign);

        Assert.Throws<InvalidOperationException>(() => service.AddToContainer(container, foreign));
        Assert.Empty(container.Children);
        Assert.Contains(foreign, otherService.GetAll());
    }

    [Fact]
    public void AddToContainer_AcceptsNewUnattachedChild()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 0f, 0f);
        service.Add(container);
        var child = new Rectangle();

        service.AddToContainer(container, child);

        Assert.Same(container, child.Parent);
        Assert.Same(child, Assert.Single(container.Children));
    }

    [Fact]
    public void AddToContainer_RejectsContainerOutsideDocument()
    {
        var service = new PrimitiveService();
        var foreignContainer = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle();
        service.Add(child);

        Assert.Throws<InvalidOperationException>(() => service.AddToContainer(foreignContainer, child));
        Assert.Contains(child, service.GetAll());
    }

    [Fact]
    public void AddToContainer_RejectsChildThatAlreadyHasAParent()
    {
        var service = new PrimitiveService();
        var first = Container.CreateOffset("first", 0f, 0f);
        var second = Container.CreateOffset("second", 0f, 0f);
        var child = new Rectangle();
        service.Add(first);
        service.Add(second);
        service.AddToContainer(first, child);

        Assert.Throws<InvalidOperationException>(() => service.AddToContainer(second, child));
        Assert.Same(first, child.Parent);
        Assert.Single(first.Children);
        Assert.Empty(second.Children);
    }

    [Fact]
    public void AddToContainer_RejectsMovingAnAncestorIntoItsDescendant()
    {
        var service = new PrimitiveService();
        var outer = Container.CreateOffset("outer", 0f, 0f);
        var inner = Container.CreateOffset("inner", 0f, 0f);
        outer.AddChild(inner);
        service.Add(outer);

        Assert.Throws<InvalidOperationException>(() => service.AddToContainer(inner, outer));
        Assert.Contains(outer, service.GetAll());
        Assert.Same(outer, inner.Parent);
    }

    [Fact]
    public void AddToContainer_MovesRootChildIntoContainer()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle();
        service.Add(container);
        service.Add(child);

        service.AddToContainer(container, child);

        Assert.DoesNotContain(child, service.GetAll());
        Assert.Same(container, child.Parent);
        Assert.Same(child, Assert.Single(container.Children));
    }

    [Fact]
    public void UnknownPropValue_GetBoxedValueThrowsDiagnosticError()
    {
        var primitive = new CustomPrimitive();

        var error = Assert.Throws<NotSupportedException>(() => primitive.Custom.GetBoxedValue());

        Assert.Contains(nameof(CustomPropValue), error.Message);
    }

    [Fact]
    public void UnknownPropValue_SetBoxedValueThrowsDiagnosticError()
    {
        var primitive = new CustomPrimitive();

        var error = Assert.Throws<NotSupportedException>(() => primitive.Custom.SetBoxedValue(1));

        Assert.Contains(nameof(CustomPropValue), error.Message);
    }

    [Fact]
    public void UnknownPropValue_SerializerThrowsInsteadOfSilentlyDroppingIt()
    {
        var primitive = new CustomPrimitive();

        var error = Assert.Throws<NotSupportedException>(
            () => DocumentSerializer.Serialize(new Primitive[] { primitive }, Array.Empty<Primitive>()));

        Assert.Contains(nameof(CustomPropValue), error.Message);
    }

    [Fact]
    public void UnknownPropValue_HistoryCaptureThrowsInsteadOfSilentlyDroppingIt()
    {
        var service = new PrimitiveService();
        var primitive = new CustomPrimitive();

        Assert.Throws<NotSupportedException>(() => service.Add(primitive));
        Assert.Empty(service.GetAll());
    }

    private sealed class CustomPrimitive : Primitive
    {
        public CustomPrimitive() : base("Custom")
        {
            Custom = new CustomPropValue("Custom");
            AddProp(Custom);
        }

        public CustomPropValue Custom { get; }

        protected override Box GetLocalBoundingBox() => new(0f, 0f, 1f, 1f);

        protected override void TranslateLocal(float deltaX, float deltaY)
        {
        }

        protected override Box SetBoundsLocal(Box bounds, Point anchor) => bounds;

        protected override bool ContainsLocalPoint(Point point) => false;
    }

    private sealed class CustomPropValue : PropValue
    {
        public CustomPropValue(string name) : base(name)
        {
        }
    }
}
