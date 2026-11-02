using System.Text.Json;
using Microsoft.JSInterop;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

/// <summary>文档库条目：显式保存或关闭时暂存的非空会话。</summary>
public sealed record DocumentLibraryEntry(
    Guid Id,
    string Name,
    string StorageKey,
    string JournalKey,
    string SavedAt)
{
    /// <summary>最后一次进入库时是否有未保存改动；旧版本条目缺省为 true。</summary>
    public bool IsSaved { get; init; } = true;

    /// <summary>关闭时没有任何图元、配置或待恢复内容的空会话。</summary>
    public bool IsEmpty { get; init; }
}

public sealed record SessionRegistry(Guid ActiveId, List<SessionDescriptor> Sessions);

public sealed record SessionDescriptor(
    Guid Id,
    string Name,
    string StorageKey,
    string JournalKey);

public sealed record ActiveSessionDescriptor(
    Guid Id,
    string Name,
    string StorageKey,
    string JournalKey);

/// <summary>
/// 最多同时打开 3 个文档会话。每个会话拥有独立 PrimitiveService（模型、选区、
/// revision 与统一历史）、仿真配置和存储/journal 键；活动会话通过代理暴露给
/// 既有 UI，切换时只替换代理目标，不重建组件。
/// </summary>
public sealed class DocumentSessionManager : IDisposable
{
    public const int MaxSessions = 3;
    public const string SessionsKey = "penelopa.sessions";
    public const string LibraryKey = "penelopa.library";

    private readonly IJSRuntime _js;
    private readonly List<DocumentSession> _sessions = new();
    private readonly List<DocumentLibraryEntry> _library = new();
    private readonly SemaphoreSlim _switchGate = new(1, 1);
    private DocumentSession _active;
    private bool _disposed;
    private int _nextNumber = 2;
    private Task? _initializeTask;

    public DocumentSessionManager(IJSRuntime js)
    {
        _js = js;
        ActiveProxy = new ActivePrimitiveService(this);
        Editor = new DocumentEditor(ActiveProxy);

        var initial = CreateSession(
            "Document 1",
            DocumentStorage.StorageKey,
            "penelopa.document");
        _active = initial;
        _sessions.Add(initial);

        ConfigStore = new SimulationConfigStore(initial.Primitives);
        Storage = new DocumentStorage(js);
        ActiveProxy.Attach(initial.Primitives);
        Journal = new DocumentJournalService(js, ActiveProxy, ConfigStore, Storage);
        ImportRollback = new DocumentImportRollbackService(
            ActiveProxy,
            Editor,
            ConfigStore,
            Storage);
        Storage.Saved += HandleSaved;
        Storage.Loaded += HandleLoaded;
    }

    public event Action? Changed;

    public ActivePrimitiveService ActiveProxy { get; }

    public DocumentEditor Editor { get; }

    public SimulationConfigStore ConfigStore { get; }

    public DocumentStorage Storage { get; }

    public DocumentJournalService Journal { get; }

    public DocumentImportRollbackService ImportRollback { get; }

    public IReadOnlyList<DocumentSession> Sessions => _sessions;

    public IReadOnlyList<DocumentLibraryEntry> Library => _library;

    public DocumentSession Active => _active;

    public Task InitializeAsync()
        => _disposed ? Task.CompletedTask : _initializeTask ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _switchGate.WaitAsync();
        try
        {
            var registry = await ReadJsonAsync<SessionRegistry>(SessionsKey);
            if (registry is { Sessions.Count: > 1 and <= MaxSessions })
            {
                var restored = new List<DocumentSession>();
                var ids = new HashSet<Guid>();
                var storageKeys = new HashSet<string>(StringComparer.Ordinal);
                var journalKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var descriptor in registry.Sessions)
                {
                    if (descriptor is null
                        || !IsValidDescriptor(descriptor)
                        || !ids.Add(descriptor.Id)
                        || !storageKeys.Add(descriptor.StorageKey)
                        || !journalKeys.Add(descriptor.JournalKey))
                    {
                        continue;
                    }

                    var session = CreateSession(
                        descriptor.Name,
                        descriptor.StorageKey,
                        descriptor.JournalKey,
                        descriptor.Id);
                    await RestoreContentAsync(session, preferJournal: false);
                    restored.Add(session);
                }

                if (restored.Count > 0)
                {
                    _sessions.Clear();
                    _sessions.AddRange(restored);
                    _active = restored.FirstOrDefault(
                        session => session.Id == registry.ActiveId) ?? restored[0];
                    _nextNumber = Math.Max(
                        2,
                        restored.Max(session => ParseDocumentNumber(session.Name)) + 1);
                    Storage.SetActiveKey(_active.StorageKey);
                    ConfigStore.SwitchDocument(_active.Primitives, _active.Config);
                    ActiveProxy.Attach(_active.Primitives);
                    ImportRollback.Clear();
                    await Journal.SwitchSessionAsync(_active.JournalKey);
                    foreach (var session in _sessions)
                    {
                        await Journal.PrepareSessionRecoveryAsync(
                            session.JournalKey,
                            session.SavedDocumentId,
                            session.SavedRevision,
                            session.Primitives.DocumentId,
                            session.Primitives.Revision);
                    }

                    await LoadLibraryAsync();
                    Changed?.Invoke();
                    return;
                }
            }

            var activeDescriptor = await ReadActiveDescriptorAsync();
            if (activeDescriptor is not null)
            {
                var restored = CreateSession(
                    activeDescriptor.Name,
                    activeDescriptor.StorageKey,
                    activeDescriptor.JournalKey,
                    activeDescriptor.Id);
                // 单会话 P6 恢复只自动应用显式保存内容；更新 journal 留给恢复横幅确认。
                await RestoreContentAsync(restored, preferJournal: false);
                _sessions.Clear();
                _sessions.Add(restored);
                _active = restored;
                _nextNumber = Math.Max(2, ParseDocumentNumber(restored.Name) + 1);
                Storage.SetActiveKey(restored.StorageKey);
                ConfigStore.SwitchDocument(restored.Primitives, restored.Config);
                ActiveProxy.Attach(restored.Primitives);
                ImportRollback.Clear();
                await Journal.SwitchSessionAsync(restored.JournalKey);
            }
            else
            {
                // 默认单会话没有 activeSession 描述时也必须恢复显式保存内容；
                // 较新的 journal 仍由 DocumentJournalService 的恢复横幅确认。
                await RestoreContentAsync(_active, preferJournal: false);
                ConfigStore.ReplaceConfig(_active.Config);
            }

            await LoadLibraryAsync();
            Changed?.Invoke();
        }
        finally
        {
            _switchGate.Release();
        }
    }

    public async Task<(bool Success, string? Error)> CreateNewAsync()
    {
        await _switchGate.WaitAsync();
        try
        {
            if (_sessions.Count >= MaxSessions)
            {
                return (false, $"At most {MaxSessions} documents can be open.");
            }

            var number = _nextNumber++;
            var id = Guid.NewGuid();
            var session = CreateSession(
                $"Document {number}",
                $"{DocumentStorage.StorageKey}.{id:N}",
                $"penelopa.journal.{id:N}",
                id);
            _sessions.Add(session);
            await ActivateCoreAsync(session);
            return (true, null);
        }
        finally
        {
            _switchGate.Release();
        }
    }

    public async Task<bool> ActivateAsync(Guid sessionId)
    {
        if (_active.Id == sessionId)
        {
            return true;
        }

        await _switchGate.WaitAsync();
        try
        {
            if (_active.Id == sessionId)
            {
                return true;
            }

            var session = _sessions.FirstOrDefault(candidate => candidate.Id == sessionId);
            if (session is null)
            {
                return false;
            }

            await ActivateCoreAsync(session);
            return true;
        }
        finally
        {
            _switchGate.Release();
        }
    }

    public async Task<(bool Success, string? Error)> OpenFromLibraryAsync(
        DocumentLibraryEntry entry)
    {
        if (entry is null)
        {
            return (false, "The document entry is invalid.");
        }

        var open = _sessions.FirstOrDefault(
            session => session.StorageKey == entry.StorageKey);
        if (open is not null)
        {
            return await ActivateAsync(open.Id)
                ? (true, null)
                : (false, "The document could not be activated.");
        }

        await _switchGate.WaitAsync();
        try
        {
            if (_sessions.Count >= MaxSessions)
            {
                return (false, $"At most {MaxSessions} documents can be open.");
            }

            if (entry.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(entry.Name)
                || !AreMatchingKeys(entry.StorageKey, entry.JournalKey))
            {
                return (false, "The document entry is invalid.");
            }

            var session = CreateSession(
                entry.Name,
                entry.StorageKey,
                entry.JournalKey,
                entry.Id);
            await RestoreContentAsync(session);
            _sessions.Add(session);
            _nextNumber = Math.Max(_nextNumber, ParseDocumentNumber(entry.Name) + 1);
            await ActivateCoreAsync(session);
            return (true, null);
        }
        finally
        {
            _switchGate.Release();
        }
    }

    public async Task<(bool Success, string? Error)> CloseAsync(Guid sessionId)
    {
        await _switchGate.WaitAsync();
        try
        {
            if (_sessions.Count <= 1)
            {
                return (false, "At least one document must stay open.");
            }

            var session = _sessions.FirstOrDefault(candidate => candidate.Id == sessionId);
            if (session is null)
            {
                return (false, "The document is not open.");
            }

            var wasActive = _active.Id == sessionId;
            if (wasActive)
            {
                await Journal.FlushAsync();
                await RecordClosedSessionAsync(session);
                _sessions.Remove(session);
                Journal.ForgetSession(session.JournalKey);
                var replacement = _sessions[0];
                await ActivateCoreAsync(replacement);
            }
            else
            {
                await RecordClosedSessionAsync(session);
                _sessions.Remove(session);
                Journal.ForgetSession(session.JournalKey);
                await PersistRegistryAsync();
                Changed?.Invoke();
            }

            return (true, null);
        }
        finally
        {
            _switchGate.Release();
        }
    }

    private async Task ActivateCoreAsync(DocumentSession target)
    {
        _active.Config = ConfigStore.Config;
        await Journal.FlushAsync();

        _active = target;
        Storage.SetActiveKey(target.StorageKey);
        ConfigStore.SwitchDocument(target.Primitives, target.Config);
        ActiveProxy.Attach(target.Primitives);
        ImportRollback.Clear();
        await Journal.SwitchSessionAsync(target.JournalKey);
        await PersistActiveAsync();
        await PersistRegistryAsync();
        Changed?.Invoke();
    }

    private DocumentSession CreateSession(
        string name,
        string storageKey,
        string journalKey,
        Guid? id = null)
        => new(
            id ?? Guid.NewGuid(),
            name,
            storageKey,
            journalKey,
            new PrimitiveService());

    private async Task RestoreContentAsync(
        DocumentSession session,
        bool preferJournal = true)
    {
        var journal = preferJournal
            ? await Journal.ReadSessionAsync(session.JournalKey)
            : null;
        var storage = new DocumentStorage(_js);
        storage.SetActiveKey(session.StorageKey);
        var saved = await storage.LoadAsync();
        var document = journal is not null
            && (saved.Document is null
                || journal.DocumentId != saved.Document.DocumentId
                || journal.Revision >= saved.Document.Revision)
                ? journal
                : saved.Document;
        session.SavedDocumentId = saved.Document?.DocumentId ?? Guid.Empty;
        session.SavedRevision = saved.Document?.Revision ?? -1;
        if (document is null)
        {
            return;
        }

        session.Primitives.ReplaceAll(document.Roots);
        session.Primitives.SetDocumentIdentity(document.DocumentId, document.Revision);
        session.Primitives.SetExtensions(document.Extensions);
        session.Primitives.SetSelectedRange(document.ResolveSelection());
        session.Config = document.Simulation ?? SimulationConfig.Empty;
    }

    /// <summary>会话是否有尚未保存到显式存储的内容/配置编辑。</summary>
    public bool HasUnsavedChanges(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Journal.HasPendingRecoveryFor(session.JournalKey))
        {
            return true;
        }

        return session.Primitives.GetAll().Count > 0
            && (session.Primitives.DocumentId != session.SavedDocumentId
                || session.Primitives.Revision != session.SavedRevision);
    }

    private void HandleLoaded(DocumentLoadedEvent loaded)
    {
        if (_disposed)
        {
            return;
        }

        var session = _sessions.FirstOrDefault(
            candidate => string.Equals(
                candidate.StorageKey,
                loaded.StorageKey,
                StringComparison.Ordinal));
        if (session is null)
        {
            return;
        }

        session.SavedDocumentId = loaded.DocumentId;
        session.SavedRevision = loaded.Revision;
    }

    private void HandleSaved(DocumentSavedEvent saved)
    {
        if (_disposed)
        {
            return;
        }

        var session = _sessions.FirstOrDefault(
            candidate => string.Equals(
                candidate.StorageKey,
                saved.StorageKey,
                StringComparison.Ordinal));
        if (session is null)
        {
            return;
        }

        session.SavedDocumentId = saved.DocumentId;
        session.SavedRevision = saved.Revision;

        if (!string.Equals(saved.StorageKey, _active.StorageKey, StringComparison.Ordinal))
        {
            // 保存可能在切换标签后完成；更新被保存会话自己的 journal 基线。
            _ = Journal.NotifySavedForSessionAsync(
                session.JournalKey,
                saved.DocumentId,
                saved.Revision,
                session.Primitives.DocumentId);
        }

        _ = RecordLibraryAsync(session);
    }

    /// <summary>
    /// 关闭会话时把非空且尚未保存的会话加入文档库；journal/存储键仍保留，
    /// 用户可从标签栏重新打开，避免关闭即永久丢失。
    /// </summary>
    private async Task RecordClosedSessionAsync(DocumentSession session)
    {
        if (_disposed)
        {
            return;
        }

        var hasContent = session.Primitives.GetAll().Count > 0;
        var unsaved = HasUnsavedChanges(session);
        var isEmpty = !hasContent && !unsaved;
        var now = DateTimeOffset.UtcNow.ToString("O");
        var existing = _library.FindIndex(
            entry => entry.StorageKey == session.StorageKey);
        if (existing >= 0)
        {
            if (!unsaved && !isEmpty && _library[existing].IsSaved)
            {
                return;
            }

            _library[existing] = _library[existing] with
            {
                SavedAt = now,
                IsSaved = !unsaved,
                IsEmpty = isEmpty,
            };
        }
        else
        {
            _library.Insert(0, new DocumentLibraryEntry(
                session.Id,
                session.Name,
                session.StorageKey,
                session.JournalKey,
                now)
            {
                IsSaved = !unsaved,
                IsEmpty = isEmpty,
            });
        }

        await WriteJsonAsync(LibraryKey, _library);
    }

    private async Task RecordLibraryAsync(DocumentSession session)
    {
        var entry = new DocumentLibraryEntry(
            session.Id,
            session.Name,
            session.StorageKey,
            session.JournalKey,
            DateTimeOffset.UtcNow.ToString("O"));
        var existing = _library.FindIndex(
            item => item.StorageKey == entry.StorageKey);
        if (existing >= 0)
        {
            _library[existing] = entry;
        }
        else
        {
            _library.Insert(0, entry);
        }

        await WriteJsonAsync(LibraryKey, _library);
        Changed?.Invoke();
    }

    private async Task LoadLibraryAsync()
    {
        var entries = await ReadJsonAsync<List<DocumentLibraryEntry>>(LibraryKey);
        _library.Clear();
        if (entries is null)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry is not null
                && entry.Id != Guid.Empty
                && !string.IsNullOrWhiteSpace(entry.Name)
                && AreMatchingKeys(entry.StorageKey, entry.JournalKey))
            {
                _library.Add(entry);
            }
        }
    }

    private async Task<ActiveSessionDescriptor?> ReadActiveDescriptorAsync()
    {
        var descriptor = await ReadJsonAsync<ActiveSessionDescriptor>(
            DocumentStorage.ActiveSessionKey);
        return descriptor is { } candidate && IsValidDescriptor(candidate)
            ? candidate
            : null;
    }

    private async Task PersistActiveAsync()
    {
        await WriteJsonAsync(
            DocumentStorage.ActiveSessionKey,
            new ActiveSessionDescriptor(
                _active.Id,
                _active.Name,
                _active.StorageKey,
                _active.JournalKey));
    }

    private async Task PersistRegistryAsync()
    {
        if (_sessions.Count <= 1)
        {
            await WriteJsonAsync<string?>(SessionsKey, null);
            return;
        }

        await WriteJsonAsync(
            SessionsKey,
            new SessionRegistry(
                _active.Id,
                _sessions.Select(session => new SessionDescriptor(
                    session.Id,
                    session.Name,
                    session.StorageKey,
                    session.JournalKey)).ToList()));
    }

    private async Task<T?> ReadJsonAsync<T>(string key)
    {
        try
        {
            var read = await _js.InvokeAsync<StorageReadResult>(
                "penelopa.readStorage",
                key);
            return read is { Ok: true, Value: not null }
                && !string.IsNullOrWhiteSpace(read.Value)
                ? JsonSerializer.Deserialize<T>(read.Value)
                : default;
        }
        catch (Exception ex) when (ex is JsonException or JSException
            or JSDisconnectedException or InvalidOperationException)
        {
            Console.Error.WriteLine($"[Penelopa] DocumentSessionManager read failed: {ex}");
            return default;
        }
    }

    private async Task WriteJsonAsync<T>(string key, T value)
    {
        try
        {
            var json = value is null ? string.Empty : JsonSerializer.Serialize(value);
            var result = await _js.InvokeAsync<StorageWriteResult>(
                "penelopa.writeStorage",
                key,
                json);
            if (result is not null && !result.Ok)
            {
                Console.Error.WriteLine(
                    $"[Penelopa] DocumentSessionManager write failed: {result.Error}");
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException
            or InvalidOperationException)
        {
            Console.Error.WriteLine($"[Penelopa] DocumentSessionManager write failed: {ex}");
        }
    }

    private static bool IsValidDescriptor(SessionDescriptor descriptor)
        => descriptor.Id != Guid.Empty
            && !string.IsNullOrWhiteSpace(descriptor.Name)
            && AreMatchingKeys(descriptor.StorageKey, descriptor.JournalKey);

    private static bool IsValidDescriptor(ActiveSessionDescriptor descriptor)
        => descriptor.Id != Guid.Empty
            && !string.IsNullOrWhiteSpace(descriptor.Name)
            && AreMatchingKeys(descriptor.StorageKey, descriptor.JournalKey);

    private static bool AreMatchingKeys(string storageKey, string journalKey)
    {
        if (storageKey == DocumentStorage.StorageKey)
        {
            return journalKey == "penelopa.document";
        }

        const string journalPrefix = "penelopa.journal.";
        if (!storageKey.StartsWith(DocumentStorage.StorageKey + ".", StringComparison.Ordinal)
            || !journalKey.StartsWith(journalPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var storageSuffix = storageKey[(DocumentStorage.StorageKey.Length + 1)..];
        var journalSuffix = journalKey[journalPrefix.Length..];
        return storageSuffix.Length == 32
            && journalSuffix.Length == 32
            && storageSuffix.All(char.IsAsciiHexDigit)
            && string.Equals(storageSuffix, journalSuffix, StringComparison.Ordinal);
    }

    private static int ParseDocumentNumber(string name)
    {
        var index = name.LastIndexOf(' ');
        return index >= 0 && int.TryParse(name[(index + 1)..], out var number)
            ? number
            : 1;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Storage.Saved -= HandleSaved;
        Storage.Loaded -= HandleLoaded;
        Journal.Dispose();
        ImportRollback.Dispose();
        _switchGate.Dispose();
    }
}
