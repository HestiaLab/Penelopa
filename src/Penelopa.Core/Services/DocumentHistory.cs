using Penelopa.Core.Primitives;
using Penelopa.Core.Simulation;
using System.Text.Json;

namespace Penelopa.Core.Services;

/// <summary>稳定命令 id；用于历史标签、合并窗口、宏与后续审计。</summary>
public static class DocumentCommands
{
    public const string Edit = "penelopa.edit";
    public const string Add = "penelopa.structure.add";
    public const string MoveIntoContainer = "penelopa.structure.moveIntoContainer";
    public const string Group = "penelopa.structure.group";
    public const string Ungroup = "penelopa.structure.ungroup";
    public const string Delete = "penelopa.structure.delete";
    public const string Align = "penelopa.edit.align";
    public const string SetProperty = "penelopa.edit.setProperty";
    public const string MoveSelection = "penelopa.edit.move";
    public const string Gesture = "penelopa.edit.gesture";
    public const string SetKeyframe = "penelopa.simulation.setKeyframe";
    public const string AddTrack = "penelopa.simulation.addTrack";
    public const string RemoveTrack = "penelopa.simulation.removeTrack";
    public const string UpdateKeyframe = "penelopa.simulation.updateKeyframe";
    public const string RemoveKeyframe = "penelopa.simulation.removeKeyframe";
    public const string RemoveDormantTracks = "penelopa.simulation.removeDormantTracks";
}

/// <summary>历史系统读取/恢复仿真配置的参与者接口。</summary>
internal interface IHistoryConfigParticipant
{
    SimulationConfig GetConfigForHistory();

    /// <summary>撤销/重做配置补丁：恢复轨道但保留当前 Loop（Loop 不是历史状态）。</summary>
    void ApplyConfigFromHistory(SimulationConfig config);

    /// <summary>恢复完整配置（含 Loop），用于一次性状态/导入回滚。</summary>
    void RestoreConfigState(SimulationConfig config);
}

/// <summary>
/// 记录型补丁历史：事务内记录属性变更与结构操作，撤销时逆向应用。
/// 默认预算为 500 步 / 32 MB，重做条目计入同一预算；选中项作为条目元数据恢复。
/// </summary>
internal sealed class DocumentHistory
{
    internal const int DefaultMaxEntries = 500;
    internal const long DefaultMaxBytes = 32L * 1024 * 1024;
    private const double CoalesceWindowMilliseconds = 800d;

    private readonly PrimitiveService _service;
    private readonly TimeProvider _clock;
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();
    private IHistoryConfigParticipant? _config;
    private PendingTransaction? _pending;
    private int _depth;
    private int _maxEntries = DefaultMaxEntries;
    private long _maxBytes = DefaultMaxBytes;
    private long _undoWeight;
    private long _redoWeight;
    private int _coalesceEpoch;

    internal DocumentHistory(PrimitiveService service, TimeProvider? clock = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _clock = clock ?? TimeProvider.System;
    }

    internal bool CanUndo => _undo.Count > 0;

    internal bool CanRedo => _redo.Count > 0;

    internal bool IsTransactionOpen => _depth > 0;

    internal string? UndoLabel => _undo.Count > 0 ? _undo[^1].Label : null;

    internal string? RedoLabel => _redo.Count > 0 ? _redo[^1].Label : null;

    internal int UndoDepth => _undo.Count;

    internal int RedoDepth => _redo.Count;

    internal long EstimatedBytes => _undoWeight + _redoWeight;

    internal void AttachConfig(IHistoryConfigParticipant config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>开始事务；首次调用建立待提交条目，嵌套调用共享同一事务。</summary>
    internal void BeginTransaction(string commandId, string label)
    {
        if (_depth > 0)
        {
            _depth++;
            return;
        }

        _pending = new PendingTransaction(
            commandId,
            label,
            _service.GetSelection().ToArray(),
            _config?.GetConfigForHistory(),
            _coalesceEpoch);
        _depth = 1;
    }

    /// <summary>在首次修改前记录图元的全部属性值；同一事务内重复调用无副作用。</summary>
    internal void RecordBefore(IEnumerable<Primitive> primitives)
    {
        if (_pending is null)
        {
            return;
        }

        foreach (var primitive in primitives)
        {
            if (_pending.BeforeProps.ContainsKey(primitive))
            {
                continue;
            }

            var values = new Dictionary<string, object?>(primitive.Props.Count, StringComparer.Ordinal);
            foreach (var property in primitive.Props)
            {
                values[property.Name] = property.GetBoxedValue();
            }

            _pending.BeforeProps.Add(primitive, values);
        }
    }

    /// <summary>记录结构操作；索引语义为操作发生时刻的兄弟位置。</summary>
    internal void RecordStructure(StructureChange change)
    {
        _pending?.Structures.Add(change);
    }

    internal void CommitTransaction()
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

        var entry = BuildEntry(pending);
        if (entry is null)
        {
            return;
        }

        _redo.Clear();
        _redoWeight = 0;

        if (TryCoalesce(entry))
        {
            _service.BumpRevision();
            TrimBudget();
            return;
        }

        _undo.Add(entry);
        _undoWeight += entry.Weight;
        _service.BumpRevision();
        TrimBudget();
    }

    /// <summary>取消事务并精确恢复事务前状态（手势取消路径）。</summary>
    internal void CancelTransaction()
    {
        if (_depth == 0)
        {
            return;
        }

        var pending = _pending;
        _depth = 0;
        _pending = null;
        if (pending is null)
        {
            return;
        }

        var entry = BuildEntry(pending);
        if (entry is not null)
        {
            ApplyEntry(entry, forward: false);
        }

        _service.RestoreSelectionInternal(pending.SelectionBefore);
        _service.NotifyHistoryApplied();
    }

    /// <summary>丢弃事务快照但不恢复（未产生实际修改时使用）。</summary>
    internal void DiscardTransaction()
    {
        if (_depth == 0)
        {
            return;
        }

        _depth = 0;
        _pending = null;
    }

    internal void Undo()
    {
        if (_depth > 0 || _undo.Count == 0)
        {
            return;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _undoWeight -= entry.Weight;

        ApplyEntry(entry, forward: false);
        _service.RestoreSelectionInternal(entry.SelectionBefore);
        _redo.Add(entry);
        _redoWeight += entry.Weight;
        _service.BumpRevision();
        _service.NotifyHistoryApplied();
    }

    internal void Redo()
    {
        if (_depth > 0 || _redo.Count == 0)
        {
            return;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _redoWeight -= entry.Weight;

        ApplyEntry(entry, forward: true);
        _service.RestoreSelectionInternal(entry.SelectionAfter);
        _undo.Add(entry);
        _undoWeight += entry.Weight;
        _service.BumpRevision();
        _service.NotifyHistoryApplied();
    }

    internal void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _undoWeight = 0;
        _redoWeight = 0;
        _coalesceEpoch++;
    }

    /// <summary>结束当前合并窗口；下一次提交不会与之前的属性编辑合并。</summary>
    internal void EndCoalescing() => _coalesceEpoch++;

    internal void ConfigureBudget(int maxEntries, long maxBytes)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }

        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
        TrimBudget();
    }

    /// <summary>捕获当前文档与完整 undo/redo 栈，供一次性导入回滚使用。</summary>
    internal DocumentHistoryState CaptureState()
    {
        if (_depth > 0)
        {
            throw new InvalidOperationException(
                "Document history cannot be captured while a transaction is open.");
        }

        return new DocumentHistoryState(
            _service.DocumentId,
            _service.Revision,
            _service.Extensions,
            _service.GetAll().ToArray(),
            _service.GetSelection().ToArray(),
            _config?.GetConfigForHistory(),
            _undo.ToArray(),
            _redo.ToArray());
    }

    internal void RestoreState(DocumentHistoryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_depth > 0)
        {
            throw new InvalidOperationException(
                "Document history cannot be restored while a transaction is open.");
        }

        _config?.RestoreConfigState(state.Config ?? SimulationConfig.Empty);
        _service.SetDocumentIdentity(state.DocumentId, state.Revision);
        _service.SetExtensions(state.Extensions);
        _service.InstallDocumentState(state.CurrentRoots, state.Selection);
        _undo.Clear();
        _redo.Clear();
        _undo.AddRange(state.Undo);
        _redo.AddRange(state.Redo);
        _undoWeight = _undo.Sum(entry => entry.Weight);
        _redoWeight = _redo.Sum(entry => entry.Weight);
        TrimBudget();
        _service.NotifyHistoryApplied();
    }

    private HistoryEntry? BuildEntry(PendingTransaction pending)
    {
        var properties = new List<NodePropertyChange>();
        foreach (var (primitive, before) in pending.BeforeProps)
        {
            foreach (var property in primitive.Props)
            {
                if (!before.TryGetValue(property.Name, out var beforeValue))
                {
                    continue;
                }

                var afterValue = property.GetBoxedValue();
                if (!Equals(beforeValue, afterValue))
                {
                    properties.Add(new NodePropertyChange(
                        primitive,
                        property.Name,
                        beforeValue,
                        afterValue));
                }
            }
        }

        if (properties.Count == 0 && pending.Structures.Count == 0)
        {
            if (!TryBuildConfigPatch(pending, out var configBefore, out var configAfter))
            {
                return null;
            }

            return new HistoryEntry(
                pending.CommandId,
                pending.Label,
                _clock.GetUtcNow().UtcDateTime,
                properties,
                pending.Structures.ToArray(),
                configBefore,
                configAfter,
                pending.SelectionBefore,
                _service.GetSelection().ToArray(),
                pending.CoalesceEpoch);
        }

        TryBuildConfigPatch(pending, out var entryConfigBefore, out var entryConfigAfter);
        return new HistoryEntry(
            pending.CommandId,
            pending.Label,
            _clock.GetUtcNow().UtcDateTime,
            properties,
            pending.Structures.ToArray(),
            entryConfigBefore,
            entryConfigAfter,
            pending.SelectionBefore,
            _service.GetSelection().ToArray(),
            pending.CoalesceEpoch);
    }

    private bool TryBuildConfigPatch(
        PendingTransaction pending,
        out SimulationConfig? before,
        out SimulationConfig? after)
    {
        before = null;
        after = null;
        if (_config is null || pending.ConfigBefore is not { } configBefore)
        {
            return false;
        }

        var configAfter = _config.GetConfigForHistory();
        if (!ConfigTracksDiffer(configBefore, configAfter))
        {
            return false;
        }

        before = configBefore;
        after = configAfter;
        return true;
    }

    private static bool ConfigTracksDiffer(SimulationConfig before, SimulationConfig after)
    {
        if (ReferenceEquals(before, after))
        {
            return false;
        }

        if (before.Tracks.Count != after.Tracks.Count)
        {
            return true;
        }

        for (var index = 0; index < before.Tracks.Count; index++)
        {
            if (!ReferenceEquals(before.Tracks[index], after.Tracks[index]))
            {
                return true;
            }
        }

        // 仅 Loop 变化不属于历史状态。
        return false;
    }

    private bool TryCoalesce(HistoryEntry entry)
    {
        if (_undo.Count == 0 || _redo.Count > 0)
        {
            return false;
        }

        var top = _undo[^1];
        if (top.Structures.Count > 0 || top.Properties.Count != 1
            || entry.Structures.Count > 0 || entry.Properties.Count != 1
            || top.ConfigAfter is not null || entry.ConfigAfter is not null)
        {
            return false;
        }

        if (top.CoalesceEpoch != entry.CoalesceEpoch)
        {
            return false;
        }

        var previous = top.Properties[0];
        var next = entry.Properties[0];
        if (!string.Equals(top.CommandId, entry.CommandId, StringComparison.Ordinal)
            || !ReferenceEquals(previous.Node, next.Node)
            || !string.Equals(previous.PropertyName, next.PropertyName, StringComparison.Ordinal))
        {
            return false;
        }

        if ((entry.Timestamp - top.Timestamp).TotalMilliseconds > CoalesceWindowMilliseconds)
        {
            return false;
        }

        var mergedProperty = previous with { After = next.After };
        var merged = new HistoryEntry(
            top.CommandId,
            top.Label,
            entry.Timestamp,
            new[] { mergedProperty },
            Array.Empty<StructureChange>(),
            configBefore: null,
            configAfter: null,
            top.SelectionBefore,
            entry.SelectionAfter,
            entry.CoalesceEpoch);

        _undoWeight -= top.Weight;
        _undo[^1] = merged;
        _undoWeight += merged.Weight;
        return true;
    }

    private void ApplyEntry(HistoryEntry entry, bool forward)
    {
        if (forward)
        {
            if (entry.ConfigAfter is { } configAfter)
            {
                _config?.ApplyConfigFromHistory(configAfter);
            }

            foreach (var change in entry.Structures)
            {
                ApplyStructureForward(change);
            }

            foreach (var change in entry.Properties)
            {
                SetProperty(change.Node, change.PropertyName, change.After);
            }
        }
        else
        {
            if (entry.ConfigBefore is { } configBefore)
            {
                _config?.ApplyConfigFromHistory(configBefore);
            }

            for (var index = entry.Properties.Count - 1; index >= 0; index--)
            {
                var change = entry.Properties[index];
                SetProperty(change.Node, change.PropertyName, change.Before);
            }

            for (var index = entry.Structures.Count - 1; index >= 0; index--)
            {
                ApplyStructureBackward(entry.Structures[index]);
            }
        }
    }

    private void ApplyStructureForward(StructureChange change)
    {
        switch (change.Op)
        {
            case StructureOp.Insert:
                Attach(change.Node, change.ToParent, change.ToIndex);
                break;
            case StructureOp.Remove:
                Detach(change.Node);
                break;
            case StructureOp.Move:
                Detach(change.Node);
                Attach(change.Node, change.ToParent, change.ToIndex);
                break;
        }
    }

    private void ApplyStructureBackward(StructureChange change)
    {
        switch (change.Op)
        {
            case StructureOp.Insert:
                Detach(change.Node);
                break;
            case StructureOp.Remove:
                Attach(change.Node, change.FromParent, change.FromIndex);
                break;
            case StructureOp.Move:
                Detach(change.Node);
                Attach(change.Node, change.FromParent, change.FromIndex);
                break;
        }
    }

    private void Attach(Primitive node, Container? parent, int index)
    {
        if (node.Parent is not null || _service.RootsInternal.Contains(node))
        {
            Detach(node);
        }

        if (parent is null)
        {
            _service.InsertRootInternal(node, index);
        }
        else
        {
            _service.InsertChildInternal(parent, node, index);
        }
    }

    private void Detach(Primitive node)
    {
        if (node.Parent is Container parent)
        {
            parent.RemoveChild(node);
            return;
        }

        _service.RemoveRootInternal(node);
    }

    private static void SetProperty(Primitive node, string propertyName, object? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (var property in node.Props)
        {
            if (string.Equals(property.Name, propertyName, StringComparison.Ordinal))
            {
                property.SetBoxedValue(value);
                return;
            }
        }
    }

    private void TrimBudget()
    {
        while (_undo.Count + _redo.Count > _maxEntries || EstimatedBytes > _maxBytes)
        {
            if (_undo.Count > 0)
            {
                var entry = _undo[0];
                _undo.RemoveAt(0);
                _undoWeight -= entry.Weight;
            }
            else if (_redo.Count > 0)
            {
                var entry = _redo[0];
                _redo.RemoveAt(0);
                _redoWeight -= entry.Weight;
            }
            else
            {
                break;
            }
        }
    }

    private sealed class PendingTransaction
    {
        internal PendingTransaction(
            string commandId,
            string label,
            Primitive[] selectionBefore,
            SimulationConfig? configBefore,
            int coalesceEpoch)
        {
            CommandId = commandId;
            Label = label;
            SelectionBefore = selectionBefore;
            ConfigBefore = configBefore;
            CoalesceEpoch = coalesceEpoch;
        }

        internal string CommandId { get; }

        internal string Label { get; }

        internal Primitive[] SelectionBefore { get; }

        internal SimulationConfig? ConfigBefore { get; }

        internal int CoalesceEpoch { get; }

        internal Dictionary<Primitive, Dictionary<string, object?>> BeforeProps { get; } = new();

        internal List<StructureChange> Structures { get; } = new();
    }
}

/// <summary>文档历史的一次性完整快照；内容对调用方不透明。</summary>
public sealed class DocumentHistoryState
{
    internal DocumentHistoryState(
        Guid documentId,
        long revision,
        IReadOnlyDictionary<string, JsonElement> extensions,
        IReadOnlyList<Primitive> currentRoots,
        IReadOnlyList<Primitive> selection,
        SimulationConfig? config,
        IReadOnlyList<HistoryEntry> undo,
        IReadOnlyList<HistoryEntry> redo)
    {
        DocumentId = documentId;
        Revision = revision;
        Extensions = extensions;
        CurrentRoots = currentRoots;
        Selection = selection;
        Config = config;
        Undo = undo;
        Redo = redo;
    }

    internal Guid DocumentId { get; }

    internal long Revision { get; }

    internal IReadOnlyDictionary<string, JsonElement> Extensions { get; }

    internal IReadOnlyList<Primitive> CurrentRoots { get; }

    internal IReadOnlyList<Primitive> Selection { get; }

    internal SimulationConfig? Config { get; }

    internal IReadOnlyList<HistoryEntry> Undo { get; }

    internal IReadOnlyList<HistoryEntry> Redo { get; }
}

internal sealed class HistoryEntry
{
    internal HistoryEntry(
        string commandId,
        string label,
        DateTime timestamp,
        IReadOnlyList<NodePropertyChange> properties,
        IReadOnlyList<StructureChange> structures,
        SimulationConfig? configBefore,
        SimulationConfig? configAfter,
        IReadOnlyList<Primitive> selectionBefore,
        IReadOnlyList<Primitive> selectionAfter,
        int coalesceEpoch = 0)
    {
        CommandId = commandId;
        Label = label;
        Timestamp = timestamp;
        Properties = properties;
        Structures = structures;
        ConfigBefore = configBefore;
        ConfigAfter = configAfter;
        SelectionBefore = selectionBefore;
        SelectionAfter = selectionAfter;
        CoalesceEpoch = coalesceEpoch;
        Weight = EstimateWeight();
    }

    internal string CommandId { get; }

    internal string Label { get; }

    internal DateTime Timestamp { get; }

    internal IReadOnlyList<NodePropertyChange> Properties { get; }

    internal IReadOnlyList<StructureChange> Structures { get; }

    internal SimulationConfig? ConfigBefore { get; }

    internal SimulationConfig? ConfigAfter { get; }

    internal IReadOnlyList<Primitive> SelectionBefore { get; }

    internal IReadOnlyList<Primitive> SelectionAfter { get; }

    internal int CoalesceEpoch { get; }

    internal long Weight { get; }

    private long EstimateWeight()
    {
        long weight = 256 + (Label.Length * 2L);
        weight += Properties.Count * 200;
        foreach (var change in Structures)
        {
            weight += 256;
            if (change.Op is StructureOp.Insert or StructureOp.Remove)
            {
                weight += CountNodes(change.Node) * 512;
            }
        }

        weight += (SelectionBefore.Count + SelectionAfter.Count) * 16L;
        if (ConfigAfter is not null)
        {
            weight += 512;
            weight += ConfigWeight(ConfigBefore);
            weight += ConfigWeight(ConfigAfter);
        }

        return weight;
    }

    private static long ConfigWeight(SimulationConfig? config)
    {
        if (config is null)
        {
            return 0;
        }

        long weight = config.Tracks.Count * 16L;
        foreach (var track in config.Tracks)
        {
            weight += track.Keyframes.Count * 64L;
        }

        return weight;
    }

    private static long CountNodes(Primitive primitive)
    {
        long count = 1;
        if (primitive is Container container)
        {
            foreach (var child in container.Children)
            {
                count += CountNodes(child);
            }
        }

        return count;
    }
}

internal sealed record NodePropertyChange(
    Primitive Node,
    string PropertyName,
    object? Before,
    object? After);

internal enum StructureOp
{
    Insert,
    Remove,
    Move,
}

internal sealed record StructureChange(
    StructureOp Op,
    Primitive Node,
    Container? FromParent,
    int FromIndex,
    Container? ToParent,
    int ToIndex);
