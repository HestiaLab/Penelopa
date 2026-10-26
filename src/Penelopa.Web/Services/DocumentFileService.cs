using System.Text;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

/// <summary>读取本地文档文件的结果；原文件名即使失败也保留给 UI 显示。</summary>
public sealed record DocumentFileLoadResult(
    string FileName,
    DeserializeResult? Document,
    string? Error)
{
    public bool Success => Document is not null;
}

/// <summary>导出文件的名称与 JSON 负载；实际下载由 UI 层的浏览器助手完成。</summary>
public sealed record DocumentExportPayload(string FileName, string Json);

/// <summary>
/// v2 JSON 文档的文件边界。复用 DocumentSerializer 的版本、字节、深度和领域校验；
/// 导入只替换内存中的文档/配置，不写 localStorage，也不负责确认或撤销。
/// </summary>
public sealed class DocumentFileService
{
    public const string FileExtension = ".json";
    public const string SampleFileName = "loop-study-01.penelopa.json";
    public const string SamplePath = "samples/" + SampleFileName;

    private readonly HttpClient _http;
    private readonly IPrimitiveService _primitives;
    private readonly SimulationConfigStore _config;

    public DocumentFileService(
        HttpClient http,
        IPrimitiveService primitives,
        SimulationConfigStore config)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// 读取并校验一个用户选择的文件。扩展名只做前置提示，内容仍由
    /// DocumentSerializer 完整校验；失败时当前文档不会被触碰。
    /// </summary>
    public async Task<DocumentFileLoadResult> ReadAsync(
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (!string.Equals(
            Path.GetExtension(file.Name),
            FileExtension,
            StringComparison.OrdinalIgnoreCase))
        {
            return Failure(file.Name, "Choose a .penelopa.json or .json file.");
        }

        if (file.Size > DocumentSerializer.MaxDocumentBytes)
        {
            return Failure(file.Name, "The file is larger than 4 MB.");
        }

        try
        {
            await using var stream = file.OpenReadStream(
                DocumentSerializer.MaxDocumentBytes,
                cancellationToken);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            var json = await reader.ReadToEndAsync();
            return Parse(file.Name, json);
        }
        catch (Exception ex) when (ex is IOException
            or JSException
            or JSDisconnectedException
            or InvalidOperationException
            or NotSupportedException)
        {
            LogFailure("ReadAsync", ex);
            return Failure(file.Name, "The file could not be read.");
        }
    }

    /// <summary>读取随应用发布的示例场景；示例与用户文件走同一套 v2 校验。</summary>
    public async Task<DocumentFileLoadResult> LoadSampleAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _http.GetStringAsync(SamplePath, cancellationToken);
            return Parse(SampleFileName, json);
        }
        catch (Exception ex) when (ex is HttpRequestException
            or TaskCanceledException
            or JSException
            or JSDisconnectedException
            or InvalidOperationException)
        {
            LogFailure("LoadSampleAsync", ex);
            return Failure(SampleFileName, "The sample scene could not be loaded.");
        }
    }

    /// <summary>整体替换文档、选区和仿真配置；不产生文件/library 持久化副作用。</summary>
    public void Apply(DeserializeResult document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var selected = document.ResolveSelection();
        _primitives.ReplaceAll(document.Roots);
        _primitives.SetSelectedRange(selected);
        _config.ReplaceConfig(document.Simulation ?? SimulationConfig.Empty);
    }

    /// <summary>生成与 Save 相同的负载：当前内存文档、选区和 active 仿真轨道。</summary>
    public DocumentExportPayload CreateExport()
    {
        var roots = _primitives.GetAll().ToList();
        var selection = _primitives.GetSelection().ToList();
        var active = _config.ExportActive();
        var json = DocumentSerializer.Serialize(roots, selection, active);
        var fileName = $"penelopa-{DateTime.Now:yyyyMMdd-HHmmss}.penelopa.json";
        return new DocumentExportPayload(fileName, json);
    }

    /// <summary>统计根树中的图元总数（含容器后代），用于导入确认摘要。</summary>
    public static int CountPrimitives(IReadOnlyList<Primitive> roots)
    {
        var count = 0;
        foreach (var root in roots)
        {
            count++;
            if (root is Container container)
            {
                count += CountPrimitives(container.Children);
            }
        }

        return count;
    }

    private static DocumentFileLoadResult Parse(string fileName, string json)
    {
        var document = DocumentSerializer.Deserialize(json);
        return document is null
            ? Failure(fileName, "This is not a valid or supported Penelopa document.")
            : new DocumentFileLoadResult(fileName, document, null);
    }

    private static DocumentFileLoadResult Failure(string fileName, string error)
        => new(fileName, null, error);

    private static void LogFailure(string operation, Exception exception)
        => Console.Error.WriteLine($"[Penelopa] DocumentFileService.{operation} failed: {exception}");
}
