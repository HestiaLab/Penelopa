using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Penelopa.Web.Services;

/// <summary>云端文档快照；ETag 用于下一次写入的 If-Match。</summary>
public sealed record CloudDocumentSnapshot(
    [property: JsonPropertyName("json")] string Json,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("etag")] string? ETag);

public sealed record CloudGetResult(
    bool Success,
    CloudDocumentSnapshot? Document,
    string? Error);

public enum CloudSaveStatus
{
    Saved,
    Conflict,
    Failed,
}

/// <summary>保存结果；Conflict 携带服务端的最新快照，由用户选择处理方式。</summary>
public sealed record CloudSaveResult(
    CloudSaveStatus Status,
    long? Revision = null,
    string? ETag = null,
    CloudDocumentSnapshot? Remote = null,
    string? Error = null);

public sealed record CloudCheckpointResult(
    bool Success,
    string? CheckpointId,
    string? Error);

/// <summary>
/// 云端文档契约（ADR-001 3.7.3）。本期不实现后端；HttpCloudDocumentGateway
/// 按 REST 契约工作，测试以本地 handler 验证请求与冲突解析。
/// </summary>
public interface ICloudDocumentGateway
{
    Task<CloudGetResult> GetAsync(
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<CloudSaveResult> SaveAsync(
        Guid documentId,
        string json,
        long baseRevision,
        string? etag,
        CancellationToken cancellationToken = default);

    Task<CloudCheckpointResult> CreateCheckpointAsync(
        Guid documentId,
        string name,
        string json,
        long revision,
        CancellationToken cancellationToken = default);
}

public sealed class HttpCloudDocumentGateway : ICloudDocumentGateway
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;

    public HttpCloudDocumentGateway(HttpClient http)
    {
        _http = http;
    }

    public async Task<CloudGetResult> GetAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(
                $"documents/{documentId}",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CloudGetResult(
                    false,
                    null,
                    $"Cloud load failed ({(int)response.StatusCode}).");
            }

            var snapshot = await ReadSnapshotAsync(response, cancellationToken);
            return snapshot is null
                ? new CloudGetResult(false, null, "Cloud load returned an invalid document.")
                : new CloudGetResult(true, snapshot, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or JsonException)
        {
            return new CloudGetResult(false, null, $"Cloud load failed: {Describe(ex)}");
        }
    }

    public async Task<CloudSaveResult> SaveAsync(
        Guid documentId,
        string json,
        long baseRevision,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Put,
                $"documents/{documentId}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation(
                "X-Penelopa-Base-Revision",
                baseRevision.ToString());
            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var remote = await ReadSnapshotAsync(response, cancellationToken);
                return remote is null
                    ? new CloudSaveResult(
                        CloudSaveStatus.Failed,
                        Error: "Cloud save conflicted, but the remote document could not be read.")
                    : new CloudSaveResult(CloudSaveStatus.Conflict, Remote: remote);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new CloudSaveResult(
                    CloudSaveStatus.Failed,
                    Error: $"Cloud save failed ({(int)response.StatusCode}).");
            }

            var saved = await ReadSaveResponseAsync(response, cancellationToken);
            return saved;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or JsonException or FormatException)
        {
            return new CloudSaveResult(
                CloudSaveStatus.Failed,
                Error: $"Cloud save failed: {Describe(ex)}");
        }
    }

    public async Task<CloudCheckpointResult> CreateCheckpointAsync(
        Guid documentId,
        string name,
        string json,
        long revision,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                name,
                json,
                revision,
            }, Options);
            using var response = await _http.PostAsync(
                $"documents/{documentId}/checkpoints",
                new StringContent(body, Encoding.UTF8, "application/json"),
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new CloudCheckpointResult(
                    false,
                    null,
                    $"Cloud checkpoint failed ({(int)response.StatusCode}).");
            }

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("checkpointId", out var id)
                ? new CloudCheckpointResult(true, id.GetString(), null)
                : new CloudCheckpointResult(false, null, "Cloud checkpoint returned no id.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or JsonException)
        {
            return new CloudCheckpointResult(
                false,
                null,
                $"Cloud checkpoint failed: {Describe(ex)}");
        }
    }

    private static async Task<CloudDocumentSnapshot?> ReadSnapshotAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        if (!root.TryGetProperty("json", out var json)
            || json.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var revision = root.TryGetProperty("revision", out var revisionElement)
            && revisionElement.TryGetInt64(out var parsedRevision)
            ? parsedRevision
            : 0;
        var etag = root.TryGetProperty("etag", out var etagElement)
            ? etagElement.GetString()
            : response.Headers.ETag?.Tag;
        return new CloudDocumentSnapshot(json.GetString()!, revision, etag);
    }

    private static async Task<CloudSaveResult> ReadSaveResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var revision = root.TryGetProperty("revision", out var revisionElement)
            && revisionElement.TryGetInt64(out var parsedRevision)
            ? parsedRevision
            : 0;
        var etag = root.TryGetProperty("etag", out var etagElement)
            ? etagElement.GetString()
            : response.Headers.ETag?.Tag;
        return new CloudSaveResult(CloudSaveStatus.Saved, revision, etag);
    }

    private static string Describe(Exception exception)
        => exception.Message.Split('\n')[0].Trim();
}
