using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class SetBoundsCoordinateSpaceTests
{
    public enum ContainerCase
    {
        Root,
        Offset,
        Flip,
        Nested,
        Rotated,
    }

    public enum ShapeKind
    {
        Circle,
        Rectangle,
        Triangle,
    }

    public static IEnumerable<object[]> AxisAlignedCases() => Cases(includeRotated: false);

    public static IEnumerable<object[]> AllCases() => Cases(includeRotated: true);

    public static IEnumerable<object[]> RotatedCases()
    {
        foreach (ShapeKind shapeKind in Enum.GetValues<ShapeKind>())
        {
            yield return new object[] { shapeKind };
        }
    }

    private static IEnumerable<object[]> Cases(bool includeRotated)
    {
        foreach (ContainerCase containerCase in Enum.GetValues<ContainerCase>())
        {
            if (!includeRotated && containerCase == ContainerCase.Rotated)
            {
                continue;
            }

            foreach (ShapeKind shapeKind in Enum.GetValues<ShapeKind>())
            {
                yield return new object[] { containerCase, shapeKind };
            }
        }
    }

    [Theory]
    [MemberData(nameof(AxisAlignedCases))]
    public void SetBounds_FitsRequestedWorldBox(ContainerCase containerCase, ShapeKind shapeKind)
    {
        var (leaf, _) = BuildDocument(containerCase, shapeKind);
        var target = new Box(100f, 40f, 140f, 80f);
        var anchor = ResizeMath.FixedCorner(leaf.GetWorldBoundingBox(), ResizeHandle.BottomRight);

        var actual = leaf.SetBounds(target, anchor);

        AssertBox(target, actual);
        AssertBox(target, leaf.GetWorldBoundingBox());

        // 后续选择框与命中都使用同一个世界包围盒
        Assert.True(leaf.ContainsWorldPoint(target.Center));
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void CanResizeInWorld_OnlyReflectsAxisAlignedAncestors(ContainerCase containerCase, ShapeKind shapeKind)
    {
        var (leaf, _) = BuildDocument(containerCase, shapeKind);

        Assert.Equal(containerCase != ContainerCase.Rotated, leaf.CanResizeInWorld);
    }

    [Theory]
    [MemberData(nameof(RotatedCases))]
    public void SetBounds_OnRotatedParent_StaysLocalAndFinite(ShapeKind shapeKind)
    {
        var (leaf, _) = BuildDocument(ContainerCase.Rotated, shapeKind);
        var target = new Box(100f, 40f, 140f, 80f);

        var actual = leaf.SetBounds(target, new Point(100f, 40f));

        Assert.True(float.IsFinite(actual.MinX));
        Assert.True(float.IsFinite(actual.MinY));
        Assert.True(float.IsFinite(actual.MaxX));
        Assert.True(float.IsFinite(actual.MaxY));
        Assert.False(leaf.CanResizeInWorld);
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void CancelResize_RestoresEveryProperty(ContainerCase containerCase, ShapeKind shapeKind)
    {
        var (leaf, root) = BuildDocument(containerCase, shapeKind);
        var originalLeafProps = SnapshotProps(leaf);
        var originalRootProps = SnapshotProps(root);
        var originalBounds = leaf.GetWorldBoundingBox();
        var service = new PrimitiveService();
        service.Add(root);
        service.SetSelected(leaf);
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);
        var hit = new HitTestResult { Handle = ResizeHandle.BottomRight, Primitive = leaf };

        controller.PointerDown(
            new Point(originalBounds.MaxX, originalBounds.MinY),
            new Point(0f, 0f),
            hit,
            ctrl: false);
        controller.PointerMove(
            new Point(originalBounds.MaxX + 30f, originalBounds.MinY - 20f),
            new Point(0f, 0f));
        controller.Cancel();

        AssertProps(originalLeafProps, leaf);
        AssertProps(originalRootProps, root);
        Assert.Equal(1, host.MutationCancels);
        Assert.Contains(leaf, host.Notified);

        // 取消不应留下撤销条目：一次 Undo 应回退 Add 本身
        service.Undo();
        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void TriangleChildOfOffsetContainer_FitsRequestedWorldBox()
    {
        var container = Container.CreateOffset("c", 20f, 20f);
        var triangle = new Triangle();
        triangle.Vertex1X.Value = 50f; triangle.Vertex1Y.Value = 50f;
        triangle.Vertex2X.Value = 70f; triangle.Vertex2Y.Value = 50f;
        triangle.Vertex3X.Value = 60f; triangle.Vertex3Y.Value = 60f;
        container.AddChild(triangle);

        // 审查报告 6.4 的复现场景
        var actual = triangle.SetBounds(new Box(70f, 70f, 120f, 100f), new Point(70f, 70f));

        AssertBox(new Box(70f, 70f, 120f, 100f), actual);
        AssertBox(new Box(70f, 70f, 120f, 100f), triangle.GetWorldBoundingBox());
        Assert.True(triangle.ContainsWorldPoint(new Point(90f, 80f)));
        Assert.False(triangle.ContainsWorldPoint(new Point(70f, 100f)));
    }

    [Fact]
    public void CircleChildOfOffsetContainer_ReturnsWorldBoundingBox()
    {
        var container = Container.CreateOffset("c", 20f, 20f);
        var circle = new Circle { CenterX = { Value = 10f }, CenterY = { Value = 10f }, Radius = { Value = 5f } };
        container.AddChild(circle);

        var returned = circle.SetBounds(new Box(20f, 20f, 40f, 40f), new Point(20f, 30f));

        AssertBox(new Box(20f, 20f, 40f, 40f), returned);
        AssertBox(returned, circle.GetWorldBoundingBox());
    }

    [Fact]
    public void RectangleChildOfNestedContainers_ReturnsWorldBoundingBox()
    {
        var outer = Container.CreateOffset("outer", 30f, -10f);
        var inner = Container.CreateFlip("inner", flipX: true, flipY: false);
        outer.AddChild(inner);
        var rectangle = new Rectangle
        {
            PosX = { Value = 10f },
            PosY = { Value = 10f },
            Width = { Value = 10f },
            Height = { Value = 10f },
        };
        inner.AddChild(rectangle);
        var target = new Box(-40f, 20f, 0f, 60f);

        var returned = rectangle.SetBounds(target, new Point(0f, 20f));

        AssertBox(target, returned);
        AssertBox(target, rectangle.GetWorldBoundingBox());
    }

    [Fact]
    public void CancelResize_TriangleChildOfOffsetContainer_RestoresEveryProperty()
    {
        var container = Container.CreateOffset("c", 20f, 20f);
        var triangle = new Triangle();
        triangle.Vertex1X.Value = 50f; triangle.Vertex1Y.Value = 50f;
        triangle.Vertex2X.Value = 70f; triangle.Vertex2Y.Value = 50f;
        triangle.Vertex3X.Value = 60f; triangle.Vertex3Y.Value = 60f;
        container.AddChild(triangle);

        var originalProps = SnapshotProps(triangle);
        var originalBounds = triangle.GetWorldBoundingBox();
        var service = new PrimitiveService();
        service.Add(container);
        service.SetSelected(triangle);
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);
        var hit = new HitTestResult { Handle = ResizeHandle.BottomRight, Primitive = triangle };

        controller.PointerDown(new Point(originalBounds.MaxX, originalBounds.MinY), new Point(0f, 0f), hit, ctrl: false);
        controller.PointerMove(new Point(120f, 100f), new Point(0f, 0f));
        Assert.NotEqual(originalBounds, triangle.GetWorldBoundingBox());

        controller.Cancel();

        AssertProps(originalProps, triangle);
        Assert.Same(container, triangle.Parent);
        Assert.Single(container.Children);
        Assert.Same(triangle, container.Children[0]);
        Assert.Equal(1, host.MutationCancels);
        Assert.Contains(triangle, host.Notified);
    }

    [Fact]
    public void CancelDrag_TriangleChildOfRotatedContainer_RestoresEveryProperty()
    {
        var container = Container.CreateRotation("r", 45f);
        var triangle = new Triangle();
        triangle.Vertex1X.Value = 10f; triangle.Vertex1Y.Value = 10f;
        triangle.Vertex2X.Value = 30f; triangle.Vertex2Y.Value = 10f;
        triangle.Vertex3X.Value = 20f; triangle.Vertex3Y.Value = 20f;
        container.AddChild(triangle);

        var originalProps = SnapshotProps(triangle);
        var originalContainerProps = SnapshotProps(container);
        var service = new PrimitiveService();
        service.Add(container);
        service.SetSelected(triangle);
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);
        var hit = new HitTestResult { Primitive = triangle };

        controller.PointerDown(new Point(20f, 15f), new Point(0f, 0f), hit, ctrl: false);
        controller.PointerMove(new Point(40f, 35f), new Point(0f, 0f));
        controller.Cancel();

        AssertProps(originalProps, triangle);
        AssertProps(originalContainerProps, container);
    }

    private static (Primitive Leaf, Primitive Root) BuildDocument(ContainerCase containerCase, ShapeKind shapeKind)
    {
        var leaf = CreateShape(shapeKind);

        switch (containerCase)
        {
            case ContainerCase.Root:
                return (leaf, leaf);
            case ContainerCase.Offset:
                {
                    var container = Container.CreateOffset("c", 20f, 20f);
                    container.AddChild(leaf);
                    return (leaf, container);
                }

            case ContainerCase.Flip:
                {
                    var container = Container.CreateFlip("f", flipX: true, flipY: false);
                    container.AddChild(leaf);
                    return (leaf, container);
                }

            case ContainerCase.Nested:
                {
                    var outer = Container.CreateOffset("outer", 30f, -10f);
                    var inner = Container.CreateFlip("inner", flipX: true, flipY: false);
                    outer.AddChild(inner);
                    inner.AddChild(leaf);
                    return (leaf, outer);
                }

            case ContainerCase.Rotated:
                {
                    var container = Container.CreateRotation("r", 45f);
                    container.AddChild(leaf);
                    return (leaf, container);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(containerCase));
        }
    }

    private static Primitive CreateShape(ShapeKind shapeKind) => shapeKind switch
    {
        ShapeKind.Circle => new Circle { CenterX = { Value = 10f }, CenterY = { Value = 10f }, Radius = { Value = 5f } },
        ShapeKind.Rectangle => new Rectangle
        {
            PosX = { Value = 10f },
            PosY = { Value = 10f },
            Width = { Value = 10f },
            Height = { Value = 10f },
        },
        ShapeKind.Triangle => CreateTriangle(),
        _ => throw new ArgumentOutOfRangeException(nameof(shapeKind)),
    };

    private static Triangle CreateTriangle()
    {
        var triangle = new Triangle();
        triangle.Vertex1X.Value = 10f; triangle.Vertex1Y.Value = 10f;
        triangle.Vertex2X.Value = 30f; triangle.Vertex2Y.Value = 10f;
        triangle.Vertex3X.Value = 20f; triangle.Vertex3Y.Value = 20f;
        return triangle;
    }

    private static Dictionary<string, float> SnapshotProps(Primitive primitive)
        => primitive.Props.OfType<FloatPropValue>().ToDictionary(p => p.Name, p => p.Value);

    private static void AssertProps(Dictionary<string, float> expected, Primitive actual)
    {
        foreach (var (name, value) in expected)
        {
            var prop = Assert.IsType<FloatPropValue>(actual.Props.Single(p => p.Name == name));
            Assert.Equal(value, prop.Value);
        }
    }

    private static void AssertBox(Box expected, Box actual)
    {
        Assert.Equal(expected.MinX, actual.MinX, 3);
        Assert.Equal(expected.MinY, actual.MinY, 3);
        Assert.Equal(expected.MaxX, actual.MaxX, 3);
        Assert.Equal(expected.MaxY, actual.MaxY, 3);
    }
}
