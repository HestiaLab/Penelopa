using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;

namespace Penelopa.Core.Services;

/// <summary>管理图元集合和当前选区。</summary>
public interface IPrimitiveService
{
    void Add(Primitive primitive);
    void Remove(Primitive primitive);

    /// <summary>删除多个图元；整批修改只产生一个撤销条目，并清理已失效的选区。</summary>
    void RemoveRange(IEnumerable<Primitive> primitives);

    /// <summary>当前根图元的只读快照；后续结构修改不会改变已返回的集合。</summary>
    IReadOnlyList<Primitive> GetAll();

    /// <summary>将根图元移入容器；校验同一文档、非自身、非祖先/后代与重复父级。</summary>
    void AddToContainer(Container container, Primitive child);

    /// <summary>
    /// 将至少两个根级选中项放入新的 identity 偏移容器，容器插入到第一个选中根的位置；
    /// 子项保持世界位置，结构与选区在同一个事务中提交。失败时返回 null 并给出原因。
    /// </summary>
    Container? GroupSelection(out string? error);

    /// <summary>
    /// 展开一个 identity 根容器，把子项按原顺序放回根列表并选中它们。
    /// 嵌套或非 identity 容器会被拒绝（否则无法保持世界位置），不产生历史条目。
    /// </summary>
    bool TryUngroup(Container container, out string? error);

    void ReplaceAll(IReadOnlyList<Primitive> primitives, bool clearHistory = true);
    void Undo();
    void Redo();
    bool CanUndo { get; }
    bool CanRedo { get; }

    /// <summary>开始事务边界；所有文档修改都通过它进入历史。</summary>
    void BeginTransaction();

    /// <summary>提交事务边界，生成一个撤销条目。</summary>
    void CommitTransaction();

    /// <summary>取消事务边界并恢复到事务前状态。</summary>
    void CancelTransaction();

    void CaptureForGesture();

    /// <summary>对齐当前选区，返回是否发生移动；修改可撤销。</summary>
    bool Align(AlignType type);

    /// <summary>校验并写入属性文本；非法值不写入、不产生历史条目。</summary>
    bool TrySetProperty(Primitive owner, PropValue property, string text, out string? error);

    /// <summary>
    /// 把同一属性的一次编辑原子地应用到多个图元；任一图元拒绝时回滚全部写入，
    /// 成功时只产生一个撤销条目。
    /// </summary>
    bool TrySetProperty(IReadOnlyList<Primitive> owners, string propertyName, string text, out string? error);

    /// <summary>按世界空间增量移动当前顶层选区；每个调用产生一个撤销条目。</summary>
    bool MoveSelection(float deltaX, float deltaY);

    void SetSelected(Primitive primitive);
    void SetSelectedRange(IEnumerable<Primitive> primitives);
    void AppendSelected(Primitive primitive);
    void ClearSelection();

    /// <summary>当前选区的只读快照；后续选择修改不会改变已返回的集合。</summary>
    IReadOnlyList<Primitive> GetSelection();

    /// <summary>获取当前根图元与选区的只读快照。面板初始化时使用。</summary>
    DocumentState GetState();

    /// <summary>通知指定图元的几何已变更。</summary>
    void NotifyPrimitivesChanged(IEnumerable<Primitive> primitives);

    /// <summary>统一的文档变更通知；所有状态变化（含加载、取消、撤销）都会触发。</summary>
    event Action<DocumentChange>? Changed;
}
