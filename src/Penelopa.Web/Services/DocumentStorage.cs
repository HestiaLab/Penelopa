using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.JSInterop;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

public sealed record DocumentSaveResult(bool Success, string? Error);

/// <summary>一次成功保存的实际身份；订阅者不得用当前活动会话替代它。</summary>
public sealed record DocumentSavedEvent(string StorageKey, Guid DocumentId, long Revision);

/// <summary>一次成功读取到的显式保存文档身份，用于会话的“已保存基线”。</summary>
public sealed record DocumentLoadedEvent(string StorageKey, Guid DocumentId, long Revision);

public sealed record DocumentLoadResult(DeserializeResult? Document, string? Error);

/// <summary>JS localStorage 读取结果；失败不会以异常形式穿过互操作边界。</summary>
public sealed record StorageReadResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>JS localStorage 写入结果；失败不会以异常形式穿过互操作边界。</summary>
public sealed record StorageWriteResult(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("error")] string? Error);

/// <summary>
/// 编辑页与仿真页共用的 localStorage 持久化边界：序列化 v3 文档、
/// 备份上一次负载，并把 JS 异常转换为可显示的结果。
/// </summary>
public sealed class DocumentStorage
{
    public const string StorageKey = "penelopa.document";
    public const string BackupKey = "penelopa.document.backup";
    public const string ActiveSessionKey = "penelopa.activeSession";

    private readonly IJSRuntime _js;

    public DocumentStorage(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        _js = js;
    }

    /// <summary>当前活动会话的 localStorage 键；默认会话保持 legacy 键。</summary>
    public string ActiveStorageKey { get; private set; } = StorageKey;

    public string ActiveBackupKey => ActiveStorageKey + ".backup";

    public void SetActiveKey(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        ActiveStorageKey = storageKey;
    }

    /// <summary>仅在一次完整的 localStorage 写入成功后触发。</summary>
    public event Action<DocumentSavedEvent>? Saved;

    /// <summary>显式保存文档成功解析后触发；会话据此更新已保存基线。</summary>
    public event Action<DocumentLoadedEvent>? Loaded;

    public async Task<DocumentSaveResult> SaveAsync(
        IReadOnlyList<Primitive> roots,
        IEnumerable<Primitive> selection,
        SimulationConfig? simulation = null,
        Guid documentId = default,
        long revision = 0,
        IReadOnlyDictionary<string, JsonElement>? extensions = null)
    {
        // 键与载荷必须在同一个同步片段内确定；之后任何 await 期间切换会话都不能改变写入目标。
        var storageKey = ActiveStorageKey;
        var backupKey = storageKey + ".backup";
        var resolvedDocumentId = documentId == Guid.Empty ? Guid.NewGuid() : documentId;
        try
        {
            var json = DocumentSerializer.Serialize(
                roots,
                selection,
                simulation,
                resolvedDocumentId,
                revision,
                extensions);
            if (Encoding.UTF8.GetByteCount(json) > DocumentSerializer.MaxDocumentBytes)
            {
                return SaveFailure(
                    $"The document is larger than {DocumentSerializer.MaxDocumentBytes / (1024 * 1024)} MB and cannot be saved.");
            }

            var previous = await ReadStorageAsync(storageKey);
            if (!previous.Success)
            {
                return SaveFailure(previous.Error);
            }

            if (!string.IsNullOrEmpty(previous.Value))
            {
                var backup = await WriteStorageAsync(backupKey, previous.Value);
                if (!backup.Success)
                {
                    return SaveFailure(backup.Error);
                }
            }

            var write = await WriteStorageAsync(storageKey, json);
            if (!write.Success)
            {
                return SaveFailure(write.Error);
            }

            Saved?.Invoke(new DocumentSavedEvent(
                storageKey,
                resolvedDocumentId,
                revision));
            return new DocumentSaveResult(true, null);
        }
        catch (Exception ex)
        {
            LogFullException("SaveAsync", ex);
            return new DocumentSaveResult(
                false,
                $"The document could not be saved: {DescribeFailure(ex)}");
        }
    }

    public async Task<DocumentLoadResult> LoadAsync()
    {
        var storageKey = ActiveStorageKey;
        try
        {
            var read = await ReadStorageAsync(storageKey);
            if (!read.Success)
            {
                return new DocumentLoadResult(
                    null,
                    $"The saved document could not be read: {DescribeFailure(read.Error)}");
            }

            var json = read.Value;
            if (string.IsNullOrWhiteSpace(json))
            {
                return new DocumentLoadResult(null, "No saved document was found.");
            }

            var result = DocumentSerializer.Deserialize(
                json,
                out var rejectedVersion,
                out var unknownTypeId);
            if (result is null)
            {
                if (unknownTypeId is not null)
                {
                    var display = unknownTypeId.Length <= 120
                        ? unknownTypeId
                        : unknownTypeId[..120];
                    return new DocumentLoadResult(
                        null,
                        $"The saved document uses an unknown primitive type '{display}'. It was not loaded.");
                }

                return new DocumentLoadResult(
                    null,
                    DescribeVersionRejection(rejectedVersion)
                        ?? "The saved document is invalid or unsupported.");
            }

            Loaded?.Invoke(new DocumentLoadedEvent(
                storageKey,
                result.DocumentId,
                result.Revision));
            return new DocumentLoadResult(result, null);
        }
        catch (Exception ex)
        {
            // 最后边界：任何未处理异常都不能击穿渲染器；当前文档保持不变。
            LogFullException("LoadAsync", ex);
            return new DocumentLoadResult(
                null,
                $"The saved document could not be read: {DescribeFailure(ex)}");
        }
    }

    private static string? DescribeVersionRejection(int? rejectedVersion)
    {
        return rejectedVersion switch
        {
            0 => "The saved document has no supported version. Only version 3 documents are supported.",
            1 => "The saved document uses document version 1, which is no longer supported. It was not loaded; export it with an older release before upgrading.",
            2 => "The saved document uses document version 2, which is no longer supported. It was not loaded; export it with an older release before upgrading.",
            > 3 => $"The saved document uses version {rejectedVersion}, which is newer than this app supports. It was not loaded.",
            _ => null,
        };
    }

    private async Task<(bool Success, string? Value, string? Error)> ReadStorageAsync(
        string key)
    {
        var result = await _js.InvokeAsync<StorageReadResult>(
            "penelopa.readStorage",
            key);
        return result is null
            ? (false, null, "Storage read returned no result.")
            : (result.Ok, result.Value, result.Error);
    }

    private async Task<(bool Success, string? Error)> WriteStorageAsync(
        string key,
        string value)
    {
        var result = await _js.InvokeAsync<StorageWriteResult>(
            "penelopa.writeStorage",
            key,
            value);
        return result is null
            ? (false, "Storage write returned no result.")
            : (result.Ok, result.Error);
    }

    private static DocumentSaveResult SaveFailure(string? error)
        => new(false, $"The document could not be saved: {DescribeFailure(error)}");

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

        if (firstLine.StartsWith("Error: ", StringComparison.Ordinal))
        {
            firstLine = firstLine["Error: ".Length..];
        }

        firstLine = firstLine.Trim();
        return firstLine.Length == 0 ? "unknown error" : firstLine;
    }

    private static void LogFullException(string operation, Exception exception)
        => Console.Error.WriteLine($"[Penelopa] DocumentStorage.{operation} failed: {exception}");
}
