using System.Collections.ObjectModel;
using Penelopa.Core.Alignment;
using Penelopa.Core.Services;

namespace Penelopa.Core.Primitives;

/// <summary>
/// 几何图元基类：暴露一组命名属性，可在世界空间中平移。
/// </summary>
public abstract class Primitive : IAlignable
{
    private readonly List<PropValue> _props = new();
    private readonly ReadOnlyCollection<PropValue> _readOnlyProps;

    protected Primitive(string name)
    {
        Id = Guid.NewGuid();
        _readOnlyProps = _props.AsReadOnly();

        Name = new StringPropValue("Name", name, allowEmpty: false, maxLength: 128, trim: true);
        AddProp(Name);
    }

    public Guid Id { get; internal set; }

    public StringPropValue Name { get; }

    /// <summary>属性包的只读视图；属性只能在构造期间通过 <see cref="AddProp"/> 注册。</summary>
    public IReadOnlyList<PropValue> Props => _readOnlyProps;

    protected void AddProp(PropValue propValue)
    {
        _props.Add(propValue);
    }

    /// <summary>父容器，根图元为 null。</summary>
    public Primitive? Parent { get; internal set; }

    /// <summary>所属文档服务；仅由 <see cref="PrimitiveService"/> 维护，用于拒绝跨文档结构操作。</summary>
    internal PrimitiveService? Owner { get; private set; }

    /// <summary>递归设置文档归属；容器结构变更时由服务或 <see cref="Container"/> 调用。</summary>
    internal void SetOwnerRecursive(PrimitiveService? owner)
    {
        Owner = owner;
        if (this is Container container)
        {
            foreach (var child in container.Children)
            {
                child.SetOwnerRecursive(owner);
            }
        }
    }

    /// <summary>世界空间轴对齐包围盒。</summary>
    /// <remarks>叶子图元返回局部包围盒，基类通过父容器链映射到世界空间。</remarks>
    public Box GetWorldBoundingBox()
    {
        var box = GetLocalBoundingBox();
        for (var node = Parent; node is not null; node = node.Parent)
        {
            if (node is Container container)
            {
                box = container.TransformBoundsToParent(box);
            }
        }

        return box;
    }

    /// <summary>图元在自身（父相对）空间中的包围盒。</summary>
    protected abstract Box GetLocalBoundingBox();

    /// <summary>内部桥接：供容器查询子图元的局部包围盒。</summary>
    internal Box GetParentLocalBoundingBox() => GetLocalBoundingBox();

    /// <summary>
    /// 按世界空间增量平移图元。容器内的子图元通过父链转换增量，确保在旋转容器内移动也跟随指针。
    /// </summary>
    public void Translate(float deltaX, float deltaY)
    {
        if (Parent is Container container)
        {
            var local = container.ToLocalVector(deltaX, deltaY);
            TranslateLocal(local.X, local.Y);
        }
        else
        {
            TranslateLocal(deltaX, deltaY);
        }
    }

    /// <summary>在局部空间中按增量移动。</summary>
    protected abstract void TranslateLocal(float deltaX, float deltaY);

    /// <summary>将图元几何拟合到给定的世界空间包围盒。</summary>
    /// <param name="bounds">目标世界空间轴对齐包围盒。</param>
    /// <param name="anchor">缩放手势的固定角。会重新居中的形状（如圆）保持此点在边界上；
    /// 直接填充的形状忽略此参数。</param>
    /// <returns>实际拟合后的世界空间包围盒。保持宽高比的形状可能不完全填满目标，
    /// 调用方须用返回值修正选区叠加层。</returns>
    /// <remarks>
    /// 世界空间到父级局部空间的转换只在派生类的 <see cref="SetBoundsLocal"/> 调用前发生一次；
    /// 该局部方法只接收和返回父级局部包围盒。
    /// </remarks>
    public Box SetBounds(Box bounds, Point anchor)
    {
        if (Parent is Container container)
        {
            var localBounds = container.WorldToLocalBounds(bounds);
            var localAnchor = container.ToLocalPoint(anchor);
            return container.ToWorldBounds(SetBoundsLocal(localBounds, localAnchor));
        }

        return SetBoundsLocal(bounds, anchor);
    }

    /// <summary>在父级局部空间中拟合到给定包围盒；入参和返回值都是局部坐标。</summary>
    protected abstract Box SetBoundsLocal(Box bounds, Point anchor);

    /// <summary>
    /// 是否可以按世界轴对齐包围盒精确缩放：自身或任一祖先容器含旋转/剪切时为 false。
    /// 此时世界 AABB 无法用局部轴缩放精确表达，交互层应禁用角柄而不是画出错位的框。
    /// </summary>
    public bool CanResizeInWorld
    {
        get
        {
            for (var node = this; node is not null; node = node.Parent)
            {
                if (node is Container container && !container.LocalTransform.IsAxisAligned)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>判断给定的世界空间点是否在图元形状内。用于穿透候选收集和框选测试。</summary>
    public bool ContainsWorldPoint(Point point)
    {
        if (this is Container self)
        {
            return ContainsLocalPoint(self.ToLocalPoint(point));
        }

        if (Parent is Container parent)
        {
            return ContainsLocalPoint(parent.ToLocalPoint(point));
        }

        return ContainsLocalPoint(point);
    }

    /// <summary>在图元局部空间中测试点是否在形状内。</summary>
    protected abstract bool ContainsLocalPoint(Point point);

    /// <summary>内部桥接：供容器测试子图元的局部形状。</summary>
    internal bool ContainsParentLocalPoint(Point point) => ContainsLocalPoint(point);

    /// <inheritdoc/>
    public Point GetWorldPosition() => GetCurrentPosition();

    /// <inheritdoc/>
    public void SetWorldPosition(Point position)
    {
        var currentPos = GetCurrentPosition();
        Translate(position.X - currentPos.X, position.Y - currentPos.Y);
    }

    /// <summary>屏幕坐标下包围盒的左上锚点（x 右增、y 下增，锚点为 (MinX, MaxY)）。</summary>
    protected Point GetCurrentPosition()
    {
        var bbox = GetWorldBoundingBox();
        return new Point(bbox.MinX, bbox.MaxY);
    }
}

/// <summary>圆形：中心 + 半径。</summary>
public class Circle : Primitive
{
    public Circle() : base("Circle")
    {
        CenterX = new FloatPropValue("CenterX", 10.0f);
        CenterY = new FloatPropValue("CenterY", 10.0f);
        Radius = new FloatPropValue("Radius", 5.0f, exclusiveMinimum: 0f);
        Color = new ColorPropValue("Color", 0xFFFFFFFF);

        AddProp(CenterX);
        AddProp(CenterY);
        AddProp(Radius);
        AddProp(Color);
    }

    public FloatPropValue CenterX { get; }
    public FloatPropValue CenterY { get; }
    public FloatPropValue Radius { get; }
    public ColorPropValue Color { get; }

    protected override Box GetLocalBoundingBox()
    {
        float left = CenterX.Value - Radius.Value;
        float top = CenterY.Value - Radius.Value;
        float right = CenterX.Value + Radius.Value;
        float bottom = CenterY.Value + Radius.Value;
        return new Box(left, top, right, bottom);
    }

    protected override void TranslateLocal(float deltaX, float deltaY)
    {
        CenterX.Value += deltaX;
        CenterY.Value += deltaY;
    }

    protected override Box SetBoundsLocal(Box bounds, Point anchor)
    {
        // 圆保持宽高比。锚点是缩放手势的固定角，保持在圆边界上：
        // 中心由锚点 + 内切半径推导，圆从固定角生长而非在目标框中重新居中。
        float radius = MathF.Max(0f, MathF.Min(bounds.Width, bounds.Height) * 0.5f);

        const float eps = 1e-4f;
        float centerX = anchor.X <= bounds.MinX + eps ? bounds.MinX + radius : bounds.MaxX - radius;
        float centerY = anchor.Y <= bounds.MinY + eps ? bounds.MinY + radius : bounds.MaxY - radius;

        CenterX.Value = centerX;
        CenterY.Value = centerY;
        Radius.Value = radius;
        return GetLocalBoundingBox();
    }

    protected override bool ContainsLocalPoint(Point point)
    {
        float dx = point.X - CenterX.Value;
        float dy = point.Y - CenterY.Value;
        return dx * dx + dy * dy <= Radius.Value * Radius.Value;
    }
}

/// <summary>矩形：左上角位置 + 尺寸。</summary>
public class Rectangle : Primitive
{
    public Rectangle() : base("Rectangle")
    {
        PosX = new FloatPropValue("PosX", 10.0f);
        PosY = new FloatPropValue("PosY", 10.0f);
        Width = new FloatPropValue("Width", 10.0f, exclusiveMinimum: 0f);
        Height = new FloatPropValue("Height", 10.0f, exclusiveMinimum: 0f);
        Color = new ColorPropValue("Color", 0xFFFFFFFF);

        AddProp(PosX);
        AddProp(PosY);
        AddProp(Width);
        AddProp(Height);
        AddProp(Color);
    }

    public FloatPropValue PosX { get; }
    public FloatPropValue PosY { get; }
    public FloatPropValue Width { get; }
    public FloatPropValue Height { get; }
    public ColorPropValue Color { get; }

    protected override Box GetLocalBoundingBox()
    {
        float left = PosX.Value;
        float top = PosY.Value;
        float right = PosX.Value + Width.Value;
        float bottom = PosY.Value + Height.Value;
        return new Box(left, top, right, bottom);
    }

    protected override void TranslateLocal(float deltaX, float deltaY)
    {
        PosX.Value += deltaX;
        PosY.Value += deltaY;
    }

    protected override Box SetBoundsLocal(Box bounds, Point anchor)
    {
        PosX.Value = bounds.MinX;
        PosY.Value = bounds.MinY;
        Width.Value = bounds.Width;
        Height.Value = bounds.Height;
        return bounds;
    }

    protected override bool ContainsLocalPoint(Point point)
    {
        var box = GetLocalBoundingBox();
        return point.X >= box.MinX && point.X <= box.MaxX
            && point.Y >= box.MinY && point.Y <= box.MaxY;
    }
}

/// <summary>三角形：三个顶点。</summary>
public class Triangle : Primitive
{
    public Triangle() : base("Triangle")
    {
        Vertex1X = new FloatPropValue("Vertex1X", 50.0f);
        Vertex1Y = new FloatPropValue("Vertex1Y", 50.0f);
        Vertex2X = new FloatPropValue("Vertex2X", 70.0f);
        Vertex2Y = new FloatPropValue("Vertex2Y", 50.0f);
        Vertex3X = new FloatPropValue("Vertex3X", 60.0f);
        Vertex3Y = new FloatPropValue("Vertex3Y", 60.0f);
        Color = new ColorPropValue("Color", 0xFFFFFFFF);

        AddProp(Vertex1X);
        AddProp(Vertex1Y);
        AddProp(Vertex2X);
        AddProp(Vertex2Y);
        AddProp(Vertex3X);
        AddProp(Vertex3Y);
        AddProp(Color);
    }

    public FloatPropValue Vertex1X { get; }
    public FloatPropValue Vertex1Y { get; }
    public FloatPropValue Vertex2X { get; }
    public FloatPropValue Vertex2Y { get; }
    public FloatPropValue Vertex3X { get; }
    public FloatPropValue Vertex3Y { get; }
    public ColorPropValue Color { get; }

    protected override Box GetLocalBoundingBox()
    {
        float minX = MathF.Min(Vertex1X.Value, MathF.Min(Vertex2X.Value, Vertex3X.Value));
        float minY = MathF.Min(Vertex1Y.Value, MathF.Min(Vertex2Y.Value, Vertex3Y.Value));
        float maxX = MathF.Max(Vertex1X.Value, MathF.Max(Vertex2X.Value, Vertex3X.Value));
        float maxY = MathF.Max(Vertex1Y.Value, MathF.Max(Vertex2Y.Value, Vertex3Y.Value));
        return new Box(minX, minY, maxX, maxY);
    }

    protected override void TranslateLocal(float deltaX, float deltaY)
    {
        Vertex1X.Value += deltaX; Vertex1Y.Value += deltaY;
        Vertex2X.Value += deltaX; Vertex2Y.Value += deltaY;
        Vertex3X.Value += deltaX; Vertex3Y.Value += deltaY;
    }

    protected override Box SetBoundsLocal(Box bounds, Point anchor)
    {
        // 按顶点在当前局部包围盒中的归一化位置映射，保持形状同时精确填充目标框。锚点隐含在框的角中。
        var current = GetLocalBoundingBox();
        float scaleX = current.Width > 1e-6f ? bounds.Width / current.Width : 0f;
        float scaleY = current.Height > 1e-6f ? bounds.Height / current.Height : 0f;

        Vertex1X.Value = bounds.MinX + (Vertex1X.Value - current.MinX) * scaleX;
        Vertex1Y.Value = bounds.MinY + (Vertex1Y.Value - current.MinY) * scaleY;
        Vertex2X.Value = bounds.MinX + (Vertex2X.Value - current.MinX) * scaleX;
        Vertex2Y.Value = bounds.MinY + (Vertex2Y.Value - current.MinY) * scaleY;
        Vertex3X.Value = bounds.MinX + (Vertex3X.Value - current.MinX) * scaleX;
        Vertex3Y.Value = bounds.MinY + (Vertex3Y.Value - current.MinY) * scaleY;
        return GetLocalBoundingBox();
    }

    protected override bool ContainsLocalPoint(Point point)
    {
        var box = GetLocalBoundingBox();
        if (box.Width < 1e-6f || box.Height < 1e-6f)
        {
            // 退化（零面积）三角形不可命中
            return false;
        }

        // 叉积符号测试：三条边测试同号则在内部（或边界上）
        float d1 = Cross(point, new Point(Vertex1X.Value, Vertex1Y.Value), new Point(Vertex2X.Value, Vertex2Y.Value));
        float d2 = Cross(point, new Point(Vertex2X.Value, Vertex2Y.Value), new Point(Vertex3X.Value, Vertex3Y.Value));
        float d3 = Cross(point, new Point(Vertex3X.Value, Vertex3Y.Value), new Point(Vertex1X.Value, Vertex1Y.Value));
        bool hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNegative && hasPositive);
    }

    private static float Cross(Point p, Point a, Point b)
        => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
}
