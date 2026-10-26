using Penelopa.Core.Primitives;

namespace Penelopa.Core.Services;

/// <summary>
/// 文档撤销/重做历史。快照按引用捕获图元树及每个图元的属性值和容器子元素；
/// 应用快照时恢复这些值并通过 <see cref="IPrimitiveService.ReplaceAll"/> 重建根列表。
/// 选区是 UI 状态而非文档状态：不被捕获，撤销/重做保留恢复后仍存活的选区。
/// 所有修改通过事务边界管理：<see cref="BeginTransaction"/> 在首次修改前拍快照，
/// <see cref="CommitTransaction"/> 生成一个撤销条目，<see cref="CancelTransaction"/> 精确回滚到事务前状态。
/// 手势取消与撤销共用同一套快照恢复逻辑。
/// </summary>
public sealed class DocumentHistory
{
    private readonly IPrimitiveService _service;
    private readonly Stack<DocumentSnapshot> _undoStack = new();
    private readonly Stack<DocumentSnapshot> _redoStack = new();
    private DocumentSnapshot? _pending;
    private int _depth;

    public DocumentHistory(IPrimitiveService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    /// <summary>是否可撤销。</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>是否可重做。</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>是否有打开的事务。</summary>
    public bool IsTransactionOpen => _depth > 0;

    /// <summary>捕获当前文档状态并立即生成撤销条目（兼容旧的“修改前捕获”用法）。</summary>
    public void Capture()
    {
        if (_depth > 0)
        {
            // 外层事务已经保存了修改前状态，无需重复捕获。
            return;
        }

        _undoStack.Push(DocumentSnapshot.Capture(_service.GetAll().ToList()));
        _redoStack.Clear();
    }

    /// <summary>开始事务；首次调用时捕获修改前快照，嵌套调用共享同一快照。</summary>
    /// <remarks>快照捕获失败时不改变事务深度，避免后续提交/取消作用在损坏的状态上。</remarks>
    public void BeginTransaction()
    {
        if (_depth > 0)
        {
            _depth++;
            return;
        }

        _pending = DocumentSnapshot.Capture(_service.GetAll().ToList());
        _depth = 1;
    }

    /// <summary>提交事务，生成一个撤销条目；嵌套调用在最后一层提交。</summary>
    public void CommitTransaction()
    {
        if (_depth == 0)
        {
            return;
        }

        if (--_depth > 0)
        {
            return;
        }

        var pending = _pending;
        _pending = null;
        if (pending is null)
        {
            return;
        }

        _undoStack.Push(pending);
        _redoStack.Clear();
    }

    /// <summary>取消事务并恢复到事务前状态（手势取消路径）。</summary>
    public void CancelTransaction()
    {
        if (_depth == 0)
        {
            return;
        }

        var pending = _pending;
        _depth = 0;
        _pending = null;
        pending?.Apply(_service);
    }

    /// <summary>丢弃事务快照但不恢复（未产生实际修改时使用）。</summary>
    public void DiscardTransaction()
    {
        if (_depth == 0)
        {
            return;
        }

        _depth = 0;
        _pending = null;
    }

    /// <summary>撤销最近的事务/捕获。</summary>
    public void Undo()
    {
        if (_depth > 0 || _undoStack.Count == 0)
        {
            return;
        }

        var current = DocumentSnapshot.Capture(_service.GetAll().ToList());
        var target = _undoStack.Pop();
        _redoStack.Push(current);
        target.Apply(_service);
    }

    /// <summary>重做最近撤销的捕获。</summary>
    public void Redo()
    {
        if (_depth > 0 || _redoStack.Count == 0)
        {
            return;
        }

        var current = DocumentSnapshot.Capture(_service.GetAll().ToList());
        var target = _redoStack.Pop();
        _undoStack.Push(current);
        target.Apply(_service);
    }

    /// <summary>清空两个栈（文档被外部替换时）。</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    /// <summary>
    /// 捕获当前文档和完整的 undo/redo 栈，供一次性导入回滚使用。
    /// 事务进行中不允许捕获，避免保存一个无法一致恢复的中间状态。
    /// </summary>
    public DocumentHistoryState CaptureState()
    {
        if (_depth > 0)
        {
            throw new InvalidOperationException(
                "Document history cannot be captured while a transaction is open.");
        }

        return new DocumentHistoryState(
            DocumentSnapshot.Capture(_service.GetAll().ToList()),
            _undoStack.ToArray(),
            _redoStack.ToArray());
    }

    /// <summary>
    /// 恢复当前文档及 undo/redo 栈。选区不属于历史快照，由调用方另行恢复。
    /// </summary>
    public void RestoreState(DocumentHistoryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_depth > 0)
        {
            throw new InvalidOperationException(
                "Document history cannot be restored while a transaction is open.");
        }

        _undoStack.Clear();
        _redoStack.Clear();
        state.Current.Apply(_service);

        for (var index = state.Undo.Count - 1; index >= 0; index--)
        {
            _undoStack.Push(state.Undo[index]);
        }

        for (var index = state.Redo.Count - 1; index >= 0; index--)
        {
            _redoStack.Push(state.Redo[index]);
        }
    }
}

/// <summary>文档历史的一次性完整快照；内容对调用方不透明。</summary>
public sealed class DocumentHistoryState
{
    internal DocumentHistoryState(
        DocumentSnapshot current,
        IReadOnlyList<DocumentSnapshot> undo,
        IReadOnlyList<DocumentSnapshot> redo)
    {
        Current = current;
        Undo = undo;
        Redo = redo;
    }

    internal DocumentSnapshot Current { get; }

    internal IReadOnlyList<DocumentSnapshot> Undo { get; }

    internal IReadOnlyList<DocumentSnapshot> Redo { get; }
}

/// <summary>图元树的某一时刻快照。</summary>
public sealed class DocumentSnapshot
{
    private readonly List<Primitive> _roots;
    private readonly Dictionary<Guid, Dictionary<string, object>> _props;
    private readonly Dictionary<Guid, List<Primitive>> _children;

    private DocumentSnapshot(
        List<Primitive> roots,
        Dictionary<Guid, Dictionary<string, object>> props,
        Dictionary<Guid, List<Primitive>> children)
    {
        _roots = roots;
        _props = props;
        _children = children;
    }

    public static DocumentSnapshot Capture(IReadOnlyList<Primitive> roots)
    {
        var props = new Dictionary<Guid, Dictionary<string, object>>();
        var children = new Dictionary<Guid, List<Primitive>>();
        foreach (var root in roots)
        {
            CaptureNode(root, props, children);
        }

        return new DocumentSnapshot(roots.ToList(), props, children);
    }

    private static void CaptureNode(
        Primitive primitive,
        Dictionary<Guid, Dictionary<string, object>> props,
        Dictionary<Guid, List<Primitive>> children)
    {
        var values = new Dictionary<string, object>();
        foreach (var prop in primitive.Props)
        {
            if (prop.GetBoxedValue() is { } value)
            {
                values[prop.Name] = value;
            }
        }

        props[primitive.Id] = values;

        if (primitive is Container container)
        {
            children[primitive.Id] = container.Children.ToList();
            foreach (var child in container.Children)
            {
                CaptureNode(child, props, children);
            }
        }
    }

    /// <summary>将此快照恢复到服务上（根列表和属性值）。</summary>
    public void Apply(IPrimitiveService service)
    {
        // 从捕获的结构（_children）重建快照的节点集，而非活跃树：
        // container.Children 在 Apply 运行时已偏离快照（本次撤销回退的修改已发生），
        // 遍历活跃子元素会错误地包含快照不拥有的节点。
        var snapshotNodes = new List<Primitive>();
        var snapshotSet = new HashSet<Primitive>();
        foreach (var root in _roots)
        {
            CollectSnapshotNodes(root, snapshotNodes, snapshotSet);
        }

        // 记录快照中的父子关系，用于把节点从当前（可能已被替换掉的）父级上摘下。
        var snapshotParents = new Dictionary<Primitive, Container?>();
        foreach (var root in _roots)
        {
            snapshotParents[root] = null;
        }

        foreach (var (containerId, savedChildren) in _children)
        {
            var container = snapshotNodes.FirstOrDefault(p => p is Container c && c.Id == containerId) as Container;
            if (container is null)
            {
                continue;
            }

            foreach (var child in savedChildren)
            {
                snapshotParents[child] = container;
            }
        }

        // 分组会创建不在目标快照中的新容器；撤销/重做时必须先解除其子项的旧父指针，
        // 否则 ReplaceAll 会把子项装回根列表，但 Parent 仍指向旧容器。
        foreach (var primitive in snapshotNodes)
        {
            if (primitive.Parent is Container currentParent
                && (!snapshotParents.TryGetValue(primitive, out var targetParent)
                    || !ReferenceEquals(currentParent, targetParent)))
            {
                currentParent.RemoveChild(primitive);
            }
        }

        // 恢复容器子元素（结构可能已变更）
        foreach (var (containerId, savedChildren) in _children)
        {
            var container = snapshotNodes.FirstOrDefault(p => p is Container c && c.Id == containerId) as Container;
            if (container is null)
            {
                continue;
            }

            foreach (var existing in container.Children.ToList())
            {
                container.RemoveChild(existing);
            }

            foreach (var child in savedChildren)
            {
                // 恢复期间容器可能尚未挂回文档（例如重做分组）；先清除子项归属，
                // 由后面的 ReplaceAll 从根节点统一重建文档归属。
                child.SetOwnerRecursive(null);
                container.AddChild(child);
            }
        }

        // 保留活跃选区跨恢复：选区是 UI 状态，不在快照中，ReplaceAll 会清空它，
        // 因此先记住，恢复后仅重新应用仍存活的项。
        var liveSelection = service.GetSelection().ToList();

        service.ReplaceAll(_roots, clearHistory: false);

        // 恢复属性值
        foreach (var primitive in snapshotNodes)
        {
            if (!_props.TryGetValue(primitive.Id, out var values))
            {
                continue;
            }

            foreach (var prop in primitive.Props)
            {
                if (!values.TryGetValue(prop.Name, out var value))
                {
                    continue;
                }

                prop.SetBoxedValue(value);
            }
        }

        // 重新应用活跃选区，仅保留本次恢复后仍存活的图元（被撤销修改新增的图元会丢弃）
        service.SetSelectedRange(liveSelection.Where(snapshotSet.Contains));
    }

    private void CollectSnapshotNodes(Primitive root, List<Primitive> nodes, HashSet<Primitive> set)
    {
        nodes.Add(root);
        set.Add(root);
        if (root is Container container && _children.TryGetValue(container.Id, out var savedChildren))
        {
            foreach (var child in savedChildren)
            {
                CollectSnapshotNodes(child, nodes, set);
            }
        }
    }
}
