using System.Text.Json;
using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;

namespace Penelopa.Web.Services;

/// <summary>
/// 把现有 UI/服务的写入与读取转发到当前活动文档会话。
/// 会话切换时重新订阅底层 Changed，并用一次 Selection 刷新面板，
/// 避免把切换误判为文档编辑。
/// </summary>
public sealed class ActivePrimitiveService : IPrimitiveService
{
    private readonly DocumentSessionManager _manager;
    private PrimitiveService? _subscribed;

    public ActivePrimitiveService(DocumentSessionManager manager)
    {
        _manager = manager;
    }

    public event Action<DocumentChange>? Changed;

    private IPrimitiveService Current => _manager.Active.Primitives;

    public Guid DocumentId => Current.DocumentId;

    public long Revision => Current.Revision;

    public IReadOnlyDictionary<string, JsonElement> Extensions => Current.Extensions;

    public bool CanUndo => Current.CanUndo;

    public bool CanRedo => Current.CanRedo;

    public string? UndoLabel => Current.UndoLabel;

    public string? RedoLabel => Current.RedoLabel;

    public int UndoDepth => Current.UndoDepth;

    public int RedoDepth => Current.RedoDepth;

    public long EstimatedBytes => Current.EstimatedBytes;

    internal void Attach(PrimitiveService primitives)
    {
        if (_subscribed is not null)
        {
            _subscribed.Changed -= Forward;
        }

        _subscribed = primitives;
        _subscribed.Changed += Forward;
        Changed?.Invoke(new DocumentChange(
            DocumentChangeKind.Structure
            | DocumentChangeKind.Geometry
            | DocumentChangeKind.Selection,
            primitives.GetState()));
    }

    private void Forward(DocumentChange change) => Changed?.Invoke(change);

    public void SetDocumentIdentity(Guid documentId, long revision)
        => Current.SetDocumentIdentity(documentId, revision);

    public void SetExtensions(IReadOnlyDictionary<string, JsonElement>? extensions)
        => Current.SetExtensions(extensions);

    public void Add(Primitive primitive) => Current.Add(primitive);

    public void Remove(Primitive primitive) => Current.Remove(primitive);

    public void RemoveRange(IEnumerable<Primitive> primitives) => Current.RemoveRange(primitives);

    public IReadOnlyList<Primitive> GetAll() => Current.GetAll();

    public void AddToContainer(Container container, Primitive child)
        => Current.AddToContainer(container, child);

    public Container? GroupSelection(out string? error)
        => Current.GroupSelection(out error);

    public bool TryUngroup(Container container, out string? error)
        => Current.TryUngroup(container, out error);

    public void ReplaceAll(IReadOnlyList<Primitive> primitives, bool clearHistory = true)
        => Current.ReplaceAll(primitives, clearHistory);

    public void Undo() => Current.Undo();

    public void Redo() => Current.Redo();

    public void ConfigureHistoryBudget(int maxEntries, long maxBytes)
        => Current.ConfigureHistoryBudget(maxEntries, maxBytes);

    public DocumentHistoryState CaptureHistoryState()
        => Current.CaptureHistoryState();

    public void RestoreHistoryState(DocumentHistoryState state)
        => Current.RestoreHistoryState(state);

    public void EndCoalescing() => Current.EndCoalescing();

    public void MarkConfigurationChanged() => Current.MarkConfigurationChanged();

    public void BeginTransaction() => Current.BeginTransaction();

    public void BeginTransaction(string commandId, string label)
        => Current.BeginTransaction(commandId, label);

    public void RecordBeforeChange(IEnumerable<Primitive> primitives)
        => Current.RecordBeforeChange(primitives);

    public void CommitTransaction() => Current.CommitTransaction();

    public void CancelTransaction() => Current.CancelTransaction();

    public bool Align(AlignType type) => Current.Align(type);

    public bool TrySetProperty(
        Primitive owner,
        PropValue property,
        string text,
        out string? error)
        => Current.TrySetProperty(owner, property, text, out error);

    public bool TrySetProperty(
        IReadOnlyList<Primitive> owners,
        string propertyName,
        string text,
        out string? error)
        => Current.TrySetProperty(owners, propertyName, text, out error);

    public bool MoveSelection(float deltaX, float deltaY)
        => Current.MoveSelection(deltaX, deltaY);

    public void SetSelected(Primitive primitive) => Current.SetSelected(primitive);

    public void SetSelectedRange(IEnumerable<Primitive> primitives)
        => Current.SetSelectedRange(primitives);

    public void AppendSelected(Primitive primitive) => Current.AppendSelected(primitive);

    public void ClearSelection() => Current.ClearSelection();

    public IReadOnlyList<Primitive> GetSelection() => Current.GetSelection();

    public DocumentState GetState() => Current.GetState();

    public void NotifyPrimitivesChanged(IEnumerable<Primitive> primitives)
        => Current.NotifyPrimitivesChanged(primitives);

    public void NotifyTransientValuesChanged()
        => Current.NotifyTransientValuesChanged();
}
