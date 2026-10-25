using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using Xunit;

namespace Penelopa.Core.Tests;

public class DrillDownTests
{
    private static (Container Container, Rectangle Child) Nested()
    {
        var container = Container.CreateOffset("c", 0f, 0f);
        var child = new Rectangle { PosX = { Value = 0f }, PosY = { Value = 0f }, Width = { Value = 10f }, Height = { Value = 10f } };
        container.AddChild(child);
        return (container, child);
    }

    [Fact]
    public void FirstClick_SelectsOutermostCandidate()
    {
        var (container, child) = Nested();
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        // Candidates are ordered root → deepest leaf (confirmed drill direction).
        var hit = new HitTestResult { Primitive = child, Candidates = new Primitive[] { container, child } };

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: false);

        Assert.Same(container, host.Selected);
    }

    [Fact]
    public void RepeatedClick_AdvancesTowardDeepestLeaf()
    {
        var (container, child) = Nested();
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var hit = new HitTestResult { Primitive = child, Candidates = new Primitive[] { container, child } };

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));
        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));

        Assert.Same(child, host.Selected);
    }

    [Fact]
    public void ClickAtDifferentSpot_ResetsToOutermost()
    {
        var (container, child) = Nested();
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var hit = new HitTestResult { Primitive = child, Candidates = new Primitive[] { container, child } };

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));
        // Move far away → different chain/spot, resets index.
        controller.PointerDown(new Point(500f, 500f), new Point(500f, 500f), hit, ctrl: false);
        controller.PointerUp(new Point(500f, 500f));

        Assert.Same(container, host.Selected);
    }

    [Fact]
    public void AtDeepestLeaf_FurtherClicksStayOnLeaf()
    {
        var (container, child) = Nested();
        var host = new TestHost();
        var controller = new EditorInteractionController(host);
        var hit = new HitTestResult { Primitive = child, Candidates = new Primitive[] { container, child } };

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));
        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));
        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), hit, ctrl: false);
        controller.PointerUp(new Point(5f, 5f));

        Assert.Same(child, host.Selected); // does not wrap around
    }

    private sealed class TestHost : IEditorInteractionHost
    {
        public Primitive? Selected { get; private set; }
        public List<Primitive> Selection { get; } = new();
        public List<Point> PanCalls { get; } = new();

        public IReadOnlyList<Primitive> GetSelection() => Selection;

        public bool IsSelected(Primitive primitive) => Selection.Contains(primitive);

        public void SetSelected(Primitive primitive)
        {
            Selected = primitive;
            Selection.Clear();
            Selection.Add(primitive);
        }

        public void AppendSelected(Primitive primitive)
        {
            if (!Selection.Contains(primitive))
            {
                Selection.Add(primitive);
            }
        }

        public void ToggleSelected(Primitive primitive)
        {
            if (Selection.Contains(primitive))
            {
                Selection.Remove(primitive);
            }
            else
            {
                Selection.Add(primitive);
            }
        }

        public void ClearSelection() => Selection.Clear();

        public void PanByCss(float deltaX, float deltaY)
        {
            PanCalls.Add(new Point(deltaX, deltaY));
        }

        public void BeginMutation()
        {
        }

        public void CommitMutation()
        {
        }

        public void CancelMutation()
        {
        }

        public void NotifyPrimitivesChanged(IReadOnlyList<Primitive> primitives)
        {
        }
    }
}
