namespace Penelopa.Core.Alignment;

/// <summary>
/// 可在世界空间中对齐的对象契约。对齐只涉及平移，因此接口暴露锚点位置而非完整仿射变换。
/// </summary>
public interface IAlignable
{
    /// <summary>获取世界空间轴对齐包围盒（AABB）。</summary>
    Box GetWorldBoundingBox();

    /// <summary>获取世界空间锚点位置（AABB 左上角）。</summary>
    Point GetWorldPosition();
}
