using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class EditorInteractionControllerTests
{
    private static Rectangle Rect(float x, float y, float w = 10f, float h = 10f)
        => new Rectangle { PosX = { Value = x }, PosY = { Value = y }, Width = { Value = w }, Height = { Value = h } };

    [Fact]
    public void PointerDown_OnUnselectedPrimitive_SelectsItAndStaysPressed()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var rect = Rect(0f, 0f);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = rect }, ctrl: false);

        Assert.Equal(ControllerState.Pressed, controller.State);
        Assert.Same(rect, host.Selected);
    }

    [Fact]
    public void PointerDown_OnEmpty_clearsSelection()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        host.Select(Rect(0f, 0f));

        controller.PointerDown(new Point(50f, 50f), new Point(50f, 50f), default, ctrl: false);

        // Pressing empty space enters Pressed (a pan candidate) and clears
        // the selection immediately; a plain click commits as a click.
        Assert.Equal(ControllerState.Pressed, controller.State);
        Assert.True(host.Cleared);
    }

    [Fact]
    public void EmptySpaceDrag_PansView()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);

        controller.PointerDown(new Point(50f, 50f), new Point(50f, 50f), default, ctrl: false);
        controller.PointerMove(new Point(80f, 50f), new Point(80f, 50f));   // crosses the threshold
        controller.PointerMove(new Point(100f, 60f), new Point(100f, 60f));  // incremental pan
        controller.PointerUp(new Point(100f, 60f));

        Assert.Equal(ControllerState.Idle, controller.State);
        Assert.Empty(host.Notified); // panning does not commit geometry
        Assert.Contains(new Point(30f, 0f), host.PanCalls);
        Assert.Contains(new Point(20f, 10f), host.PanCalls);
    }

    [Fact]
    public void EmptySpaceClick_DoesNotPan()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);

        controller.PointerDown(new Point(50f, 50f), new Point(50f, 50f), default, ctrl: false);
        controller.PointerUp(new Point(50f, 50f));

        Assert.Empty(host.PanCalls);
        Assert.True(host.Cleared);
    }

    [Fact]
    public void Press_ThenMoveBeyondThreshold_DragsAndCommitsOnUp()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var rect = Rect(0f, 0f);
        host.Select(rect);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = rect }, ctrl: false);
        controller.PointerMove(new Point(20f, 5f), new Point(20f, 5f)); // beyond threshold
        Assert.Equal(ControllerState.Dragging, controller.State);
        Assert.Equal(15f, rect.PosX.Value); // translated by 15

        controller.PointerMove(new Point(30f, 10f), new Point(30f, 10f));
        Assert.Equal(25f, rect.PosX.Value);
        Assert.Equal(5f, rect.PosY.Value);

        controller.PointerUp(new Point(30f, 10f));

        Assert.Equal(ControllerState.Idle, controller.State);
        Assert.Contains(rect, host.Notified);
    }

    [Fact]
    public void Click_WithinThreshold_DoesNotMoveOrCommit()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var rect = Rect(0f, 0f);
        host.Select(rect);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = rect }, ctrl: false);
        controller.PointerMove(new Point(6f, 6f), new Point(6f, 6f)); // within threshold
        controller.PointerUp(new Point(6f, 6f));

        Assert.Equal(0f, rect.PosX.Value);
        Assert.Empty(host.Notified);
    }

    [Fact]
    public void PointerDown_OnHandle_ResizesAndCommitsOnUp()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var rect = Rect(0f, 0f);
        host.Select(rect);

        // BottomRight handle of rect (0,0,10,10) in world Y-up: (MaxX, MinY) = (10,0).
        controller.PointerDown(
            new Point(10f, 0f),
            new Point(10f, 0f),
            new HitTestResult { Handle = ResizeHandle.BottomRight, Primitive = rect },
            ctrl: false);
        Assert.Equal(ControllerState.Resizing, controller.State);

        controller.PointerMove(new Point(30f, 0f), new Point(30f, 0f));
        controller.PointerUp(new Point(30f, 0f));

        // Fixed corner (0,10); pointer (30,0) → box (0,0,30,10).
        Assert.Equal(30f, rect.Width.Value);
        Assert.Contains(rect, host.Notified);
    }

    [Fact]
    public void Cancel_AfterDrag_RestoresOriginalPosition()
    {
        var service = new PrimitiveService();
        var rect = Rect(0f, 0f);
        service.Add(rect);
        service.SetSelected(rect);
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = rect }, ctrl: false);
        controller.PointerMove(new Point(30f, 5f), new Point(30f, 5f));
        controller.PointerMove(new Point(40f, 5f), new Point(40f, 5f));
        controller.Cancel();

        Assert.Equal(0f, rect.PosX.Value);
        Assert.Equal(ControllerState.Idle, controller.State);
        Assert.Equal(1, host.MutationCancels);
        Assert.Contains(rect, host.Notified);
    }

    [Fact]
    public void Cancel_AfterResize_RestoresOriginalBounds()
    {
        var service = new PrimitiveService();
        var rect = Rect(0f, 0f);
        service.Add(rect);
        service.SetSelected(rect);
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);

        controller.PointerDown(
            new Point(10f, 0f),
            new Point(10f, 0f),
            new HitTestResult { Handle = ResizeHandle.BottomRight, Primitive = rect },
            ctrl: false);
        controller.PointerMove(new Point(50f, 0f), new Point(50f, 0f));
        controller.Cancel();

        Assert.Equal(10f, rect.Width.Value);
        Assert.Equal(10f, rect.Height.Value);
        Assert.Equal(1, host.MutationCancels);
    }

    [Fact]
    public void ResizeCircle_KeepsFixedCornerOnTheCircle()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var circle = new Circle { CenterX = { Value = 10f }, CenterY = { Value = 10f }, Radius = { Value = 5f } };
        host.Select(circle);

        // BottomRight handle of bbox (5,5,15,15): world corner (MaxX, MinY) = (15,5).
        controller.PointerDown(
            new Point(15f, 5f),
            new Point(15f, 5f),
            new HitTestResult { Handle = ResizeHandle.BottomRight, Primitive = circle },
            ctrl: false);
        controller.PointerMove(new Point(35f, -5f), new Point(35f, -5f)); // target (5,-5,35,15): w=30, h=20 → r=10
        controller.PointerUp(new Point(35f, -5f));

        // Fixed corner (5,15) stays on the circle; the circle grew to r=10.
        Assert.Equal(10f, circle.Radius.Value);
        Assert.Equal(new Box(5f, -5f, 25f, 15f), circle.GetWorldBoundingBox());
        Assert.Contains(circle, host.Notified);
    }

    [Fact]
    public void CtrlClick_OnSelectedMember_TogglesItOff()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var a = Rect(0f, 0f);
        var b = Rect(20f, 0f);
        host.SelectRange(a, b);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = a }, ctrl: true);

        Assert.Equal(ControllerState.Idle, controller.State);
        Assert.Contains(b, host.Selection);
        Assert.DoesNotContain(a, host.Selection);
    }

    [Fact]
    public void CtrlClick_InsideContainer_AppendsOutermostCandidate()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        container.AddChild(child);
        var existing = Rect(50f, 50f);
        host.SelectRange(container, existing); // container already in the multi-selection

        // Ctrl-click the child: the outermost candidate (the container) is
        // already selected → toggled off, not the child.
        var hit = new HitTestResult { Primitive = child, Candidates = new Primitive[] { container, child } };
        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: true);

        Assert.Equal(ControllerState.Idle, controller.State);
        Assert.Contains(existing, host.Selection);
        Assert.DoesNotContain(container, host.Selection);
        Assert.DoesNotContain(child, host.Selection);
    }

    [Fact]
    public void CtrlClick_InsideContainer_WhenContainerUnselected_AppendsContainer()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        container.AddChild(child);

        var hit = new HitTestResult { Primitive = child, Candidates = new Primitive[] { container, child } };
        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: true);

        Assert.Contains(container, host.Selection);
        Assert.DoesNotContain(child, host.Selection);
    }

    [Fact]
    public void PressOnSelectedMemberOfMultiSelection_CollapsesOnClick()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var a = Rect(0f, 0f);
        var b = Rect(20f, 0f);
        host.SelectRange(a, b);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = a }, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));

        Assert.Same(a, host.Selected);
    }

    [Fact]
    public void PressOnSelectedMemberOfMultiSelection_DragsGroupWhenMoved()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var a = Rect(0f, 0f);
        var b = Rect(20f, 0f);
        host.SelectRange(a, b);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = a }, ctrl: false);
        controller.PointerMove(new Point(25f, 5f), new Point(25f, 5f));  // dx = +20 from press
        controller.PointerMove(new Point(35f, 15f), new Point(35f, 15f)); // dx = +10, dy = +10
        controller.PointerUp(new Point(35f, 15f));

        Assert.Equal(30f, a.PosX.Value);
        Assert.Equal(50f, b.PosX.Value);
        Assert.Equal(10f, a.PosY.Value);
        Assert.Contains(a, host.Notified);
        Assert.Contains(b, host.Notified);
    }

    [Fact]
    public void PointerDown_InUnionBox_DragsGroup()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var a = Rect(0f, 0f);
        var b = Rect(20f, 0f);
        host.SelectRange(a, b);

        controller.PointerDown(new Point(10f, 10f), new Point(10f, 10f), new HitTestResult { InUnionBox = true }, ctrl: false);
        controller.PointerMove(new Point(30f, 10f), new Point(30f, 10f));
        controller.PointerUp(new Point(30f, 10f));

        Assert.Equal(20f, a.PosX.Value);
        Assert.Equal(40f, b.PosX.Value);
    }

    [Fact]
    public void Click_DoesNotBeginMutation()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var rect = Rect(0f, 0f);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = rect }, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));

        Assert.Equal(0, host.MutationStarts);
    }

    [Fact]
    public void EmptySpaceDrag_DoesNotBeginMutation()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);

        controller.PointerDown(new Point(50f, 50f), new Point(50f, 50f), default, ctrl: false);
        controller.PointerMove(new Point(80f, 50f), new Point(80f, 50f));
        controller.PointerUp(new Point(80f, 50f));

        Assert.Equal(0, host.MutationStarts);
    }

    [Fact]
    public void Drag_BeginsMutationOnce()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var rect = Rect(0f, 0f);
        host.Select(rect);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = rect }, ctrl: false);
        controller.PointerMove(new Point(20f, 5f), new Point(20f, 5f));
        controller.PointerMove(new Point(30f, 5f), new Point(30f, 5f));
        controller.PointerUp(new Point(30f, 5f));

        Assert.Equal(1, host.MutationStarts);
    }

    [Fact]
    public void Resize_BeginsMutationOnce()
    {
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var rect = Rect(0f, 0f);
        host.Select(rect);

        controller.PointerDown(
            new Point(10f, 0f),
            new Point(10f, 0f),
            new HitTestResult { Handle = ResizeHandle.BottomRight, Primitive = rect },
            ctrl: false);
        controller.PointerMove(new Point(30f, 0f), new Point(30f, 0f));
        controller.PointerMove(new Point(40f, 0f), new Point(40f, 0f));
        controller.PointerUp(new Point(40f, 0f));

        Assert.Equal(1, host.MutationStarts);
    }

    private sealed class TestHost : IEditorInteractionHost
    {
        private readonly List<Primitive> _selection = new();

        public Primitive? Selected { get; private set; }
        public List<Primitive> Selection => _selection;
        public bool Cleared { get; private set; }
        public List<Primitive> Notified { get; } = new();
        public List<Point> PanCalls { get; } = new();
        public int MutationStarts { get; private set; }

        public void Select(Primitive primitive)
        {
            _selection.Clear();
            _selection.Add(primitive);
        }

        public void SelectRange(params Primitive[] primitives)
        {
            _selection.Clear();
            _selection.AddRange(primitives);
        }

        public IReadOnlyList<Primitive> GetSelection() => _selection;

        public bool IsSelected(Primitive primitive) => _selection.Contains(primitive);

        public void SetSelected(Primitive primitive)
        {
            Selected = primitive;
            Select(primitive);
        }

        public void AppendSelected(Primitive primitive)
        {
            if (!_selection.Contains(primitive))
            {
                _selection.Add(primitive);
            }
        }

        public void ToggleSelected(Primitive primitive)
        {
            if (_selection.Contains(primitive))
            {
                _selection.Remove(primitive);
            }
            else
            {
                _selection.Add(primitive);
            }
        }

        public void ClearSelection()
        {
            Cleared = true;
            _selection.Clear();
        }

        public void PanByCss(float deltaX, float deltaY)
        {
            PanCalls.Add(new Point(deltaX, deltaY));
        }

        public void BeginMutation()
        {
            MutationStarts++;
        }

        public void CommitMutation()
        {
        }

        public void CancelMutation()
        {
        }

        public void NotifyPrimitivesChanged(IReadOnlyList<Primitive> primitives)
        {
            Notified.AddRange(primitives);
        }
    }
}
