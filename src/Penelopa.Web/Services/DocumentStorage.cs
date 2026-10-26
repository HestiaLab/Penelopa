using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

public sealed record DocumentSaveResult(bool Success, string? Error);

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
/// 编辑页与仿真页共用的 localStorage 持久化边界：序列化 v2 文档、
/// 备份上一次负载，并把 JS 异常转换为可显示的结果。
/// </summary>
public sealed class DocumentStorage
{
    public const string StorageKey = "penelopa.document";
    public const string BackupKey = "penelopa.document.backup";

    private readonly IJSRuntime _js;

    public DocumentStorage(IJSRuntime js)
    {
        ArgumentNullException.ThrowIfNull(js);
        _js = js;
    }

    public async Task<DocumentSaveResult> SaveAsync(
        IReadOnlyList<Primitive> roots,
        IEnumerable<Primitive> selection,
        SimulationConfig? simulation = null)
    {
        try
        {
            var json = DocumentSerializer.Serialize(roots, selection, simulation);
            var previous = await ReadStorageAsync(StorageKey);
            if (!previous.Success)
            {
                return SaveFailure(previous.Error);
            }

            if (!string.IsNullOrEmpty(previous.Value))
            {
                var backup = await WriteStorageAsync(BackupKey, previous.Value);
                if (!backup.Success)
                {
                    return SaveFailure(backup.Error);
                }
            }

            var write = await WriteStorageAsync(StorageKey, json);
            return write.Success
                ? new DocumentSaveResult(true, null)
                : SaveFailure(write.Error);
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
        try
        {
            var read = await ReadStorageAsync(StorageKey);
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

            var result = DocumentSerializer.Deserialize(json);
            if (result is null)
            {
                return new DocumentLoadResult(
                    null,
                    "The saved document is invalid or unsupported.");
            }

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
