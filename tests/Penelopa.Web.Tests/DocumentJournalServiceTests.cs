using System.Security.Cryptography;
using System.Text;
using Bunit;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentJournalServiceTests : BunitContext
{
    [Fact]
    public async Task Initialize_WithoutJournal_DoesNotOfferRecovery()
    {
        SetupStorage(null);
        JSInterop.Setup<JournalReadResult>("penelopa.readJournal", _ => true)
            .SetResult(new JournalReadResult(true, null, null));
        using var journal = CreateJournal(out _);

        await journal.InitializeAsync();

        Assert.Null(journal.PendingRecovery);
        Assert.Null(journal.PendingEntry);
    }

    [Fact]
    public async Task Initialize_NewerJournal_OffersRecovery()
    {
        var documentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var savedJson = DocumentSerializer.Serialize(
            new Primitive[] { new Rectangle { Name = { Value = "Saved" } } },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: 2);
        var journalJson = DocumentSerializer.Serialize(
            new Primitive[] { new Rectangle { Name = { Value = "Recovered" } } },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: 5);
        SetupStorage(savedJson);
        SetupJournalRead(CreateEntry(documentId, revision: 5, savedRevision: 2, journalJson));
        JSInterop.Setup<JournalWriteResult>("penelopa.clearJournal", _ => true)
            .SetResult(new JournalWriteResult(true, null));
        using var journal = CreateJournal(out _);

        await journal.InitializeAsync();

        Assert.NotNull(journal.PendingRecovery);
        Assert.Equal("Recovered", Assert.Single(journal.PendingRecovery!.Roots).Name.Value);
        Assert.Equal(5, journal.PendingEntry!.Revision);

        await journal.DiscardRecoveryAsync();

        Assert.Null(journal.PendingRecovery);
        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.clearJournal");
    }

    [Fact]
    public async Task Initialize_JournalAtSavedRevision_IsCleared()
    {
        var documentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Circle() },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: 4);
        SetupStorage(json);
        SetupJournalRead(CreateEntry(documentId, revision: 4, savedRevision: 4, json));
        JSInterop.Setup<JournalWriteResult>("penelopa.clearJournal", _ => true)
            .SetResult(new JournalWriteResult(true, null));
        using var journal = CreateJournal(out _);

        await journal.InitializeAsync();

        Assert.Null(journal.PendingRecovery);
        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.clearJournal");
    }

    [Fact]
    public async Task Initialize_JournalHashMismatch_IsCleared()
    {
        var documentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Circle() },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: 7);
        SetupStorage(null);
        SetupJournalRead(CreateEntry(
            documentId,
            revision: 7,
            savedRevision: -1,
            json,
            hash: "not-the-hash"));
        JSInterop.Setup<JournalWriteResult>("penelopa.clearJournal", _ => true)
            .SetResult(new JournalWriteResult(true, null));
        using var journal = CreateJournal(out _);

        await journal.InitializeAsync();

        Assert.Null(journal.PendingRecovery);
        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.clearJournal");
    }

    [Fact]
    public async Task DocumentChange_AutosavesAfterDebounce()
    {
        SetupStorage(null);
        JSInterop.Setup<JournalReadResult>("penelopa.readJournal", _ => true)
            .SetResult(new JournalReadResult(true, null, null));
        JSInterop.Setup<JournalWriteResult>("penelopa.writeJournal", _ => true)
            .SetResult(new JournalWriteResult(true, null));
        using var journal = CreateJournal(out var primitives);

        await journal.InitializeAsync();
        primitives.Add(new Circle { Name = { Value = "Autosaved" } });
        await Task.Delay(1_900);

        var write = Assert.Single(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeJournal");
        var entry = Assert.IsType<JournalEntryDto>(write.Arguments[0]);
        Assert.Equal("penelopa.document", write.Arguments[1]);
        Assert.Equal("Autosaved", DocumentSerializer.Deserialize(entry.Json)!
            .Roots[0].Name.Value);
        Assert.Equal(1, entry.Revision);
        Assert.Equal(-1, entry.SavedRevision);
        Assert.False(string.IsNullOrWhiteSpace(entry.Hash));
    }

    [Fact]
    public async Task ConfigChange_AutosavesAfterDebounce()
    {
        SetupStorage(null);
        JSInterop.Setup<JournalReadResult>("penelopa.readJournal", _ => true)
            .SetResult(new JournalReadResult(true, null, null));
        JSInterop.Setup<JournalWriteResult>("penelopa.writeJournal", _ => true)
            .SetResult(new JournalWriteResult(true, null));
        var primitives = new PrimitiveService();
        var config = new SimulationConfigStore(primitives);
        var storage = new DocumentStorage(JSInterop.JSRuntime);
        using var journal = new DocumentJournalService(
            JSInterop.JSRuntime,
            primitives,
            config,
            storage);

        await journal.InitializeAsync();
        var circle = new Circle();
        primitives.Add(circle);
        Assert.True(config.TryAddTrack(
            circle.Id,
            nameof(Circle.CenterX),
            time: 0d,
            value: 10f,
            out _));
        await Task.Delay(1_900);

        var write = Assert.Single(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeJournal");
        var entry = Assert.IsType<JournalEntryDto>(write.Arguments[0]);
        Assert.Equal("penelopa.document", write.Arguments[1]);
        var document = DocumentSerializer.Deserialize(entry.Json)!;
        Assert.Single(document.Simulation!.Tracks);
        Assert.Equal(circle.Id, document.Simulation.Tracks[0].PrimitiveId);
    }

    [Fact]
    public async Task PendingRecovery_SuppressesAutosaveUntilResolved()
    {
        var documentId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var json = DocumentSerializer.Serialize(
            new Primitive[] { new Circle() },
            Array.Empty<Primitive>(),
            documentId: documentId,
            revision: 3);
        SetupStorage(null);
        SetupJournalRead(CreateEntry(documentId, revision: 3, savedRevision: -1, json));
        JSInterop.Setup<JournalWriteResult>("penelopa.clearJournal", _ => true)
            .SetResult(new JournalWriteResult(true, null));
        JSInterop.Setup<JournalWriteResult>("penelopa.writeJournal", _ => true)
            .SetResult(new JournalWriteResult(true, null));
        using var journal = CreateJournal(out var primitives);

        await journal.InitializeAsync();
        primitives.Add(new Rectangle());
        await Task.Delay(1_900);

        Assert.DoesNotContain(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeJournal");

        journal.ResolveRecovery();
        primitives.Add(new Triangle());
        await Task.Delay(1_900);

        Assert.Contains(
            JSInterop.Invocations,
            invocation => invocation.Identifier == "penelopa.writeJournal");
    }

    private void SetupStorage(string? json)
    {
        JSInterop.Setup<StorageReadResult>("penelopa.readStorage", _ => true)
            .SetResult(new StorageReadResult(true, json, null));
    }

    private void SetupJournalRead(JournalEntryDto entry)
    {
        JSInterop.Setup<JournalReadResult>("penelopa.readJournal", _ => true)
            .SetResult(new JournalReadResult(true, entry, null));
    }

    private DocumentJournalService CreateJournal(out PrimitiveService primitives)
    {
        primitives = new PrimitiveService();
        var config = new SimulationConfigStore(primitives);
        var storage = new DocumentStorage(JSInterop.JSRuntime);
        return new DocumentJournalService(JSInterop.JSRuntime, primitives, config, storage);
    }

    private static JournalEntryDto CreateEntry(
        Guid documentId,
        long revision,
        long savedRevision,
        string json,
        string? hash = null)
    {
        return new JournalEntryDto(
            documentId,
            revision,
            savedRevision,
            DateTimeOffset.UtcNow.ToString("O"),
            json,
            hash ?? ComputeHash(json));
    }

    private static string ComputeHash(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
}
