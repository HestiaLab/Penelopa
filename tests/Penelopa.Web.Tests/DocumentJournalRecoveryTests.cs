using System.Security.Cryptography;
using System.Text;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentJournalRecoveryTests
{
    private const string DefaultKey = "penelopa.document";
    private const string DefaultJournalKey = "penelopa.document";

    [Fact]
    public async Task Initialize_ForeignDocumentId_OffersRecoveryInsteadOfSilentlyIgnoring()
    {
        var runtime = new FakeBrowserRuntime();
        var savedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var importedId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        runtime.Storage[DefaultKey] = Serialize(savedId, 1, "Saved");
        runtime.Journals[DefaultJournalKey] = Entry(
            importedId,
            5,
            1,
            Serialize(importedId, 5, "Imported"));
        var (journal, _, _) = Create(runtime);

        await journal.InitializeAsync();

        Assert.NotNull(journal.PendingRecovery);
        Assert.True(journal.PendingDocumentDiffers);
        Assert.Equal("Imported", Assert.Single(journal.PendingRecovery!.Roots).Name.Value);
    }

    [Fact]
    public async Task SwitchSession_PreservesPendingRecoveryAndDoesNotOverwriteJournal()
    {
        var runtime = new FakeBrowserRuntime();
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        runtime.Storage[DefaultKey] = Serialize(id, 1, "Saved");
        runtime.Journals[DefaultJournalKey] = Entry(
            id,
            4,
            1,
            Serialize(id, 4, "Newer"));
        var (journal, _, _) = Create(runtime);
        await journal.InitializeAsync();
        Assert.NotNull(journal.PendingRecovery);

        await journal.SwitchSessionAsync("penelopa.journal.other");
        Assert.Null(journal.PendingRecovery);

        await journal.SwitchSessionAsync(DefaultJournalKey);
        Assert.NotNull(journal.PendingRecovery);

        await journal.FlushAsync();
        Assert.Equal(4, runtime.Journals[DefaultJournalKey].Revision);
    }

    [Fact]
    public async Task DiscardRecovery_WithEditsDuringPending_WritesCurrentState()
    {
        var runtime = new FakeBrowserRuntime();
        var id = Guid.Parse("44444444-4444-4444-4444-444444444444");
        runtime.Storage[DefaultKey] = Serialize(id, 1, "Saved");
        runtime.Journals[DefaultJournalKey] = Entry(
            id,
            4,
            1,
            Serialize(id, 4, "Newer"));
        var (journal, primitives, _) = Create(runtime);
        await journal.InitializeAsync();
        Assert.NotNull(journal.PendingRecovery);

        // 恢复横幅显示期间继续编辑；Discard 必须保留这些编辑。
        primitives.Add(new Circle { Name = { Value = "EditedDuringPending" } });
        await journal.DiscardRecoveryAsync();

        Assert.Null(journal.PendingRecovery);
        Assert.True(runtime.Journals.TryGetValue(DefaultJournalKey, out var entry));
        Assert.Equal(1, entry!.Revision);
        var saved = DocumentSerializer.Deserialize(entry.Json)!;
        Assert.Equal("EditedDuringPending", Assert.Single(saved.Roots).Name.Value);
    }

    [Fact]
    public async Task NotifyDocumentReplaced_ClearsPendingAndWritesImportedJournal()
    {
        var runtime = new FakeBrowserRuntime();
        var savedId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var importedId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        runtime.Storage[DefaultKey] = Serialize(savedId, 1, "Saved");
        runtime.Journals[DefaultJournalKey] = Entry(
            savedId,
            4,
            1,
            Serialize(savedId, 4, "Newer"));
        var (journal, primitives, _) = Create(runtime);
        await journal.InitializeAsync();
        Assert.NotNull(journal.PendingRecovery);

        primitives.ReplaceAll(new Primitive[]
        {
            new Rectangle { Name = { Value = "Imported" } },
        });
        primitives.SetDocumentIdentity(importedId, 0);
        await journal.NotifyDocumentReplacedAsync(importedId, 0);

        Assert.Null(journal.PendingRecovery);
        Assert.True(runtime.Journals.TryGetValue(DefaultJournalKey, out var entry));
        Assert.Equal(importedId, entry!.DocumentId);
        Assert.Equal(0, entry.Revision);
        var saved = DocumentSerializer.Deserialize(entry.Json)!;
        Assert.Equal("Imported", Assert.Single(saved.Roots).Name.Value);
    }

    [Fact]
    public async Task ExplicitSave_ClearsJournalFromAnotherDocument()
    {
        var runtime = new FakeBrowserRuntime();
        var savedId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var foreignId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        runtime.Storage[DefaultKey] = Serialize(savedId, 1, "Saved");
        runtime.Journals[DefaultJournalKey] = Entry(
            foreignId,
            3,
            0,
            Serialize(foreignId, 3, "Foreign"));
        var (journal, _, storage) = Create(runtime);
        await journal.InitializeAsync();
        Assert.NotNull(journal.PendingRecovery);

        var result = await storage.SaveAsync(
            new Primitive[] { new Circle { Name = { Value = "SavedNow" } } },
            Array.Empty<Primitive>(),
            documentId: savedId,
            revision: 2);

        Assert.True(result.Success, result.Error);
        await Task.Delay(50);
        Assert.False(
            runtime.Journals.ContainsKey(DefaultJournalKey),
            $"pendingNull={journal.PendingRecovery is null}, reads={string.Join(',', runtime.JournalReads)}, clears={string.Join(',', runtime.JournalClears)}");
        Assert.Null(journal.PendingRecovery);
    }

    private static (DocumentJournalService Journal, PrimitiveService Primitives, DocumentStorage Storage) Create(
        FakeBrowserRuntime runtime)
    {
        var primitives = new PrimitiveService();
        var config = new SimulationConfigStore(primitives);
        var storage = new DocumentStorage(runtime);
        var journal = new DocumentJournalService(runtime, primitives, config, storage);
        return (journal, primitives, storage);
    }

    private static string Serialize(Guid documentId, long revision, string name)
        => DocumentSerializer.Serialize(
            new Primitive[] { new Circle { Name = { Value = name } } },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: revision);

    private static JournalEntryDto Entry(
        Guid documentId,
        long revision,
        long savedRevision,
        string json)
        => new(
            documentId,
            revision,
            savedRevision,
            DateTimeOffset.UtcNow.ToString("O"),
            json,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
                .ToLowerInvariant());
}
