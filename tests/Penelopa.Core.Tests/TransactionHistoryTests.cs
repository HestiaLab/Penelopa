using Penelopa.Core.Alignment;
using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class TransactionHistoryTests
{
    private static Rectangle Rect(float x, float y, float w = 10f, float h = 10f)
        => new Rectangle { PosX = { Value = x }, PosY = { Value = y }, Width = { Value = w }, Height = { Value = h } };

    [Fact]
    public void Undo_AfterAlign_RestoresPositionsAndKeepsPrimitives()
    {
        var service = new PrimitiveService();
        var a = Rect(0f, 0f);
        var b = Rect(30f, 0f);
        service.Add(a);
        service.Add(b);
        service.SetSelectedRange(new Primitive[] { a, b });

        Assert.True(service.Align(AlignType.Left));
        Assert.Equal(0f, b.PosX.Value);

        service.Undo();

        Assert.Equal(30f, b.PosX.Value);
        Assert.Contains(a, service.GetAll());
        Assert.Contains(b, service.GetAll());
    }

    [Fact]
    public void Undo_AfterPropertyEdit_RestoresValue()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);

        Assert.True(service.TrySetProperty(circle, circle.Radius, "20", out var error));
        Assert.Null(error);
        Assert.Equal(20f, circle.Radius.Value);

        service.Undo();

        Assert.Equal(5f, circle.Radius.Value);
        Assert.Contains(circle, service.GetAll());
    }

    [Fact]
    public void Redo_AfterPropertyEdit_ReappliesValue()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);
        service.TrySetProperty(circle, circle.Radius, "20", out _);

        service.Undo();
        service.Redo();

        Assert.Equal(20f, circle.Radius.Value);
    }

    [Fact]
    public void Align_AlreadyAligned_DoesNotAddHistoryEntry()
    {
        var service = new PrimitiveService();
        var a = Rect(0f, 0f);
        var b = Rect(0f, 20f);
        service.Add(a);
        service.Add(b);
        service.SetSelectedRange(new Primitive[] { a, b });

        Assert.False(service.Align(AlignType.Left));

        // 若对齐偷偷压入了历史，这里会撤销“对齐”而不是最后一次 Add
        service.Undo();
        Assert.DoesNotContain(b, service.GetAll());
        Assert.Contains(a, service.GetAll());
    }

    [Fact]
    public void InvalidPropertyEdit_DoesNotChangeValueOrAddHistory()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);

        Assert.False(service.TrySetProperty(circle, circle.Radius, "-5", out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(5f, circle.Radius.Value);

        service.Undo();
        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void UnchangedPropertyEdit_DoesNotAddHistory()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);

        Assert.True(service.TrySetProperty(circle, circle.Radius, "5", out _));

        service.Undo();
        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void DragGesture_CreatesSingleUndoEntry()
    {
        var service = new PrimitiveService();
        var a = Rect(0f, 0f);
        var b = Rect(20f, 0f);
        service.Add(a);
        service.Add(b);
        service.SetSelectedRange(new Primitive[] { a, b });
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = a }, ctrl: false);
        controller.PointerMove(new Point(25f, 5f), new Point(25f, 5f));
        controller.PointerMove(new Point(35f, 15f), new Point(35f, 15f));
        controller.PointerUp(new Point(35f, 15f));

        Assert.Equal(30f, a.PosX.Value);
        Assert.Equal(50f, b.PosX.Value);
        Assert.Equal(1, host.MutationStarts);
        Assert.Equal(1, host.MutationCommits);

        service.Undo();

        Assert.Equal(0f, a.PosX.Value);
        Assert.Equal(20f, b.PosX.Value);
        Assert.Contains(a, service.GetAll());
        Assert.Contains(b, service.GetAll());
    }

    [Fact]
    public void ResizeGesture_CreatesSingleUndoEntry()
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
        controller.PointerMove(new Point(30f, 0f), new Point(30f, 0f));
        controller.PointerMove(new Point(40f, 0f), new Point(40f, 0f));
        controller.PointerUp(new Point(40f, 0f));

        Assert.Equal(40f, rect.Width.Value);
        Assert.Equal(1, host.MutationStarts);
        Assert.Equal(1, host.MutationCommits);

        service.Undo();

        Assert.Equal(10f, rect.Width.Value);
        Assert.Contains(rect, service.GetAll());
    }

    [Fact]
    public void CancelGesture_DoesNotAddHistoryEntry()
    {
        var service = new PrimitiveService();
        var rect = Rect(0f, 0f);
        service.Add(rect);
        service.SetSelected(rect);
        var host = new ServiceInteractionHost(service);
        var controller = new EditorInteractionController(host);

        controller.PointerDown(new Point(5f, 5f), new Point(5f, 5f), new HitTestResult { Primitive = rect }, ctrl: false);
        controller.PointerMove(new Point(35f, 5f), new Point(35f, 5f));
        controller.Cancel();

        Assert.Equal(0f, rect.PosX.Value);
        Assert.Equal(1, host.MutationCancels);

        service.Undo();
        Assert.Empty(service.GetAll());
    }

    [Fact]
    public void Transaction_Commit_ProducesOneUndoEntry()
    {
        var service = new PrimitiveService();
        var rect = Rect(0f, 0f);
        service.Add(rect);

        service.BeginTransaction();
        rect.PosX.Value = 40f;
        service.CommitTransaction();

        service.Undo();
        Assert.Equal(0f, rect.PosX.Value);
    }
}
