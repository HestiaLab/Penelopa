using System.Net;
using System.Text;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class CloudDocumentGatewayTests
{
    [Fact]
    public async Task GetAsync_ReadsSnapshotAndETag()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """{"json":"{\"Version\":3}","revision":4,"etag":"\"abc\""}"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.GetAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        Assert.True(result.Success);
        Assert.Equal(4, result.Document!.Revision);
        Assert.Equal("\"abc\"", result.Document.ETag);
        Assert.Equal("{\"Version\":3}", result.Document.Json);
        Assert.Equal("documents/11111111-1111-1111-1111-111111111111", handler.LastRequest!.RequestUri!.PathAndQuery.TrimStart('/'));
    }

    [Fact]
    public async Task SaveAsync_SendsBaseRevisionAndIfMatch()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.OK,
            """{"revision":5,"etag":"\"def\""}"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.SaveAsync(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            """{"Version":3}""",
            baseRevision: 4,
            etag: "\"abc\"");

        Assert.Equal(CloudSaveStatus.Saved, result.Status);
        Assert.Equal(5, result.Revision);
        Assert.Equal("\"def\"", result.ETag);
        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("4", Assert.Single(request.Headers.GetValues("X-Penelopa-Base-Revision")));
        Assert.Equal("\"abc\"", request.Headers.IfMatch.Single().Tag);
        Assert.Equal(
            """{"Version":3}""",
            handler.LastBody);
    }

    [Fact]
    public async Task SaveAsync_Conflict_ReturnsRemoteSnapshot()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.Conflict,
            """{"json":"{\"Version\":3,\"Revision\":9}","revision":9,"etag":"\"remote\""}"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.SaveAsync(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            """{"Version":3}""",
            baseRevision: 4,
            etag: "\"abc\"");

        Assert.Equal(CloudSaveStatus.Conflict, result.Status);
        Assert.NotNull(result.Remote);
        Assert.Equal(9, result.Remote!.Revision);
        Assert.Equal("\"remote\"", result.Remote.ETag);
        Assert.Contains("Revision", result.Remote.Json);
    }

    [Fact]
    public async Task CreateCheckpointAsync_PostsNameAndReturnsId()
    {
        var handler = new StubHandler(_ => Json(
            HttpStatusCode.Created,
            """{"checkpointId":"checkpoint-1"}"""));
        var gateway = CreateGateway(handler);

        var result = await gateway.CreateCheckpointAsync(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "Milestone 1",
            """{"Version":3}""",
            revision: 7);

        Assert.True(result.Success);
        Assert.Equal("checkpoint-1", result.CheckpointId);
        Assert.Equal(
            "documents/44444444-4444-4444-4444-444444444444/checkpoints",
            handler.LastRequest!.RequestUri!.PathAndQuery.TrimStart('/'));
        Assert.Contains("\"name\":\"Milestone 1\"", handler.LastBody);
        Assert.Contains("\"revision\":7", handler.LastBody);
    }

    [Fact]
    public async Task SaveAsync_ServerError_ReturnsFailed()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var gateway = CreateGateway(handler);

        var result = await gateway.SaveAsync(
            Guid.NewGuid(),
            "{}",
            baseRevision: 0,
            etag: null);

        Assert.Equal(CloudSaveStatus.Failed, result.Status);
        Assert.Contains("500", result.Error);
    }

    private static HttpCloudDocumentGateway CreateGateway(StubHandler handler)
        => new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://cloud.example/"),
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _response;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            _response = response;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response(request);
        }
    }
}
