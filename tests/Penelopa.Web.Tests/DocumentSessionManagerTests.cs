using Bunit;
using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentSessionManagerTests : BunitContext
{
    public DocumentSessionManagerTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<StorageReadResult>("penelopa.readStorage", _ => true)
            .SetResult(new StorageReadResult(true, null, null));
        JSInterop.Setup<StorageWriteResult>("penelopa.writeStorage", _ => true)
            .SetResult(new StorageWriteResult(true, null));
    }

    [Fact]
    public async Task Sessions_KeepIndependentDocumentsHistoryAndConfiguration()
    {
        using var manager = new DocumentSessionManager(JSInterop.JSRuntime);
        var first = manager.Active;
        var circle = new Circle();
        manager.Editor.Add(circle);
        Assert.True(manager.ConfigStore.TryAddTrack(
            circle.Id,
            nameof(Circle.CenterX),
            time: 0d,
            value: 10f,
            out _));

        var (created, error) = await manager.CreateNewAsync();

        Assert.True(created, error);
        Assert.Equal(2, manager.Sessions.Count);
        Assert.NotEqual(first.Id, manager.Active.Id);
        Assert.Empty(manager.ActiveProxy.GetAll());
        Assert.Empty(manager.ConfigStore.Config.Tracks);

        var rectangle = new Rectangle();
        manager.Editor.Add(rectangle);
        Assert.Single(manager.ActiveProxy.GetAll());

        Assert.True(await manager.ActivateAsync(first.Id));
        Assert.Same(circle, Assert.Single(manager.ActiveProxy.GetAll()));
        Assert.Single(manager.ConfigStore.Config.Tracks);
        Assert.True(manager.ActiveProxy.CanUndo);
        manager.ActiveProxy.Undo();
        Assert.Empty(manager.ConfigStore.Config.Tracks);
        Assert.Same(circle, Assert.Single(manager.ActiveProxy.GetAll()));
        manager.ActiveProxy.Undo();
        Assert.Empty(manager.ActiveProxy.GetAll());

        Assert.True(await manager.ActivateAsync(manager.Sessions[1].Id));
        Assert.Same(rectangle, Assert.Single(manager.ActiveProxy.GetAll()));
    }

    [Fact]
    public async Task Sessions_AreLimitedToThree()
    {
        using var manager = new DocumentSessionManager(JSInterop.JSRuntime);

        Assert.True((await manager.CreateNewAsync()).Success);
        Assert.True((await manager.CreateNewAsync()).Success);
        var fourth = await manager.CreateNewAsync();

        Assert.False(fourth.Success);
        Assert.Contains("3", fourth.Error);
        Assert.Equal(3, manager.Sessions.Count);
    }

    [Fact]
    public async Task Close_RemovesSessionHistoryAndKeepsAtLeastOne()
    {
        using var manager = new DocumentSessionManager(JSInterop.JSRuntime);
        var first = manager.Active;
        await manager.CreateNewAsync();
        var second = manager.Active;
        manager.Editor.Add(new Circle());

        var closed = await manager.CloseAsync(second.Id);

        Assert.True(closed.Success, closed.Error);
        Assert.Single(manager.Sessions);
        Assert.Equal(first.Id, manager.Active.Id);
        Assert.Empty(manager.ActiveProxy.GetAll());

        var last = await manager.CloseAsync(first.Id);

        Assert.False(last.Success);
        Assert.Contains("one document", last.Error);
    }

    [Fact]
    public async Task ActiveDescriptor_IsPersistedForReload()
    {
        using var manager = new DocumentSessionManager(JSInterop.JSRuntime);
        await manager.CreateNewAsync();

        var write = Assert.Single(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeStorage"
                && Equals(invocation.Arguments[0], DocumentStorage.ActiveSessionKey));
        var json = Assert.IsType<string>(write.Arguments[1]);
        Assert.Contains("\"Name\":\"Document 2\"", json);
        Assert.Contains("penelopa.document.", json);
    }
}
