using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Xunit;

namespace Penelopa.Core.Tests;

public class DocumentHistoryStateTests
{
    [Fact]
    public void CaptureRestore_RestoresDocumentAndUndoRedoStacks()
    {
        var service = new PrimitiveService();
        var circle = new Circle();
        service.Add(circle);
        Assert.True(service.TrySetProperty(circle, circle.Radius, "20", out var error), error);
        service.Undo();

        var state = service.CaptureHistoryState();
        service.ReplaceAll(new Primitive[] { new Rectangle() });

        Assert.Equal("Rectangle", Assert.Single(service.GetAll()).Name.Value);
        Assert.False(service.CanUndo);
        Assert.False(service.CanRedo);

        service.RestoreHistoryState(state);

        Assert.Same(circle, Assert.Single(service.GetAll()));
        Assert.Equal(5f, circle.Radius.Value);
        Assert.True(service.CanUndo);
        Assert.True(service.CanRedo);

        service.Redo();
        Assert.Equal(20f, circle.Radius.Value);

        service.Undo();
        Assert.Equal(5f, circle.Radius.Value);
    }

    [Fact]
    public void Capture_IsRejectedWhileATransactionIsOpen()
    {
        var service = new PrimitiveService();
        service.Add(new Circle());
        service.BeginTransaction();

        try
        {
            Assert.Throws<InvalidOperationException>(() => service.CaptureHistoryState());
        }
        finally
        {
            service.CancelTransaction();
        }
    }

    [Fact]
    public void NotifyTransientValuesChanged_RaisesTransientKind()
    {
        var service = new PrimitiveService();
        DocumentChange? raised = null;
        service.Changed += change => raised = change;

        service.NotifyTransientValuesChanged();

        Assert.NotNull(raised);
        Assert.Equal(DocumentChangeKind.Transient, raised.Value.Kind);
    }
}
