using Penelopa.Core.Primitives;

namespace Penelopa.Core.Services;

/// <summary>一次文档变更涉及的语义类别（可组合）。</summary>
[Flags]
public enum DocumentChangeKind
{
    None = 0,
    Structure = 1,
    Selection = 2,
    Geometry = 4,

    /// <summary>
    /// 预览/仿真结束后的临时值刷新：模型已恢复基础值，但不是用户对文档的持久编辑。
    /// 依赖"内容是否变化"的订阅方应忽略此标志。
    /// </summary>
    Transient = 8,
}

/// <summary>统一的文档变更通知：变更类别 + 变更后的完整状态快照。</summary>
public readonly record struct DocumentChange(DocumentChangeKind Kind, DocumentState State);

/// <summary>
/// 文档的只读状态快照（根图元与选区）。图元本身仍是可变模型对象，
/// 但根列表和选区列表是副本，订阅方不会看到后续结构修改。
/// </summary>
public sealed class DocumentState
{
    public static DocumentState Empty { get; } = new(Array.Empty<Primitive>(), Array.Empty<Primitive>());

    public DocumentState(IEnumerable<Primitive> roots, IEnumerable<Primitive> selection)
    {
        Roots = roots.ToArray();
        Selection = selection.ToArray();
    }

    /// <summary>根图元（按渲染顺序）。</summary>
    public IReadOnlyList<Primitive> Roots { get; }

    /// <summary>当前选区（已规范化，祖先已选中时不含后代）。</summary>
    public IReadOnlyList<Primitive> Selection { get; }
}
