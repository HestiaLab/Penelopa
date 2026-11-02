using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;

namespace Penelopa.Core.Services;

/// <summary>
/// 文档写入的唯一入口。P1 阶段把现有服务的事务性写操作收敛到此处；
/// P2 将在内部接入记录型补丁历史，UI 与插件只依赖本类型，不直接改模型。
/// </summary>
public sealed class DocumentEditor
{
    private readonly IPrimitiveService _service;

    public DocumentEditor(IPrimitiveService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    public bool CanUndo => _service.CanUndo;

    public bool CanRedo => _service.CanRedo;

    public Guid DocumentId => _service.DocumentId;

    public long Revision => _service.Revision;

    public string? UndoLabel => _service.UndoLabel;

    public string? RedoLabel => _service.RedoLabel;

    public int UndoDepth => _service.UndoDepth;

    public int RedoDepth => _service.RedoDepth;

    public long EstimatedBytes => _service.EstimatedBytes;

    public void ConfigureHistoryBudget(int maxEntries, long maxBytes)
        => _service.ConfigureHistoryBudget(maxEntries, maxBytes);

    /// <summary>开始事务；嵌套调用共享同一事务。</summary>
    public void BeginTransaction() => _service.BeginTransaction();

    /// <summary>使用指定命令 id 与标签开始事务（手势、宏与插件使用）。</summary>
    public void BeginTransaction(string commandId, string label)
        => _service.BeginTransaction(commandId, label);

    /// <summary>在直接修改属性前记录图元当前值；手势宿主在首次修改前调用。</summary>
    public void RecordBeforeChange(IEnumerable<Primitive> primitives)
        => _service.RecordBeforeChange(primitives);

    public void CommitTransaction() => _service.CommitTransaction();

    /// <summary>结束当前属性合并窗口；属性面板切换编辑目标时调用。</summary>
    public void EndCoalescing() => _service.EndCoalescing();

    public void CancelTransaction() => _service.CancelTransaction();

    public void Undo() => _service.Undo();

    public void Redo() => _service.Redo();

    public void Add(Primitive primitive) => _service.Add(primitive);

    public void AddToContainer(Container container, Primitive child)
        => _service.AddToContainer(container, child);

    public Container? GroupSelection(out string? error)
        => _service.GroupSelection(out error);

    public bool TryUngroup(Container container, out string? error)
        => _service.TryUngroup(container, out error);

    public void Remove(Primitive primitive) => _service.Remove(primitive);

    public void RemoveRange(IEnumerable<Primitive> primitives)
        => _service.RemoveRange(primitives);

    /// <summary>整体替换文档（加载/导入）；调用方另行恢复选区与配置。</summary>
    public void ReplaceAll(IReadOnlyList<Primitive> primitives)
        => _service.ReplaceAll(primitives);

    public bool Align(AlignType type) => _service.Align(type);

    public bool TrySetProperty(
        Primitive owner,
        PropValue property,
        string text,
        out string? error)
        => _service.TrySetProperty(owner, property, text, out error);

    public bool TrySetProperty(
        IReadOnlyList<Primitive> owners,
        string propertyName,
        string text,
        out string? error)
        => _service.TrySetProperty(owners, propertyName, text, out error);

    public bool MoveSelection(float deltaX, float deltaY)
        => _service.MoveSelection(deltaX, deltaY);

    /// <summary>捕获当前文档与完整历史栈（导入回滚等一次性操作使用）。</summary>
    public DocumentHistoryState CaptureHistoryState()
        => _service.CaptureHistoryState();

    public void RestoreHistoryState(DocumentHistoryState state)
        => _service.RestoreHistoryState(state);
}
