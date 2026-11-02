using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using System.Text.Json;

namespace Penelopa.Core.Services;

/// <summary><see cref="IPrimitiveService"/> 的默认实现，基于列表存储。</summary>
public class PrimitiveService : IPrimitiveService
{
    private readonly List<Primitive> _primitives = new();
    private readonly HashSet<Primitive> _selection = new();
    private readonly DocumentHistory _history;
    private Dictionary<string, JsonElement> _extensions = new(StringComparer.Ordinal);

    public PrimitiveService()
        : this(TimeProvider.System)
    {
    }

    internal PrimitiveService(TimeProvider clock)
    {
        _history = new DocumentHistory(this, clock);
    }

    public Guid DocumentId { get; private set; } = Guid.NewGuid();

    public long Revision { get; private set; }

    public IReadOnlyDictionary<string, JsonElement> Extensions => _extensions;

    internal void BumpRevision() => Revision++;

    public void SetDocumentIdentity(Guid documentId, long revision)
    {
        DocumentId = documentId == Guid.Empty ? Guid.NewGuid() : documentId;
        Revision = Math.Max(0, revision);
    }

    public void SetExtensions(IReadOnlyDictionary<string, JsonElement>? extensions)
    {
        _extensions = extensions is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(extensions, StringComparer.Ordinal);
    }

    /// <summary>结构变更也通过事务边界，保证每种修改都产生可撤销条目。</summary>
    public void Add(Primitive primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        if (primitive.Parent is not null)
        {
            throw new InvalidOperationException("Add only accepts root primitives; use AddToContainer for children.");
        }

        if (primitive.Owner is not null && !ReferenceEquals(primitive.Owner, this))
        {
            throw new InvalidOperationException("The primitive belongs to another document.");
        }

        if (_primitives.Contains(primitive))
        {
            throw new InvalidOperationException("The primitive is already in this document.");
        }

        ValidateSupportedProperties(primitive);

        _history.BeginTransaction(DocumentCommands.Add, $"Add {Describe(primitive)}");
        _primitives.Add(primitive);
        primitive.SetOwnerRecursive(this);
        _history.RecordStructure(new StructureChange(
            StructureOp.Insert,
            primitive,
            null,
            -1,
            null,
            _primitives.Count - 1));
        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Structure);
    }

    /// <summary>将根图元加入容器。子图元从根列表移除，渲染和树面板仅在容器内显示。</summary>
    /// <remarks>
    /// 拒绝自身、跨文档图元、已有父级的子图元，以及把祖先移入其后代的环路操作。
    /// 校验在事务开始前完成，失败不会产生历史条目或部分结构修改。
    /// </remarks>
    public void AddToContainer(Container container, Primitive child)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(child);

        if (ReferenceEquals(container, child))
        {
            throw new ArgumentException("A container cannot contain itself.", nameof(child));
        }

        if (!IsInDocument(container))
        {
            throw new InvalidOperationException("The container must belong to this document.");
        }

        if (child.Owner is not null && !ReferenceEquals(child.Owner, this))
        {
            throw new InvalidOperationException("The child belongs to another document.");
        }

        if (child.Parent is not null)
        {
            throw new InvalidOperationException("A primitive can only have one parent.");
        }

        if (IsDescendantOf(container, child))
        {
            throw new InvalidOperationException("Cannot move an ancestor into its own descendant.");
        }

        ValidateSupportedProperties(child);

        var toIndex = container.Children.Count;
        var fromRootIndex = _primitives.IndexOf(child);
        _history.BeginTransaction(
            DocumentCommands.MoveIntoContainer,
            "Move into container");
        try
        {
            if (fromRootIndex >= 0)
            {
                _history.RecordStructure(new StructureChange(
                    StructureOp.Move,
                    child,
                    null,
                    fromRootIndex,
                    container,
                    toIndex));
                _primitives.Remove(child);
            }
            else
            {
                _history.RecordStructure(new StructureChange(
                    StructureOp.Insert,
                    child,
                    null,
                    -1,
                    container,
                    toIndex));
            }

            container.AddChild(child);
        }
        catch
        {
            _history.CancelTransaction();
            throw;
        }

        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Structure);
    }

    /// <summary>把当前顶层选区放入新的 identity 偏移容器；结构与选区只产生一个历史条目。</summary>
    /// <remarks>
    /// 只接受至少两个文档根图元。子项按当前根顺序进入容器，容器插入到第一个选中根的位置，
    /// 因此 identity 变换下世界位置和 z 序保持不变。失败在事务开始前返回，不产生历史条目。
    /// </remarks>
    public Container? GroupSelection(out string? error)
    {
        var selection = _selection.ToList();
        if (selection.Count < 2)
        {
            error = "Select at least two root primitives to group.";
            return null;
        }

        if (selection.Any(primitive => primitive.Parent is not null))
        {
            error = "Grouping requires root-level primitives; drilled children cannot be grouped.";
            return null;
        }

        var selected = new HashSet<Primitive>(selection);
        var ordered = _primitives.Where(selected.Contains).ToList();
        if (ordered.Count != selection.Count)
        {
            error = "Every selected primitive must belong to this document.";
            return null;
        }

        // 第一个选中根之前的根都未选中，移除选中项后该索引仍是正确的插入点。
        var index = _primitives.IndexOf(ordered[0]);
        var group = Container.CreateOffset("Group", 0f, 0f);

        _history.BeginTransaction(
            DocumentCommands.Group,
            $"Group {ordered.Count} primitives");
        try
        {
            _history.RecordStructure(new StructureChange(
                StructureOp.Insert,
                group,
                null,
                -1,
                null,
                index));
            _primitives.Insert(index, group);
            group.SetOwnerRecursive(this);

            foreach (var primitive in ordered)
            {
                var fromIndex = _primitives.IndexOf(primitive);
                var toIndex = group.Children.Count;
                _history.RecordStructure(new StructureChange(
                    StructureOp.Move,
                    primitive,
                    null,
                    fromIndex,
                    group,
                    toIndex));

                _primitives.RemoveAt(fromIndex);
                primitive.SetOwnerRecursive(null);
                group.AddChild(primitive);
            }

            _selection.Clear();
            _selection.Add(group);
        }
        catch
        {
            _history.CancelTransaction();
            throw;
        }

        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Structure | DocumentChangeKind.Selection);
        error = null;
        return group;
    }

    /// <summary>展开 identity 根容器；嵌套或非 identity 容器被拒绝，避免子项世界位置改变。</summary>
    public bool TryUngroup(Container container, out string? error)
    {
        ArgumentNullException.ThrowIfNull(container);

        if (container.Parent is not null || !IsInDocument(container))
        {
            error = "Only root containers in this document can be ungrouped.";
            return false;
        }

        if (!container.LocalTransform.IsIdentity)
        {
            error = "Ungroup requires an identity transform (offset 0, rotation 0, scale 1).";
            return false;
        }

        var index = _primitives.IndexOf(container);
        var children = container.Children.ToList();

        _history.BeginTransaction(DocumentCommands.Ungroup, "Ungroup container");
        try
        {
            for (var childIndex = 0; childIndex < children.Count; childIndex++)
            {
                var child = children[childIndex];
                var fromIndex = IndexOf(container.Children, child);
                var toIndex = index + childIndex;
                _history.RecordStructure(new StructureChange(
                    StructureOp.Move,
                    child,
                    container,
                    fromIndex,
                    null,
                    toIndex));

                container.RemoveChild(child);
                _primitives.Insert(toIndex, child);
                child.SetOwnerRecursive(this);
            }

            var containerIndex = _primitives.IndexOf(container);
            _history.RecordStructure(new StructureChange(
                StructureOp.Remove,
                container,
                null,
                containerIndex,
                null,
                -1));

            _primitives.Remove(container);
            container.SetOwnerRecursive(null);

            _selection.Clear();
            foreach (var child in children)
            {
                _selection.Add(child);
            }
        }
        catch
        {
            _history.CancelTransaction();
            throw;
        }

        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Structure | DocumentChangeKind.Selection);
        error = null;
        return true;
    }

    public void Remove(Primitive primitive) => RemoveRange(new[] { primitive });

    /// <summary>批量删除：一次事务、一个撤销条目；已失效的选区（含被删除容器的后代）一并清理。</summary>
    public void RemoveRange(IEnumerable<Primitive> primitives)
    {
        var candidates = primitives
            .Where(IsInDocument)
            .Distinct()
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        // 祖先与后代同时出现时只删除祖先，避免把已被移除的子树再记录一次。
        var targets = candidates
            .Where(primitive => !candidates.Any(
                other => !ReferenceEquals(other, primitive) && IsDescendantOf(primitive, other)))
            .ToList();

        _history.BeginTransaction(
            DocumentCommands.Delete,
            $"Delete {targets.Count} {(targets.Count == 1 ? "primitive" : "primitives")}");
        foreach (var primitive in targets)
        {
            if (primitive.Parent is Container owner)
            {
                var index = IndexOf(owner.Children, primitive);
                _history.RecordStructure(new StructureChange(
                    StructureOp.Remove,
                    primitive,
                    owner,
                    index,
                    null,
                    -1));
                owner.RemoveChild(primitive);
            }
            else
            {
                var index = _primitives.IndexOf(primitive);
                _history.RecordStructure(new StructureChange(
                    StructureOp.Remove,
                    primitive,
                    null,
                    index,
                    null,
                    -1));
                if (_primitives.Remove(primitive))
                {
                    primitive.SetOwnerRecursive(null);
                }
            }
        }

        // 删除容器会连带移除其后代；选区只保留仍属于当前文档的图元
        _selection.RemoveWhere(primitive => !IsInDocument(primitive));
        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Structure | DocumentChangeKind.Selection);
    }

    /// <summary>根图元的只读快照，避免调用方通过返回集合绕过事务修改文档。</summary>
    public IReadOnlyList<Primitive> GetAll()
    {
        return _primitives.ToArray();
    }

    /// <summary>替换整个文档（加载/历史恢复）。安装新根列表、更新文档归属、清空选区；调用方再应用选区。</summary>
    public void ReplaceAll(IReadOnlyList<Primitive> primitives, bool clearHistory = true)
    {
        ArgumentNullException.ThrowIfNull(primitives);
        foreach (var root in primitives)
        {
            if (root.Owner is not null && !ReferenceEquals(root.Owner, this))
            {
                throw new InvalidOperationException("A primitive belongs to another document.");
            }

            ValidateSupportedProperties(root);
        }

        if (clearHistory)
        {
            _history.Clear();
        }

        var previous = _primitives.ToArray();
        _primitives.Clear();
        _primitives.AddRange(primitives);
        _selection.Clear();

        // 旧树中被替换掉的节点释放文档归属；仍在新树中的节点由下面的递归赋值覆盖
        foreach (var root in previous)
        {
            if (!_primitives.Contains(root))
            {
                root.SetOwnerRecursive(null);
            }
        }

        foreach (var root in _primitives)
        {
            root.SetOwnerRecursive(this);
        }

        RaiseChanged(DocumentChangeKind.Structure | DocumentChangeKind.Selection);
    }

    public void Undo() => _history.Undo();

    public void Redo() => _history.Redo();

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? UndoLabel => _history.UndoLabel;

    public string? RedoLabel => _history.RedoLabel;

    public int UndoDepth => _history.UndoDepth;

    public int RedoDepth => _history.RedoDepth;

    public long EstimatedBytes => _history.EstimatedBytes;

    public void ConfigureHistoryBudget(int maxEntries, long maxBytes)
        => _history.ConfigureBudget(maxEntries, maxBytes);

    public DocumentHistoryState CaptureHistoryState() => _history.CaptureState();

    public void RestoreHistoryState(DocumentHistoryState state) => _history.RestoreState(state);

    public void EndCoalescing() => _history.EndCoalescing();

    public void MarkConfigurationChanged() => Revision++;

    /// <summary>开始事务边界：所有文档修改（结构/对齐/属性/手势）共用。</summary>
    public void BeginTransaction() => _history.BeginTransaction(DocumentCommands.Edit, "Edit");

    /// <summary>使用指定命令 id 与标签开始事务。</summary>
    public void BeginTransaction(string commandId, string label)
        => _history.BeginTransaction(commandId, label);

    /// <summary>直接修改属性前记录当前值（手势宿主使用）。</summary>
    public void RecordBeforeChange(IEnumerable<Primitive> primitives)
        => _history.RecordBefore(primitives);

    internal void AttachConfigHistory(IHistoryConfigParticipant config)
        => _history.AttachConfig(config);

    /// <summary>提交事务边界，生成一个撤销条目。</summary>
    public void CommitTransaction() => _history.CommitTransaction();

    /// <summary>取消事务边界并精确恢复到事务前状态（手势取消）。</summary>
    public void CancelTransaction() => _history.CancelTransaction();

    /// <summary>对齐当前选区；修改可撤销，已对齐时不产生历史条目。</summary>
    public bool Align(AlignType type)
    {
        var selection = _selection.ToList();
        if (selection.Count < 2)
        {
            return false;
        }

        _history.BeginTransaction(DocumentCommands.Align, $"Align {type}");
        _history.RecordBefore(selection);
        var changed = AlignExtensions.Align<Primitive>(selection, type);
        if (!changed)
        {
            _history.DiscardTransaction();
            return false;
        }

        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Geometry);
        return true;
    }

    /// <summary>校验并写入属性文本；非法值不写入、不产生历史条目。</summary>
    public bool TrySetProperty(Primitive owner, PropValue property, string text, out string? error)
    {
        if (!owner.Props.Contains(property))
        {
            error = "The property does not belong to this primitive.";
            return false;
        }

        return TrySetProperty(new[] { owner }, property.Name, text, out error);
    }

    /// <summary>
    /// 一次编辑原子地写入多个图元的同名属性。先逐项校验文本；
    /// 任一项失败时恢复所有已写入的值，成功时只提交一个撤销条目。
    /// </summary>
    public bool TrySetProperty(
        IReadOnlyList<Primitive> owners,
        string propertyName,
        string text,
        out string? error)
    {
        if (owners is null || owners.Count == 0)
        {
            error = "Select at least one primitive.";
            return false;
        }

        var distinctOwners = owners.Distinct().ToList();
        var properties = new List<PropValue>(distinctOwners.Count);
        Type? propertyType = null;
        foreach (var owner in distinctOwners)
        {
            var property = owner.Props.FirstOrDefault(
                candidate => string.Equals(candidate.Name, propertyName, StringComparison.Ordinal));
            if (property is null)
            {
                error = $"Property '{propertyName}' is not available on every selected primitive.";
                return false;
            }

            propertyType ??= property.GetType();
            if (property.GetType() != propertyType)
            {
                error = $"Property '{propertyName}' has different types across the selection.";
                return false;
            }

            properties.Add(property);
        }

        var before = properties.Select(property => property.GetBoxedValue()).ToList();
        _history.BeginTransaction(DocumentCommands.SetProperty, $"Set {propertyName}");
        _history.RecordBefore(distinctOwners);
        try
        {
            foreach (var property in properties)
            {
                if (!property.TrySetText(text, out error))
                {
                    RestoreValues(properties, before);
                    _history.DiscardTransaction();
                    return false;
                }
            }
        }
        catch
        {
            _history.CancelTransaction();
            throw;
        }

        if (before.SequenceEqual(properties.Select(property => property.GetBoxedValue())))
        {
            _history.DiscardTransaction();
            error = null;
            return true;
        }

        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Geometry);
        error = null;
        return true;
    }

    /// <summary>按世界空间增量移动当前顶层选区；无选区或零位移时不产生历史条目。</summary>
    public bool MoveSelection(float deltaX, float deltaY)
    {
        var selection = _selection.ToList();
        if (selection.Count == 0 || (deltaX == 0f && deltaY == 0f))
        {
            return false;
        }

        _history.BeginTransaction(DocumentCommands.MoveSelection, "Move selection");
        _history.RecordBefore(selection);
        foreach (var primitive in selection)
        {
            primitive.Translate(deltaX, deltaY);
        }

        _history.CommitTransaction();
        RaiseChanged(DocumentChangeKind.Geometry);
        return true;
    }

    public void SetSelected(Primitive primitive) => ReplaceSelection(new[] { primitive });

    public void SetSelectedRange(IEnumerable<Primitive> primitives) => ReplaceSelection(primitives);

    public void AppendSelected(Primitive primitive) => ReplaceSelection(_selection.Append(primitive));

    public void ClearSelection()
    {
        _history.EndCoalescing();
        _selection.Clear();
        RaiseChanged(DocumentChangeKind.Selection);
    }

    /// <summary>选区的只读快照；调用方持有的旧快照不随后续选择变化。</summary>
    public IReadOnlyList<Primitive> GetSelection() => _selection.ToArray();

    public DocumentState GetState() => new(_primitives, _selection);

    public void NotifyPrimitivesChanged(IEnumerable<Primitive> primitives)
        => RaiseChanged(DocumentChangeKind.Geometry);

    public void NotifyTransientValuesChanged()
        => RaiseChanged(DocumentChangeKind.Transient);

    /// <summary>所有选择入口统一规范化：祖先已选中时排除其后代，避免双重变换。</summary>
    private void ReplaceSelection(IEnumerable<Primitive> primitives)
    {
        _history.EndCoalescing();
        var normalized = NormalizeSelection(primitives);
        _selection.Clear();
        foreach (var primitive in normalized)
        {
            _selection.Add(primitive);
        }

        RaiseChanged(DocumentChangeKind.Selection);
    }

    private void RaiseChanged(DocumentChangeKind kind)
        => Changed?.Invoke(new DocumentChange(kind, GetState()));

    /// <summary>图元是否真实连接到本文档的根列表（父链与子列表双向一致）。</summary>
    private bool IsInDocument(Primitive primitive)
    {
        var node = primitive;
        while (node.Parent is Container parent)
        {
            if (!parent.Children.Contains(node))
            {
                return false;
            }

            node = parent;
        }

        return _primitives.Contains(node);
    }

    private static bool IsDescendantOf(Primitive node, Primitive ancestor)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOf(IReadOnlyList<Primitive> list, Primitive item)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (ReferenceEquals(list[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>历史应用使用的根列表引用；调用方不得通过它修改结构。</summary>
    internal IReadOnlyList<Primitive> RootsInternal => _primitives;

    internal void InsertRootInternal(Primitive node, int index)
    {
        if ((uint)index > (uint)_primitives.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _primitives.Insert(index, node);
        node.SetOwnerRecursive(this);
    }

    internal void RemoveRootInternal(Primitive node)
    {
        if (_primitives.Remove(node))
        {
            node.SetOwnerRecursive(null);
        }
    }

    internal void InsertChildInternal(Container parent, Primitive child, int index)
    {
        if (!IsInDocument(parent))
        {
            throw new InvalidOperationException("The container must belong to this document.");
        }

        parent.InsertChildAt(child, index);
        child.SetOwnerRecursive(this);
    }

    internal void RestoreSelectionInternal(IReadOnlyList<Primitive> selection)
    {
        var normalized = NormalizeSelection(selection.Where(IsInDocument));
        _selection.Clear();
        foreach (var primitive in normalized)
        {
            _selection.Add(primitive);
        }
    }

    internal void InstallDocumentState(
        IReadOnlyList<Primitive> roots,
        IReadOnlyList<Primitive> selection)
    {
        var previous = _primitives.ToArray();
        _primitives.Clear();
        _primitives.AddRange(roots);
        foreach (var root in previous)
        {
            if (!_primitives.Contains(root))
            {
                root.SetOwnerRecursive(null);
            }
        }

        foreach (var root in _primitives)
        {
            root.SetOwnerRecursive(this);
        }

        RestoreSelectionInternal(selection);
    }

    /// <summary>撤销/重做/恢复状态后统一发出一次文档变更通知。</summary>
    internal void NotifyHistoryApplied()
        => RaiseChanged(
            DocumentChangeKind.Structure
            | DocumentChangeKind.Geometry
            | DocumentChangeKind.Selection);

    private static string Describe(Primitive primitive) => primitive switch
    {
        Container container => $"{container.Kind} container",
        Circle => "circle",
        Rectangle => "rectangle",
        Triangle => "triangle",
        _ => "primitive",
    };

    /// <summary>拒绝在历史/序列化无法表达的属性子类型进入文档。</summary>
    private static void ValidateSupportedProperties(Primitive primitive)
    {
        foreach (var property in primitive.Props)
        {
            _ = property.GetBoxedValue();
        }

        if (primitive is Container container)
        {
            foreach (var child in container.Children)
            {
                ValidateSupportedProperties(child);
            }
        }
    }

    private static void RestoreValues(IReadOnlyList<PropValue> properties, IReadOnlyList<object?> values)
    {
        for (var index = 0; index < properties.Count; index++)
        {
            if (values[index] is { } value)
            {
                properties[index].SetBoxedValue(value);
            }
        }
    }

    private static List<Primitive> NormalizeSelection(IEnumerable<Primitive> primitives)
    {
        var list = primitives.Distinct().ToList();
        var selected = new HashSet<Primitive>(list);
        return list.Where(p => !HasSelectedAncestor(p, selected)).ToList();
    }

    private static bool HasSelectedAncestor(Primitive primitive, HashSet<Primitive> selected)
    {
        for (var node = primitive.Parent; node is not null; node = node.Parent)
        {
            if (selected.Contains(node))
            {
                return true;
            }
        }

        return false;
    }

    public event Action<DocumentChange>? Changed;
}
