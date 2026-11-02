using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentSessionManagerRecoveryTests
{
    [Fact]
    public async Task Initialize_DefaultSession_RestoresSavedContent()
    {
        var runtime = new FakeBrowserRuntime();
        var id = Guid.Parse("10101010-1010-1010-1010-101010101010");
        runtime.Storage[DocumentStorage.StorageKey] = Serialize(id, 2, "SavedDefault");
        using var manager = new DocumentSessionManager(runtime);

        await manager.InitializeAsync();
        await manager.Journal.InitializeAsync();

        Assert.Single(manager.Sessions);
        Assert.Equal(id, manager.ActiveProxy.DocumentId);
        Assert.Equal(2, manager.ActiveProxy.Revision);
        Assert.Equal("SavedDefault", Assert.Single(manager.ActiveProxy.GetAll()).Name.Value);
        Assert.False(manager.HasUnsavedChanges(manager.Active));
        Assert.Null(manager.Journal.PendingRecovery);
    }

    [Fact]
    public async Task Initialize_MultipleDocuments_RestoresSavedContentAndDefersJournalConfirmation()
    {
        var runtime = new FakeBrowserRuntime();
        var firstId = Guid.Parse("31313131-3131-3131-3131-313131313131");
        var secondId = Guid.Parse("32323232-3232-3232-3232-323232323232");
        var firstStorage = $"penelopa.document.{firstId:N}";
        var firstJournal = $"penelopa.journal.{firstId:N}";
        var secondStorage = $"penelopa.document.{secondId:N}";
        var secondJournal = $"penelopa.journal.{secondId:N}";
        runtime.Storage[firstStorage] = Serialize(firstId, 1, "FirstSaved");
        runtime.Storage[secondStorage] = Serialize(secondId, 1, "SecondSaved");
        runtime.Journals[firstJournal] = Entry(
            firstId,
            4,
            1,
            Serialize(firstId, 4, "FirstNewer"));
        runtime.Storage[DocumentSessionManager.SessionsKey] =
            JsonSerializer.Serialize(new SessionRegistry(
                secondId,
                new List<SessionDescriptor>
                {
                    new(firstId, "Document 1", firstStorage, firstJournal),
                    new(secondId, "Document 2", secondStorage, secondJournal),
                }));

        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();
        await manager.Journal.InitializeAsync();

        Assert.Equal(secondId, manager.ActiveProxy.DocumentId);
        Assert.Equal("SecondSaved", Assert.Single(manager.ActiveProxy.GetAll()).Name.Value);
        Assert.False(manager.HasUnsavedChanges(manager.Active));

        Assert.True(await manager.ActivateAsync(firstId));
        Assert.Equal(firstId, manager.ActiveProxy.DocumentId);
        Assert.Equal("FirstSaved", Assert.Single(manager.ActiveProxy.GetAll()).Name.Value);
        Assert.NotNull(manager.Journal.PendingRecovery);
        Assert.Equal("FirstNewer", Assert.Single(manager.Journal.PendingRecovery!.Roots).Name.Value);
        Assert.True(manager.HasUnsavedChanges(manager.Active));
    }

    [Fact]
    public async Task Initialize_SingleActiveDescriptor_RestoresSavedContent()
    {
        var runtime = new FakeBrowserRuntime();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var storageKey = $"penelopa.document.{id:N}";
        var journalKey = $"penelopa.journal.{id:N}";
        runtime.Storage[storageKey] = Serialize(id, 1, "SavedSingle");
        runtime.Storage[DocumentStorage.ActiveSessionKey] =
            JsonSerializer.Serialize(new ActiveSessionDescriptor(
                id,
                "Document 2",
                storageKey,
                journalKey));

        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();
        await manager.Journal.InitializeAsync();

        Assert.Single(manager.Sessions);
        Assert.Equal(id, manager.Active.Id);
        Assert.Equal("SavedSingle", Assert.Single(manager.ActiveProxy.GetAll()).Name.Value);
        Assert.Equal(1, manager.ActiveProxy.Revision);
        Assert.Null(manager.Journal.PendingRecovery);
    }

    [Fact]
    public async Task Initialize_SingleActiveDescriptor_LeavesNewerJournalForConfirmation()
    {
        var runtime = new FakeBrowserRuntime();
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var storageKey = $"penelopa.document.{id:N}";
        var journalKey = $"penelopa.journal.{id:N}";
        runtime.Storage[storageKey] = Serialize(id, 1, "SavedSingle");
        runtime.Journals[journalKey] = Entry(
            id,
            3,
            1,
            Serialize(id, 3, "NewerJournal"));
        runtime.Storage[DocumentStorage.ActiveSessionKey] =
            JsonSerializer.Serialize(new ActiveSessionDescriptor(
                id,
                "Document 2",
                storageKey,
                journalKey));

        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();
        await manager.Journal.InitializeAsync();

        Assert.Equal("SavedSingle", Assert.Single(manager.ActiveProxy.GetAll()).Name.Value);
        Assert.NotNull(manager.Journal.PendingRecovery);
        Assert.Equal("NewerJournal", Assert.Single(manager.Journal.PendingRecovery!.Roots).Name.Value);
        Assert.True(manager.HasUnsavedChanges(manager.Active));
    }

    [Fact]
    public async Task PendingRecovery_SurvivesSessionRoundTrip()
    {
        var runtime = new FakeBrowserRuntime();
        var id = Guid.Parse("33333333-3333-3333-3333-333333333333");
        runtime.Storage[DocumentStorage.StorageKey] = Serialize(id, 1, "Saved");
        runtime.Journals[DocumentStorage.StorageKey] = Entry(
            id,
            4,
            1,
            Serialize(id, 4, "Newer"));
        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();
        await manager.Journal.InitializeAsync();
        var first = manager.Active;
        Assert.NotNull(manager.Journal.PendingRecovery);

        var (created, error) = await manager.CreateNewAsync();
        Assert.True(created, error);
        Assert.NotEqual(first.Id, manager.Active.Id);
        Assert.Null(manager.Journal.PendingRecovery);

        Assert.True(await manager.ActivateAsync(first.Id));
        Assert.NotNull(manager.Journal.PendingRecovery);
        Assert.Equal(4, runtime.Journals[DocumentStorage.StorageKey].Revision);
    }

    [Fact]
    public async Task Initialize_RegistryWithTooManySessions_DoesNotRestoreMoreThanLimit()
    {
        var runtime = new FakeBrowserRuntime();
        var descriptors = Enumerable.Range(0, DocumentSessionManager.MaxSessions + 1)
            .Select(index =>
            {
                var id = Guid.NewGuid();
                return new SessionDescriptor(
                    id,
                    $"Document {index + 1}",
                    $"penelopa.document.{id:N}",
                    $"penelopa.journal.{id:N}");
            })
            .ToList();
        runtime.Storage[DocumentSessionManager.SessionsKey] =
            JsonSerializer.Serialize(new SessionRegistry(
                descriptors[0].Id,
                descriptors));

        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();

        Assert.Single(manager.Sessions);
        Assert.Equal("Document 1", manager.Active.Name);
    }

    [Fact]
    public async Task Close_EmptySession_IsRetainedInLibrary()
    {
        var runtime = new FakeBrowserRuntime();
        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();
        await manager.Journal.InitializeAsync();
        var first = manager.Active;
        var (created, error) = await manager.CreateNewAsync();
        Assert.True(created, error);

        var closed = await manager.CloseAsync(first.Id);

        Assert.True(closed.Success, closed.Error);
        var entry = Assert.Single(
            manager.Library,
            candidate => candidate.StorageKey == DocumentStorage.StorageKey);
        Assert.True(entry.IsEmpty);
    }

    [Fact]
    public async Task Close_UnsavedNonEmptySession_AddsRecoverableLibraryEntry()
    {
        var runtime = new FakeBrowserRuntime();
        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();
        await manager.Journal.InitializeAsync();
        var first = manager.Active;
        manager.Editor.Add(new Circle { Name = { Value = "Unsaved" } });
        var (created, error) = await manager.CreateNewAsync();
        Assert.True(created, error);
        Assert.True(await manager.ActivateAsync(first.Id));

        var closed = await manager.CloseAsync(first.Id);

        Assert.True(closed.Success, closed.Error);
        var entry = Assert.Single(
            manager.Library,
            candidate => candidate.StorageKey == DocumentStorage.StorageKey);
        Assert.False(entry.IsSaved);
        Assert.NotNull(runtime.Journals[DocumentStorage.StorageKey]);
    }

    [Fact]
    public async Task Initialize_LegacyLibraryEntries_DefaultToSaved()
    {
        var runtime = new FakeBrowserRuntime();
        var id = Guid.Parse("20202020-2020-2020-2020-202020202020");
        runtime.Storage[DocumentSessionManager.LibraryKey] = $$"""
        [
          {
            "Id": "{{id}}",
            "Name": "Legacy",
            "StorageKey": "penelopa.document",
            "JournalKey": "penelopa.document",
            "SavedAt": "2026-10-30T00:00:00.0000000+00:00"
          }
        ]
        """;

        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();

        var entry = Assert.Single(manager.Library);
        Assert.True(entry.IsSaved);
    }

    [Fact]
    public async Task Initialize_NullLibraryEntries_AreIgnored()
    {
        var runtime = new FakeBrowserRuntime();
        runtime.Storage[DocumentSessionManager.LibraryKey] = "[null]";

        using var manager = new DocumentSessionManager(runtime);
        await manager.InitializeAsync();

        Assert.Empty(manager.Library);
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
