using Penelopa.Core.Primitives;

namespace Penelopa.Core.Alignment;

/// <summary>
/// 将一组 <see cref="IAlignable"/> 项对齐到它们包围盒并集的同一参考值（边或中心）。
/// </summary>
internal static class AlignExtensions
{
    private const float Tolerance = 1e-6f;

    /// <summary>对一组可对齐项执行对齐操作。</summary>
    /// <typeparam name="T">可对齐项类型。</typeparam>
    /// <param name="items">待对齐的项集合。</param>
    /// <param name="type">对齐方向。</param>
    /// <returns>已执行对齐返回 true；项数少于两个或已经对齐则返回 false。</returns>
    internal static bool Align<T>(this IEnumerable<T> items, AlignType type) where T : Primitive
    {
        var list = items.ToList();
        if (list.Count < 2)
        {
            return false;
        }

        // 快照每项当前状态
        var boxes = list.Select(item => item.GetWorldBoundingBox()).ToList();
        var originalPositions = list.Select(item => item.GetWorldPosition()).ToList();

        // 所有包围盒的并集作为对齐参考
        var unionBox = MergeBoxes(boxes);

        if (IsAlreadyAligned(boxes, type, unionBox))
        {
            return false;
        }

        // 按偏移量逐项平移
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var box = boxes[i];
            var originalPosition = originalPositions[i];

            var (dx, dy) = CalculateOffset(box, type, unionBox);

            item.SetWorldPosition(new Point(originalPosition.X + dx, originalPosition.Y + dy));
        }

        return true;
    }

    private static Box MergeBoxes(IReadOnlyList<Box> boxes)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var box in boxes)
        {
            if (box.MinX < minX) minX = box.MinX;
            if (box.MinY < minY) minY = box.MinY;
            if (box.MaxX > maxX) maxX = box.MaxX;
            if (box.MaxY > maxY) maxY = box.MaxY;
        }

        return new Box(minX, minY, maxX, maxY);
    }

    private static bool IsAlreadyAligned(IReadOnlyList<Box> boxes, AlignType type, Box referenceBox)
    {
        float referenceValue = GetAlignmentValue(referenceBox, type);

        return boxes.All(box => Math.Abs(GetAlignmentValue(box, type) - referenceValue) < Tolerance);
    }

    private static (float dx, float dy) CalculateOffset(Box box, AlignType type, Box referenceBox)
    {
        return type switch
        {
            // 屏幕 Y 轴向下增长：视觉顶部 = MaxY，底部 = MinY
            AlignType.Left => (referenceBox.MinX - box.MinX, 0),
            AlignType.HCenter => (referenceBox.CenterX - box.CenterX, 0),
            AlignType.Right => (referenceBox.MaxX - box.MaxX, 0),
            AlignType.Top => (0, referenceBox.MaxY - box.MaxY),
            AlignType.VCenter => (0, referenceBox.CenterY - box.CenterY),
            AlignType.Bottom => (0, referenceBox.MinY - box.MinY),
            _ => throw new ArgumentException("Invalid alignment type", nameof(type)),
        };
    }

    private static float GetAlignmentValue(Box box, AlignType type) => type switch
    {
        AlignType.Left => box.MinX,
        AlignType.HCenter => box.CenterX,
        AlignType.Right => box.MaxX,
        AlignType.Top => box.MaxY,
        AlignType.VCenter => box.CenterY,
        AlignType.Bottom => box.MinY,
        _ => throw new ArgumentException("Invalid alignment type", nameof(type)),
    };
}
