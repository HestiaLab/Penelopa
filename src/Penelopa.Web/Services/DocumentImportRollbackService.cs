using Penelopa.Core.Primitives;
using Penelopa.Core.Services;
using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

/// <summary>
/// 一次性导入回滚：在替换前捕获文档、选区、仿真配置以及两者的 undo/redo 栈，
/// 在用户编辑、配置变更、成功保存、加载/再次导入或刷新后失效。
/// </summary>
public sealed class DocumentImportRollbackService : IDisposable
{
    private readonly IPrimitiveService _primitives;
    private readonly SimulationConfigStore _simulation;
    private readonly DocumentStorage _storage;
    private Snapshot? _snapshot;
    private string? _fileName;
    private bool _suspended;
    private bool _disposed;

    public DocumentImportRollbackService(
        IPrimitiveService primitives,
        SimulationConfigStore simulation,
        DocumentStorage storage)
    {
        _primitives = primitives ?? throw new ArgumentNullException(nameof(primitives));
        _simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _primitives.Changed += HandleDocumentChanged;
        _simulation.Changed += HandleSimulationConfigChanged;
        _storage.Saved += HandleSaved;
    }

    public bool CanUndo => _snapshot is not null;

    public string? ImportedFileName => _fileName;

    public event Action? Changed;

    /// <summary>
    /// 捕获当前工作区，然后执行导入替换。捕获或替换失败时保持/恢复原状态，
    /// 不覆盖已有的一次性快照。成功时用本次导入前状态替换快照。
    /// </summary>
    public bool TryApplyImport(string fileName, Action apply, out string? error)
    {
        ArgumentNullException.ThrowIfNull(apply);
        if (_disposed)
        {
            error = "The import is not available.";
            return false;
        }

        Snapshot snapshot;
        try
        {
            snapshot = new Snapshot(
                _primitives.CaptureHistoryState(),
                _primitives.GetSelection().ToList(),
                _simulation.CaptureState());
        }
        catch (Exception ex)
        {
            LogFailure("Capture", ex);
            error = "The import could not start because the current document is still being edited.";
            return false;
        }

        _suspended = true;
        try
        {
            apply();
        }
        catch (Exception ex)
        {
            LogFailure("Apply", ex);
            try
            {
                Restore(snapshot);
            }
            catch (Exception rollbackException)
            {
                LogFailure("Apply rollback", rollbackException);
            }

            error = "The document could not be imported.";
            return false;
        }
        finally
        {
            _suspended = false;
        }

        _snapshot = snapshot;
        _fileName = fileName;
        error = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>恢复导入前的工作区；不产生文档或配置历史条目，也没有 redo。</summary>
    public bool UndoImport()
    {
        if (_disposed || _snapshot is not { } snapshot)
        {
            return false;
        }

        var fileName = _fileName;
        _suspended = true;
        try
        {
            Restore(snapshot);
        }
        catch
        {
            _snapshot = snapshot;
            _fileName = fileName;
            throw;
        }
        finally
        {
            _suspended = false;
        }

        _snapshot = null;
        _fileName = null;
        Changed?.Invoke();
        return true;
    }

    /// <summary>丢弃一次性快照（成功保存、加载或其它整体替换时由事件触发）。</summary>
    public void Clear()
    {
        if (_snapshot is null)
        {
            return;
        }

        _snapshot = null;
        _fileName = null;
        Changed?.Invoke();
    }

    private void Restore(Snapshot snapshot)
    {
        _primitives.RestoreHistoryState(snapshot.DocumentHistory);
        _primitives.SetSelectedRange(snapshot.Selection);
        _simulation.RestoreState(snapshot.Simulation);
    }

    private void HandleDocumentChanged(DocumentChange change)
    {
        if (_suspended || _snapshot is null)
        {
            return;
        }

        if ((change.Kind & (DocumentChangeKind.Structure | DocumentChangeKind.Geometry)) != 0)
        {
            Clear();
        }
    }

    private void HandleSimulationConfigChanged()
    {
        if (_suspended || _snapshot is null)
        {
            return;
        }

        Clear();
    }

    private void HandleSaved()
    {
        if (_suspended)
        {
            return;
        }

        Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _primitives.Changed -= HandleDocumentChanged;
        _simulation.Changed -= HandleSimulationConfigChanged;
        _storage.Saved -= HandleSaved;
        _snapshot = null;
        _fileName = null;
    }

    private static void LogFailure(string operation, Exception exception)
        => Console.Error.WriteLine(
            $"[Penelopa] DocumentImportRollbackService.{operation} failed: {exception}");

    private sealed record Snapshot(
        DocumentHistoryState DocumentHistory,
        IReadOnlyList<Primitive> Selection,
        SimulationConfigState Simulation);
}
