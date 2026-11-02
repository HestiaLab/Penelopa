using System.Text.Json;
using Bunit;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentStorageTests : BunitContext
{
    [Fact]
    public async Task SaveAsync_StorageFailure_ReturnsSingleLineSafeSummary()
    {
        JSInterop.Setup<StorageReadResult>("penelopa.readStorage", _ => true)
            .SetResult(new StorageReadResult(true, null, null));
        JSInterop.Setup<StorageWriteResult>("penelopa.writeStorage", _ => true)
            .SetResult(new StorageWriteResult(
                false,
                "quota exceeded at Storage.setItem (/_framework/blazor.webassembly.js:1:2)"));
        var storage = new DocumentStorage(JSInterop.JSRuntime);

        var result = await storage.SaveAsync(
            new Primitive[] { new Circle() },
            Array.Empty<Primitive>());

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("quota exceeded", result.Error);
        Assert.DoesNotContain("\n", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
        Assert.DoesNotContain("_framework", result.Error);
    }

    [Fact]
    public async Task LoadAsync_StorageFailure_ReturnsReadableError()
    {
        JSInterop.Setup<StorageReadResult>("penelopa.readStorage", _ => true)
            .SetResult(new StorageReadResult(false, null, "SecurityError"));
        var storage = new DocumentStorage(JSInterop.JSRuntime);

        var result = await storage.LoadAsync();

        Assert.Null(result.Document);
        Assert.NotNull(result.Error);
        Assert.Contains("could not be read", result.Error);
        Assert.Contains("SecurityError", result.Error);
    }

    [Fact]
    public async Task LoadAsync_UnexpectedException_ReturnsErrorResultInsteadOfThrowing()
    {
        JSInterop.Setup<StorageReadResult>("penelopa.readStorage", _ => true)
            .SetException(new NullReferenceException("Unexpected storage failure."));
        var storage = new DocumentStorage(JSInterop.JSRuntime);

        var result = await storage.LoadAsync();

        Assert.Null(result.Document);
        Assert.NotNull(result.Error);
        Assert.Contains("could not be read", result.Error);
        Assert.DoesNotContain(" at ", result.Error);
    }

    [Fact]
    public async Task SaveAsync_CapturesStorageKeyBeforeAwait_AndReportsSavedIdentity()
    {
        var runtime = new FakeBrowserRuntime();
        runtime.Storage["penelopa.document"] = "old-a";
        runtime.Storage["penelopa.document.b"] = "old-b";
        var storage = new DocumentStorage(runtime);
        var saved = new List<DocumentSavedEvent>();
        storage.Saved += saved.Add;
        var documentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        runtime.BeforeStorageWrite = key =>
        {
            if (key == "penelopa.document.backup")
            {
                // 模拟备份写入期间用户切换会话。
                storage.SetActiveKey("penelopa.document.b");
            }
        };

        var result = await storage.SaveAsync(
            new Primitive[] { new Circle() },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: 3);

        Assert.True(result.Success, result.Error);
        Assert.Equal("old-b", runtime.Storage["penelopa.document.b"]);
        Assert.DoesNotContain("penelopa.document.b", runtime.StorageWrites);
        Assert.Contains("penelopa.document", runtime.StorageWrites);
        var savedEvent = Assert.Single(saved);
        Assert.Equal("penelopa.document", savedEvent.StorageKey);
        Assert.Equal(documentId, savedEvent.DocumentId);
        Assert.Equal(3, savedEvent.Revision);
    }

    [Fact]
    public async Task SaveAsync_RejectsPayloadLargerThanDocumentLimit()
    {
        using var extensionDocument = JsonDocument.Parse(
            $$"""{"blob":"{{new string('x', DocumentSerializer.MaxDocumentBytes)}}"}""");
        var extensions = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["huge"] = extensionDocument.RootElement.GetProperty("blob").Clone(),
        };
        var runtime = new FakeBrowserRuntime();
        var storage = new DocumentStorage(runtime);

        var result = await storage.SaveAsync(
            new Primitive[] { new Circle() },
            Array.Empty<Primitive>(),
            extensions: extensions);

        Assert.False(result.Success);
        Assert.Contains("larger than 4 MB", result.Error);
        Assert.Empty(runtime.StorageWrites);
    }
}
