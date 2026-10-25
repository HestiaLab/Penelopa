using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class SelectionNormalizationTests
{
    private static Rectangle Rect(float x, float y, float w = 10f, float h = 10f)
        => new Rectangle { PosX = { Value = x }, PosY = { Value = y }, Width = { Value = w }, Height = { Value = h } };

    private static (PrimitiveService Service, Container Container, Rectangle Child, Rectangle External) CreateDocument()
    {
        var service = new PrimitiveService();
        var container = Container.CreateOffset("c", 20f, 20f);
        var child = Rect(10f, 10f);
        container.AddChild(child);
        var external = Rect(100f, 0f);
        service.Add(container);
        service.Add(external);
        return (service, container, child, external);
    }

    [Fact]
    public void SetSelectedRange_ParentAndChild_KeepsAncestorOnly()
    {
        var (service, container, child, _) = CreateDocument();

        service.SetSelectedRange(new Primitive[] { container, child });

        var selection = service.GetSelection().ToList();
        Assert.Single(selection);
        Assert.Same(container, selection[0]);
    }

    [Fact]
    public void SetSelectedRange_DescendantAndAncestorInEitherOrder_KeepsAncestorOnly()
    {
        var (service, container, child, _) = CreateDocument();

        service.SetSelectedRange(new Primitive[] { child, container });

        Assert.Same(container, Assert.Single(service.GetSelection()));
    }

    [Fact]
    public void AppendSelected_ChildWhenParentSelected_KeepsParentOnly()
    {
        var (service, container, child, _) = CreateDocument();
        service.SetSelected(container);

        service.AppendSelected(child);

        Assert.Same(container, Assert.Single(service.GetSelection()));
    }

    [Fact]
    public void AppendSelected_ParentWhenChildSelected_ReplacesChildWithParent()
    {
        var (service, container, child, _) = CreateDocument();
        service.SetSelected(child);

        service.AppendSelected(container);

        Assert.Same(container, Assert.Single(service.GetSelection()));
    }

    [Fact]
    public void CtrlClick_OnChildWhenChildSelected_KeepsOnlyOutermostCandidate()
    {
        var (service, container, child, _) = CreateDocument();
        service.SetSelected(child);
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);
        var hit = new HitTestResult { Primitive = child, Candidates = new Primitive[] { container, child } };

        controller.PointerDown(new Point(35f, 35f), new Point(0f, 0f), hit, ctrl: true);

        Assert.Same(container, Assert.Single(service.GetSelection()));
        Assert.Equal(ControllerState.Pressed, controller.State);
    }

    [Fact]
    public void Drag_TopLevelMultiSelection_MovesChildOnlyThroughItsContainer()
    {
        var (service, container, child, external) = CreateDocument();
        service.SetSelected(container);
        service.AppendSelected(child); // 规范化后仍是容器
        service.AppendSelected(external);
        Assert.Equal(2, service.GetSelection().Count());

        var originalChildLocalX = child.PosX.Value;
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);

        // 点击已选中的 external，组拖拽应只移动两个顶层项一次
        controller.PointerDown(new Point(105f, 5f), new Point(0f, 0f), new HitTestResult { Primitive = external }, ctrl: false);
        controller.PointerMove(new Point(145f, 5f), new Point(0f, 0f));
        controller.PointerUp(new Point(145f, 5f));

        Assert.Equal(60f, container.OffsetX.Value);           // 20 + 40
        Assert.Equal(140f, external.PosX.Value);              // 100 + 40
        Assert.Equal(originalChildLocalX, child.PosX.Value);  // 子项局部坐标不变
        Assert.Equal(70f, child.GetWorldBoundingBox().MinX);  // 世界只移动一次：30 + 40
    }

    [Fact]
    public void Align_WithParentChildSelection_MovesEachTopLevelOnceOnly()
    {
        var (service, container, child, external) = CreateDocument();
        service.SetSelected(container);
        service.AppendSelected(child);
        service.AppendSelected(external);

        Assert.True(service.Align(AlignType.Left));

        Assert.Equal(20f, container.OffsetX.Value);
        Assert.Equal(10f, child.PosX.Value);      // 子项没有被单独对齐
        Assert.Equal(30f, external.PosX.Value);   // 外部队列到容器世界左边
        Assert.Equal(30f, child.GetWorldBoundingBox().MinX);
    }

    [Fact]
    public void Align_WithParentChildSelection_UndoRestoresTopLevelOnly()
    {
        var (service, container, child, external) = CreateDocument();
        service.SetSelected(container);
        service.AppendSelected(child);
        service.AppendSelected(external);
        service.Align(AlignType.Left);

        service.Undo();

        Assert.Equal(20f, container.OffsetX.Value);
        Assert.Equal(10f, child.PosX.Value);
        Assert.Equal(100f, external.PosX.Value);
        Assert.Contains(container, service.GetAll());
        Assert.Contains(external, service.GetAll());
    }
}
