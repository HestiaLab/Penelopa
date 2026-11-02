using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

/// <summary>IndexedDB 中的自动保存记录；不含 undo 栈或 overlay。</summary>
public sealed record JournalEntryDto(
    [property: JsonPropertyName("documentId")] Guid DocumentId,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("savedRevision")] long SavedRevision,
    [property: JsonPropertyName("savedAt")] string SavedAt,
    [property: JsonPropertyName("json")] string Json,
    [property: JsonPropertyName("hash")] string Hash);

public sealed record JournalReadResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("value")] JournalEntryDto? Value,
    [property: JsonPropertyName("error")] string? Error);

public sealed record JournalWriteResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>
/// 持久编辑的自动保存边界：防抖写入 IndexedDB journal，启动时与显式保存的 revision
/// 比较并暴露待恢复文档。恢复只恢复文档状态，不恢复撤销历史。
/// </summary>
public sealed class DocumentJournalService : IDisposable
{
    private const int DebounceMilliseconds = 1_500;
    private const int MaxErrorLength = 240;

    private readonly IJSRuntime _js;
    private readonly IPrimitiveService _primitives;
    private readonly SimulationConfigStore _config;
    private readonly DocumentStorage _storage;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly Dictionary<string, PendingRecoveryState> _pendingByKey =
        new(StringComparer.Ordinal);
    private string _journalKey;

    private CancellationTokenSource? _debounce;
    private Task? _initializeTask;
    private bool _ready;
    private bool _disposed;
    private Guid? _savedDocumentId;
    private long _savedRevision = -1;
    private PendingRecoveryState? _currentPending;
    private bool _pendingDocumentDiffers;

    public DocumentJournalService(
        IJSRuntime js,
        IPrimitiveService primitives,
        SimulationConfigStore config,
        DocumentStorage storage,
        string journalKey = "penelopa.document")
    {
        _js = js;
        _primitives = primitives;
        _config = config;
        _storage = storage;
        _journalKey = journalKey;
    }

    public string JournalKey => _journalKey;

    /// <summary>journal 状态变化（待恢复、恢复完成、保存基线更新）。</summary>
    public event Action? StateChanged;

    /// <summary>启动时发现的高于显式保存 revision 的 journal 文档。</summary>
    public DeserializeResult? PendingRecovery => _currentPending?.Document;

    public JournalEntryDto? PendingEntry => _currentPending?.Entry;

    /// <summary>待恢复 journal 是否属于与显式保存文档不同的 DocumentId。</summary>
    public bool PendingDocumentDiffers => _pendingDocumentDiffers;

    /// <summary>读取/写入 journal 的可显示错误；自动保存失败不阻断编辑。</summary>
    public string? LastError { get; private set; }

    public bool IsInitialized => _ready;

    /// <summary>指定会话是否有尚未解决的恢复记录。</summary>
    public bool HasPendingRecoveryFor(string journalKey)
        => !string.IsNullOrWhiteSpace(journalKey)
            && _pendingByKey.ContainsKey(journalKey);

    public Task InitializeAsync()
        => _disposed ? Task.CompletedTask : _initializeTask ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        _primitives.Changed += OnDocumentChanged;
        _config.Changed += OnConfigChanged;
        _storage.Saved += OnSaved;

        try
        {
            var saved = await _storage.LoadAsync();
            if (saved.Document is not null)
            {
                SetSavedBaseline(saved.Document.DocumentId, saved.Document.Revision);
            }

            var read = await ReadJournalAsync(_journalKey);
            if (!read.Ok || read.Value is null)
            {
                return;
            }

            var entry = read.Value;
            if (!TryValidateEntry(entry, out var document))
            {
                await ClearJournalInternalAsync(_journalKey);
                ClearCurrentPending();
                return;
            }

            // 空会话切换时也会写出现 revision 0 的空 journal；不要为它弹恢复提示。
            if (_savedDocumentId is null
                && entry.Revision == 0
                && document.Roots.Count == 0
                && (document.Simulation?.Tracks.Count ?? 0) == 0
                && document.SelectionIds.Count == 0)
            {
                await ClearJournalInternalAsync(_journalKey);
                ClearCurrentPending();
                return;
            }

            // journal 属于另一份文档（导入/样例/换文档）时也必须可恢复；
            // 显式 Save/Load 会清理过期 journal，因此这里不能静默丢弃。
            if (_savedDocumentId is { } savedId && entry.DocumentId != savedId)
            {
                SetPending(entry, document, documentDiffers: true);
                return;
            }

            if (_savedDocumentId is not null && entry.Revision <= _savedRevision)
            {
                await ClearJournalInternalAsync(_journalKey);
                ClearCurrentPending();
                return;
            }

            SetPending(entry, document, documentDiffers: false);
        }
        finally
        {
            _ready = true;
        }
    }

    /// <summary>
    /// 多文档恢复时为非活动会话预判 journal；不改变当前活动 journal，
    /// 切到该会话时由 SwitchSessionAsync 恢复横幅。
    /// </summary>
    public async Task PrepareSessionRecoveryAsync(
        string journalKey,
        Guid savedDocumentId,
        long savedRevision,
        Guid memoryDocumentId,
        long memoryRevision)
    {
        if (string.IsNullOrWhiteSpace(journalKey)
            || _pendingByKey.ContainsKey(journalKey))
        {
            return;
        }

        var read = await ReadJournalAsync(journalKey);
        if (!read.Ok || read.Value is null)
        {
            return;
        }

        var entry = read.Value;
        if (!TryValidateEntry(entry, out var document))
        {
            await ClearJournalInternalAsync(journalKey);
            ClearPendingFor(journalKey);
            return;
        }

        if (IsTrivialJournal(entry, document, savedDocumentId))
        {
            await ClearJournalInternalAsync(journalKey);
            ClearPendingFor(journalKey);
            return;
        }

        var documentDiffers = savedDocumentId != Guid.Empty
            && entry.DocumentId != savedDocumentId;
        if (savedDocumentId != Guid.Empty
            && !documentDiffers
            && entry.Revision <= savedRevision)
        {
            await ClearJournalInternalAsync(journalKey);
            ClearPendingFor(journalKey);
            return;
        }

        SetPendingForKey(
            journalKey,
            entry,
            document,
            documentDiffers,
            memoryDocumentId,
            memoryRevision);
    }

    private static bool IsTrivialJournal(
        JournalEntryDto entry,
        DeserializeResult document,
        Guid savedDocumentId)
        => savedDocumentId == Guid.Empty
            && entry.Revision == 0
            && document.Roots.Count == 0
            && (document.Simulation?.Tracks.Count ?? 0) == 0
            && document.SelectionIds.Count == 0;

    /// <summary>编辑器从 localStorage 确认加载后调用：更新基线并清除旧 journal。</summary>
    public async Task NotifyLocalLoadAsync(Guid documentId, long revision)
    {
        var journalKey = _journalKey;
        SetSavedBaseline(documentId, revision);
        ClearPendingFor(journalKey);
        LastError = null;
        CancelDebounce();
        await ClearJournalForKeySerializedAsync(journalKey);
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 导入或样例整体替换文档后调用：新文档成为 journal 基线并立即写入，
    /// 同时解除旧文档的待恢复状态，避免后续 Recover 覆盖刚导入的内容。
    /// </summary>
    public async Task NotifyDocumentReplacedAsync(Guid documentId, long revision)
    {
        var journalKey = _journalKey;
        SetSavedBaseline(documentId, revision);
        var entry = CaptureJournalEntry();
        ClearPendingFor(journalKey);
        LastError = null;
        CancelDebounce();
        await ClearJournalForKeySerializedAsync(journalKey);
        await WriteJournalForKeySerializedAsync(entry, journalKey);
        StateChanged?.Invoke();
    }

    /// <summary>关闭会话时丢弃其内存中的 pending 状态；journal 本身保留在 IndexedDB。</summary>
    public void ForgetSession(string journalKey)
    {
        if (string.IsNullOrWhiteSpace(journalKey))
        {
            return;
        }

        _pendingByKey.Remove(journalKey);
        if (string.Equals(journalKey, _journalKey, StringComparison.Ordinal))
        {
            ApplyCurrentPending(null);
        }
    }

    /// <summary>只读取并校验指定会话的 journal，不改变当前活动状态。</summary>
    public async Task<DeserializeResult?> ReadSessionAsync(string journalKey)
    {
        try
        {
            var result = await _js.InvokeAsync<JournalReadResult>(
                "penelopa.readJournal",
                journalKey);
            return result is { Ok: true, Value: not null }
                && TryValidateEntry(result.Value, out var document)
                ? document
                : null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[Penelopa] DocumentJournalService session read failed: {ex}");
            return null;
        }
    }

    /// <summary>切换到另一个文档会话的 journal 键，并重新读取该会话的显式保存基线。</summary>
    public async Task SwitchSessionAsync(string journalKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalKey);
        CancelDebounce();
        _journalKey = journalKey;
        _currentPending = _pendingByKey.TryGetValue(journalKey, out var pending)
            ? pending
            : null;
        _pendingDocumentDiffers = _currentPending?.DocumentDiffers ?? false;
        LastError = null;

        var saved = await _storage.LoadAsync();
        if (saved.Document is not null)
        {
            SetSavedBaseline(saved.Document.DocumentId, saved.Document.Revision);
        }
        else
        {
            _savedDocumentId = null;
            _savedRevision = -1;
        }

        StateChanged?.Invoke();
    }

    /// <summary>立即写入当前会话的 journal，用于切换/关闭前的持久化。</summary>
    public async Task FlushAsync()
    {
        if (!_ready || _disposed || _currentPending is not null)
        {
            return;
        }

        CancelDebounce();
        await WriteCurrentDocumentAsync(CancellationToken.None);
    }

    /// <summary>恢复成功后解除待恢复状态；journal 保留到下一次显式保存或自动保存。</summary>
    public void ResolveRecovery()
    {
        if (_currentPending is null)
        {
            return;
        }

        ClearCurrentPending();
        LastError = null;
        StateChanged?.Invoke();
    }

    public async Task DiscardRecoveryAsync()
    {
        var journalKey = _journalKey;
        var pending = _currentPending;
        // pending 显示期间用户可能继续编辑；这些编辑不在 journal 中，丢弃 journal 时必须保留。
        var hasNewerInMemoryState = pending is not null
            && (_primitives.DocumentId != pending.MemoryDocumentId
                || _primitives.Revision != pending.MemoryRevision);
        var currentEntry = hasNewerInMemoryState ? CaptureJournalEntry() : null;

        ClearPendingFor(journalKey);
        LastError = null;
        await ClearJournalForKeySerializedAsync(journalKey);
        if (currentEntry is not null)
        {
            await WriteJournalForKeySerializedAsync(currentEntry, journalKey);
        }

        StateChanged?.Invoke();
    }

    private void SetSavedBaseline(Guid documentId, long revision)
    {
        _savedDocumentId = documentId;
        _savedRevision = revision;
    }

    private void SetPending(
        JournalEntryDto entry,
        DeserializeResult document,
        bool documentDiffers)
        => SetPendingForKey(
            _journalKey,
            entry,
            document,
            documentDiffers,
            _primitives.DocumentId,
            _primitives.Revision);

    private void SetPendingForKey(
        string journalKey,
        JournalEntryDto entry,
        DeserializeResult document,
        bool documentDiffers,
        Guid memoryDocumentId,
        long memoryRevision)
    {
        var state = new PendingRecoveryState(
            entry,
            document,
            documentDiffers,
            memoryDocumentId,
            memoryRevision);
        _pendingByKey[journalKey] = state;
        if (string.Equals(journalKey, _journalKey, StringComparison.Ordinal))
        {
            ApplyCurrentPending(state);
            StateChanged?.Invoke();
        }
    }

    private void ApplyCurrentPending(PendingRecoveryState? state)
    {
        _currentPending = state;
        _pendingDocumentDiffers = state?.DocumentDiffers ?? false;
    }

    private void ClearCurrentPending()
    {
        _pendingByKey.Remove(_journalKey);
        ApplyCurrentPending(null);
    }

    private void ClearPendingFor(string journalKey)
    {
        _pendingByKey.Remove(journalKey);
        if (string.Equals(journalKey, _journalKey, StringComparison.Ordinal))
        {
            ApplyCurrentPending(null);
        }
    }

    private void OnDocumentChanged(DocumentChange change)
    {
        if ((change.Kind & (DocumentChangeKind.Structure | DocumentChangeKind.Geometry)) != 0)
        {
            ScheduleAutoSave();
        }
    }

    private void OnConfigChanged() => ScheduleAutoSave();

    private void OnSaved(DocumentSavedEvent saved)
    {
        // 保存可能在切换标签后完成；只有当前活动存储键才更新当前 journal 基线。
        if (!string.Equals(saved.StorageKey, _storage.ActiveStorageKey, StringComparison.Ordinal))
        {
            return;
        }

        var journalKey = _journalKey;
        var currentDocumentId = _primitives.DocumentId;
        SetSavedBaseline(saved.DocumentId, saved.Revision);
        ClearCurrentPending();
        LastError = null;

        var dirty = currentDocumentId != saved.DocumentId
            || _primitives.Revision > saved.Revision;
        if (dirty)
        {
            // 保存期间仍有新编辑：不要取消它们的自动保存。
            ScheduleAutoSave();
        }
        else
        {
            CancelDebounce();
        }

        _ = MarkSavedAsync(
            saved.DocumentId,
            saved.Revision,
            journalKey,
            currentDocumentId);
        StateChanged?.Invoke();
    }

    /// <summary>显式保存了一个非活动会话后，更新该会话自己的 journal 基线。</summary>
    public async Task NotifySavedForSessionAsync(
        string journalKey,
        Guid documentId,
        long revision,
        Guid currentDocumentId)
    {
        if (string.IsNullOrWhiteSpace(journalKey))
        {
            return;
        }

        try
        {
            await _writeGate.WaitAsync();
            try
            {
                await ApplySavedBaselineToJournalAsync(
                    journalKey,
                    documentId,
                    revision,
                    currentDocumentId);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[Penelopa] DocumentJournalService session mark-saved failed: {ex}");
        }
    }

    private void ScheduleAutoSave()
    {
        if (!_ready || _disposed || _currentPending is not null)
        {
            return;
        }

        CancelDebounce();
        _debounce = new CancellationTokenSource();
        _ = AutoSaveAfterDelayAsync(_debounce.Token);
    }

    private void CancelDebounce()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = null;
    }

    private async Task AutoSaveAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await WriteCurrentDocumentAsync(token);
    }

    private async Task WriteCurrentDocumentAsync(CancellationToken token)
    {
        try
        {
            await _writeGate.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (token.IsCancellationRequested || _disposed || _currentPending is not null)
            {
                return;
            }

            var entry = CaptureJournalEntry();
            if (Encoding.UTF8.GetByteCount(entry.Json) > DocumentSerializer.MaxDocumentBytes)
            {
                SetError("Autosave skipped: the document is larger than 4 MB.");
                return;
            }

            await WriteJournalAsync(entry, _journalKey);
        }
        catch (Exception ex)
        {
            SetError($"Autosave failed: {DescribeFailure(ex)}");
            Console.Error.WriteLine($"[Penelopa] DocumentJournalService autosave failed: {ex}");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private JournalEntryDto CaptureJournalEntry()
    {
        var documentId = _primitives.DocumentId;
        var revision = _primitives.Revision;
        var json = DocumentSerializer.Serialize(
            _primitives.GetAll(),
            _primitives.GetSelection(),
            _config.ExportActive(),
            documentId,
            revision,
            _primitives.Extensions);
        return CreateEntry(json, documentId, revision);
    }

    private JournalEntryDto CreateEntry(string json, Guid documentId, long revision)
    {
        var savedRevision = _savedDocumentId == documentId ? _savedRevision : -1;
        return new JournalEntryDto(
            documentId,
            revision,
            savedRevision,
            DateTimeOffset.UtcNow.ToString("O"),
            json,
            ComputeHash(json));
    }

    private async Task MarkSavedAsync(
        Guid documentId,
        long revision,
        string journalKey,
        Guid currentDocumentId)
    {
        try
        {
            await _writeGate.WaitAsync();
            try
            {
                await ApplySavedBaselineToJournalAsync(
                    journalKey,
                    documentId,
                    revision,
                    currentDocumentId);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Penelopa] DocumentJournalService mark-saved failed: {ex}");
        }
    }

    private async Task ApplySavedBaselineToJournalAsync(
        string journalKey,
        Guid documentId,
        long revision,
        Guid currentDocumentId)
    {
        var read = await ReadJournalAsync(journalKey);
        if (!read.Ok || read.Value is null)
        {
            return;
        }

        if (read.Value.DocumentId != documentId)
        {
            // 不同 DocumentId 的 journal 只有在它仍代表当前内存文档时才保留（导入/换文档）。
            // 否则它属于过期文档，显式保存后必须清理，避免下次启动误恢复。
            if (read.Value.DocumentId != currentDocumentId)
            {
                await ClearJournalInternalAsync(journalKey);
                ClearPendingFor(journalKey);
            }

            return;
        }

        if (read.Value.Revision <= revision)
        {
            await ClearJournalInternalAsync(journalKey);
            ClearPendingFor(journalKey);
            return;
        }

        await WriteJournalAsync(
            read.Value with { SavedRevision = revision },
            journalKey);
    }

    private async Task<JournalReadResult> ReadJournalAsync(string journalKey)
    {
        try
        {
            var result = await _js.InvokeAsync<JournalReadResult>("penelopa.readJournal",
                journalKey);
            if (result is null)
            {
                SetError("The recovery journal could not be read.");
                return new JournalReadResult(false, null, "No result.");
            }

            if (!result.Ok)
            {
                SetError($"The recovery journal could not be read: {DescribeFailure(result.Error)}");
            }

            return result;
        }
        catch (Exception ex)
        {
            SetError($"The recovery journal could not be read: {DescribeFailure(ex)}");
            Console.Error.WriteLine($"[Penelopa] DocumentJournalService read failed: {ex}");
            return new JournalReadResult(false, null, ex.Message);
        }
    }

    private async Task WriteJournalAsync(JournalEntryDto entry, string journalKey)
    {
        try
        {
            var result = await _js.InvokeAsync<JournalWriteResult>(
                "penelopa.writeJournal",
                entry,
                journalKey);
            if (result is not null && !result.Ok)
            {
                SetError($"Autosave failed: {DescribeFailure(result.Error)}");
            }
        }
        catch (Exception ex)
        {
            SetError($"Autosave failed: {DescribeFailure(ex)}");
            Console.Error.WriteLine($"[Penelopa] DocumentJournalService write failed: {ex}");
        }
    }

    private async Task ClearJournalInternalAsync(string journalKey)
    {
        try
        {
            var result = await _js.InvokeAsync<JournalWriteResult>("penelopa.clearJournal",
                journalKey);
            if (result is not null && !result.Ok)
            {
                SetError($"The recovery journal could not be cleared: {DescribeFailure(result.Error)}");
            }
        }
        catch (Exception ex)
        {
            SetError($"The recovery journal could not be cleared: {DescribeFailure(ex)}");
            Console.Error.WriteLine($"[Penelopa] DocumentJournalService clear failed: {ex}");
        }
    }

    private async Task ClearJournalForKeySerializedAsync(string journalKey)
    {
        await _writeGate.WaitAsync();
        try
        {
            await ClearJournalInternalAsync(journalKey);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task WriteJournalForKeySerializedAsync(
        JournalEntryDto entry,
        string journalKey)
    {
        await _writeGate.WaitAsync();
        try
        {
            await WriteJournalAsync(entry, journalKey);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private sealed record PendingRecoveryState(
        JournalEntryDto Entry,
        DeserializeResult Document,
        bool DocumentDiffers,
        Guid MemoryDocumentId,
        long MemoryRevision);

    private static bool TryValidateEntry(
        JournalEntryDto entry,
        out DeserializeResult document)
    {
        document = null!;
        if (entry.DocumentId == Guid.Empty || string.IsNullOrWhiteSpace(entry.Json))
        {
            return false;
        }

        var expectedHash = ComputeHash(entry.Json);
        if (!string.Equals(expectedHash, entry.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        DeserializeResult? parsed;
        try
        {
            parsed = DocumentSerializer.Deserialize(entry.Json);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        if (parsed is null || parsed.DocumentId != entry.DocumentId)
        {
            return false;
        }

        document = parsed;
        return true;
    }
    private static string ComputeHash(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();

    private void SetError(string message)
    {
        LastError = message.Length <= MaxErrorLength
            ? message
            : message[..MaxErrorLength];
    }

    private static string DescribeFailure(Exception exception)
        => DescribeFailure(exception.Message);

    private static string DescribeFailure(string? message)
    {
        var firstLine = (message ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        var stackMarker = firstLine.IndexOf(" at ", StringComparison.Ordinal);
        if (stackMarker > 0)
        {
            firstLine = firstLine[..stackMarker];
        }

        firstLine = firstLine.Trim();
        return firstLine.Length == 0 ? "unknown error" : firstLine;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _primitives.Changed -= OnDocumentChanged;
        _config.Changed -= OnConfigChanged;
        _storage.Saved -= OnSaved;
        CancelDebounce();
        _writeGate.Dispose();
    }
}
