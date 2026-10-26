using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;
using Penelopa.Web.Services;
using Xunit;

namespace Penelopa.Web.Tests;

public class DocumentFileServiceTests
{
    private readonly PrimitiveService _primitives = new();
    private readonly SimulationConfigStore _config;
    private readonly DocumentFileService _service;
    private readonly string _sampleJson;

    public DocumentFileServiceTests()
    {
        _config = new SimulationConfigStore(_primitives);
        _sampleJson = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "samples",
            DocumentFileService.SampleFileName));
        _service = new DocumentFileService(
            new HttpClient(new SampleHandler(_sampleJson))
            {
                BaseAddress = new Uri("http://localhost/"),
            },
            _primitives,
            _config);
    }

    [Fact]
    public async Task ReadAsync_ValidDocument_ReturnsParsedDocument()
    {
        var rectangle = new Rectangle { Name = { Value = "Loaded" } };
        var track = new SimulationTrack(
            rectangle.Id,
            "PosX",
            new[]
            {
                new SimulationKeyframe(0d, 10f, InterpolationKind.Linear),
                new SimulationKeyframe(1d, 30f, InterpolationKind.EaseInOut),
            });
        var json = DocumentSerializer.Serialize(
            new Primitive[] { rectangle },
            new Primitive[] { rectangle },
            new SimulationConfig(true, new[] { track }));

        var result = await _service.ReadAsync(BrowserFile("scene.penelopa.json", json));

        Assert.True(result.Success);
        Assert.Equal("scene.penelopa.json", result.FileName);
        Assert.Equal("Loaded", Assert.Single(result.Document!.Roots).Name.Value);
        Assert.Single(result.Document.ResolveSelection());
        Assert.True(result.Document.Simulation!.Loop);
        Assert.Single(result.Document.Simulation.Tracks);
    }

    [Fact]
    public async Task ReadAsync_VersionOneDocument_LoadsWithoutSimulation()
    {
        const string json = @"{
          ""Version"": 1,
          ""Primitives"": [
            {
              ""Id"": ""00000000-0000-0000-0000-000000000001"",
              ""Type"": ""Rectangle"",
              ""Props"": { ""Name"": ""Legacy"" }
            }
          ],
          ""SelectionIds"": []
        }";

        var result = await _service.ReadAsync(BrowserFile("legacy.json", json));

        Assert.True(result.Success);
        Assert.Null(result.Document!.Simulation);
        Assert.Equal("Legacy", Assert.Single(result.Document.Roots).Name.Value);
    }

    [Fact]
    public async Task ReadAsync_WrongExtension_DoesNotOpenTheFile()
    {
        var file = BrowserFile("scene.xml", "<document/>");

        var result = await _service.ReadAsync(file);

        Assert.False(result.Success);
        Assert.Contains("Choose a .penelopa.json or .json file.", result.Error);
        Assert.False(file.Opened);
    }

    [Fact]
    public async Task ReadAsync_OversizedFile_DoesNotOpenTheFile()
    {
        var file = BrowserFile("scene.penelopa.json", "{}", DocumentSerializer.MaxDocumentBytes + 1);

        var result = await _service.ReadAsync(file);

        Assert.False(result.Success);
        Assert.Contains("larger than 4 MB", result.Error);
        Assert.False(file.Opened);
    }

    [Fact]
    public async Task ReadAsync_InvalidJson_ReportsUnsupportedDocument()
    {
        var result = await _service.ReadAsync(BrowserFile("scene.penelopa.json", "not json"));

        Assert.False(result.Success);
        Assert.Contains("valid or supported", result.Error);
    }

    [Fact]
    public async Task LoadSampleAsync_LoadsThePlayerScene()
    {
        var result = await _service.LoadSampleAsync();

        Assert.True(result.Success);
        Assert.Equal(DocumentFileService.SampleFileName, result.FileName);
        Assert.Empty(result.Document!.Warnings);
        Assert.Equal(13, result.Document.Roots.Count);
        Assert.Equal(19, DocumentFileService.CountPrimitives(result.Document.Roots));
        Assert.True(result.Document.Simulation!.Loop);
        Assert.Equal(5, result.Document.Simulation.Tracks.Count);
        Assert.Equal(
            14,
            result.Document.Simulation.Tracks.Sum(track => track.Keyframes.Count));
        var selected = Assert.Single(result.Document.ResolveSelection());
        Assert.Equal("Step Indicator", selected.Name.Value);
        Assert.Equal(
            2,
            result.Document.Simulation.Tracks.Count(track => track.PrimitiveId == selected.Id));
    }

    [Fact]
    public async Task Apply_ReplacesDocumentSelectionAndSimulation()
    {
        _primitives.Add(new Circle { Name = { Value = "Old" } });
        _primitives.SetSelected(_primitives.GetAll()[0]);
        _config.SetLoop(false);

        var result = await _service.LoadSampleAsync();
        _service.Apply(result.Document!);

        Assert.Equal(13, _primitives.GetAll().Count);
        Assert.Equal("Step Indicator", Assert.Single(_primitives.GetSelection()).Name.Value);
        Assert.True(_config.Config.Loop);
        Assert.Equal(5, _config.Config.Tracks.Count);
        Assert.DoesNotContain(_primitives.GetAll(), primitive => primitive.Name.Value == "Old");
    }

    [Fact]
    public void CreateExport_UsesActiveTracksAndSkipsDormantTracks()
    {
        var active = new Rectangle { Name = { Value = "Active" } };
        var dormant = new Rectangle { Name = { Value = "Dormant" } };
        _primitives.Add(active);
        _primitives.Add(dormant);
        _primitives.SetSelected(active);
        Assert.True(_config.TryAddTrack(active.Id, "PosX", 0d, 10f, out var firstError), firstError);
        Assert.True(_config.TryAddTrack(dormant.Id, "PosX", 0d, 20f, out var secondError), secondError);
        _primitives.Remove(dormant);

        var payload = _service.CreateExport();

        Assert.EndsWith(".penelopa.json", payload.FileName);
        using var document = JsonDocument.Parse(payload.Json);
        Assert.Equal(2, document.RootElement.GetProperty("Version").GetInt32());
        var tracks = document.RootElement
            .GetProperty("Simulation")
            .GetProperty("Tracks");
        var track = Assert.Single(tracks.EnumerateArray());
        Assert.Equal(active.Id, track.GetProperty("PrimitiveId").GetGuid());
        Assert.Single(
            document.RootElement.GetProperty("SelectionIds").EnumerateArray());
    }

    private static TestBrowserFile BrowserFile(
        string name,
        string content,
        long? size = null)
        => new(name, Encoding.UTF8.GetBytes(content), size);

    private sealed class TestBrowserFile : IBrowserFile
    {
        private readonly byte[] _content;

        public TestBrowserFile(string name, byte[] content, long? size)
        {
            Name = name;
            _content = content;
            Size = size ?? content.Length;
        }

        public string Name { get; }

        public string ContentType => "application/json";

        public DateTimeOffset LastModified { get; } = DateTimeOffset.UtcNow;

        public long Size { get; }

        public bool Opened { get; private set; }

        public Stream OpenReadStream(
            long maxAllowedSize = 512000,
            CancellationToken cancellationToken = default)
        {
            Opened = true;
            if (_content.Length > maxAllowedSize)
            {
                throw new IOException("The file is too large.");
            }

            return new MemoryStream(_content, writable: false);
        }
    }

    private sealed class SampleHandler : HttpMessageHandler
    {
        private readonly string _json;

        public SampleHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(
                new Uri("http://localhost/samples/loop-study-01.penelopa.json"),
                request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
