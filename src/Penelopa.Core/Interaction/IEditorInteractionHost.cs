using Penelopa.Core.Primitives;

namespace Penelopa.Core.Interaction;

/// <summary>
/// 交互控制器驱动的编辑侧接口：选择管理与几何变更通知。
/// 由宿主组件实现，使控制器不依赖 Blazor/Skia，保持可单测。
/// </summary>
public interface IEditorInteractionHost
{
    /// <summary>获取当前选区。</summary>
    IReadOnlyList<Primitive> GetSelection();

    /// <summary>判断图元是否已选中。</summary>
    bool IsSelected(Primitive primitive);

    /// <summary>仅选中指定图元。</summary>
    void SetSelected(Primitive primitive);

    /// <summary>将图元加入选区。</summary>
    void AppendSelected(Primitive primitive);

    /// <summary>切换图元的选中状态。</summary>
    void ToggleSelected(Primitive primitive);

    /// <summary>清空选区。</summary>
    void ClearSelection();

    /// <summary>
    /// 按 CSS 像素增量平移视口。空白拖拽手势使用；控制器报告 CSS 增量是因为平移是视口操作而非世界空间几何变更。
    /// </summary>
    void PanByCss(float deltaX, float deltaY);

    /// <summary>
    /// 通知宿主即将开始修改手势（拖拽/缩放），以便在首次几何变更前打开事务快照。每个手势仅触发一次。
    /// </summary>
    void BeginMutation();

    /// <summary>提交修改手势，生成一个撤销条目。</summary>
    void CommitMutation();

    /// <summary>取消修改手势，恢复到手势前状态。</summary>
    void CancelMutation();

    /// <summary>通知面板指定图元的几何已变更（拖拽/缩放提交或取消时触发）。</summary>
    void NotifyPrimitivesChanged(IReadOnlyList<Primitive> primitives);
}
