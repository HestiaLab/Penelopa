using Bunit;
using Penelopa.Core.Primitives;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentStorageTests : TestContext
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
}
