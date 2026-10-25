using System.Collections.ObjectModel;
using Penelopa.Core.Alignment;

namespace Penelopa.Core.Primitives;

/// <summary>容器的编辑语义，决定属性面板暴露哪些变换属性。</summary>
public enum ContainerKind
{
    Offset,
    Rotation,
    Flip,
}

/// <summary>
/// 容器对子图元施加仿射变换（A661 风格的旋转/偏移/翻转均为同一类型的工厂形态）。
/// 变换由可编辑属性驱动的 TRS（平移·旋转·缩放）组合构成，属性面板可像操作普通图元一样修改偏移、旋转和缩放。
/// 仅暴露 <see cref="ContainerKind"/> 相关的属性供编辑。容器本身无可见形状，子图元在其变换内渲染，
/// 命中测试跟随渲染像素。世界包围盒为变换后子图元的 AABB，选区框和对齐无需特殊处理。
/// </summary>
public sealed class Container : Primitive
{
    private readonly List<Primitive> _children = new();
    private readonly ReadOnlyCollection<Primitive> _readOnlyChildren;

    public Container(
        string name,
        ContainerKind kind,
        float offsetX,
        float offsetY,
        float rotation,
        float scaleX,
        float scaleY)
        : base(name)
    {
        _readOnlyChildren = _children.AsReadOnly();
        Kind = kind;
        OffsetX = new FloatPropValue("Offset X", offsetX);
        OffsetY = new FloatPropValue("Offset Y", offsetY);
        Rotation = new FloatPropValue("Rotation", rotation);
        ScaleX = new FloatPropValue("Scale X", scaleX);
        ScaleY = new FloatPropValue("Scale Y", scaleY);

        switch (kind)
        {
            case ContainerKind.Offset:
                AddProp(OffsetX);
                AddProp(OffsetY);
                break;
            case ContainerKind.Rotation:
                AddProp(Rotation);
                break;
            case ContainerKind.Flip:
                AddProp(ScaleX);
                AddProp(ScaleY);
                break;
        }
    }

    /// <summary>容器编辑类型（决定暴露的属性集）。</summary>
    public ContainerKind Kind { get; }

    /// <summary>X 偏移属性（属性面板可编辑）。</summary>
    public FloatPropValue OffsetX { get; }

    /// <summary>Y 偏移属性（属性面板可编辑）。</summary>
    public FloatPropValue OffsetY { get; }

    /// <summary>旋转角度属性（属性面板可编辑）。</summary>
    public FloatPropValue Rotation { get; }

    /// <summary>X 缩放属性（负值翻转 X，属性面板可编辑）。</summary>
    public FloatPropValue ScaleX { get; }

    /// <summary>Y 缩放属性（负值翻转 Y，属性面板可编辑）。</summary>
    public FloatPropValue ScaleY { get; }

    /// <summary>
    /// 施加于子图元的变换（容器空间 → 父空间）。属性是真源，矩阵是 TRS 组合，面板编辑即时生效。
    /// </summary>
    public Transform LocalTransform
    {
        get
        {
            // 将缩放钳位远离零：零或近零缩放使变换奇异，Transform.Invert() 会抛异常导致子图元拖拽/缩放崩溃。
            // 属性面板仍保留原始值；生效的变换始终可逆。
            return Transform.Translate(OffsetX.Value, OffsetY.Value)
                .Multiply(Transform.Rotate(Rotation.Value))
                .Multiply(Transform.Scale(ClampScale(ScaleX.Value), ClampScale(ScaleY.Value)));
        }
    }

    /// <summary>钳位缩放幅值，保证局部变换可逆。</summary>
    private static float ClampScale(float value)
        => value >= 0f
            ? MathF.Max(value, MinScaleMagnitude)
            : MathF.Min(value, -MinScaleMagnitude);

    private const float MinScaleMagnitude = 0.01f;

    /// <summary>子图元的只读视图（按渲染顺序，首个在最底层）；结构变更必须经 <see cref="AddChild"/> / <see cref="RemoveChild"/>。</summary>
    public IReadOnlyList<Primitive> Children => _readOnlyChildren;

    /// <summary>创建偏移容器。</summary>
    public static Container CreateOffset(string name, float offsetX, float offsetY)
        => new(name, ContainerKind.Offset, offsetX, offsetY, 0f, 1f, 1f);

    /// <summary>创建旋转容器（角度制）。</summary>
    public static Container CreateRotation(string name, float degrees)
        => new(name, ContainerKind.Rotation, 0f, 0f, degrees, 1f, 1f);

    /// <summary>创建翻转容器，沿指定轴镜像。</summary>
    public static Container CreateFlip(string name, bool flipX, bool flipY)
        => new(name, ContainerKind.Flip, 0f, 0f, 0f, flipX ? -1f : 1f, flipY ? -1f : 1f);

    /// <summary>添加子图元，将其重新关联到本容器的局部空间。</summary>
    /// <remarks>子图元继承容器的文档归属；属于其他文档的图元会被拒绝。</remarks>
    public void AddChild(Primitive child)
    {
        if (child.Parent is not null)
        {
            throw new InvalidOperationException("A primitive can only have one parent.");
        }

        if (child.Owner is not null && !ReferenceEquals(child.Owner, Owner))
        {
            throw new InvalidOperationException("Cannot attach a primitive from another document.");
        }

        child.Parent = this;
        _children.Add(child);
        child.SetOwnerRecursive(Owner);
    }

    /// <summary>移除子图元，将其脱离到根级别。</summary>
    public void RemoveChild(Primitive child)
    {
        if (_children.Remove(child))
        {
            child.Parent = null;
            child.SetOwnerRecursive(null);
        }
    }

    /// <summary>将包围盒从本容器空间变换到父空间。</summary>
    public Box TransformBoundsToParent(Box local)
        => TransformBox(local, LocalTransform);

    /// <summary>将包围盒从本容器的父空间变换到本容器空间（只应用本层变换）。</summary>
    public Box ToLocalBounds(Box parent)
        => TransformBox(parent, LocalTransform.Invert());

    /// <summary>将世界空间的包围盒转换到本容器空间（沿祖先链逐级求逆）。</summary>
    public Box WorldToLocalBounds(Box world)
    {
        var box = Parent is Container parent ? parent.WorldToLocalBounds(world) : world;
        return TransformBox(box, LocalTransform.Invert());
    }

    /// <summary>将包围盒从本容器空间变换到世界空间。</summary>
    public Box ToWorldBounds(Box local)
    {
        var box = local;
        for (Primitive? node = this; node is not null; node = node.Parent)
        {
            if (node is Container container)
            {
                box = container.TransformBoundsToParent(box);
            }
        }

        return box;
    }

    /// <summary>将世界空间增量转换为本容器空间的增量。</summary>
    public Point ToLocalVector(float worldDx, float worldDy)
    {
        var vector = Parent is Container parent
            ? parent.ToLocalVector(worldDx, worldDy)
            : new Point(worldDx, worldDy);
        return LocalTransform.Invert().ApplyToVector(vector.X, vector.Y);
    }

    /// <summary>将世界空间点转换为本容器空间的点。</summary>
    public Point ToLocalPoint(Point world)
    {
        var point = Parent is Container parent ? parent.ToLocalPoint(world) : world;
        return LocalTransform.Invert().ApplyToPoint(point);
    }

    /// <inheritdoc/>
    protected override Box GetLocalBoundingBox()
    {
        // 子图元在本容器空间中；施加局部变换得到该空间中的内容范围
        return TransformBox(UnionChildrenBounds(), LocalTransform);
    }

    /// <inheritdoc/>
    protected override void TranslateLocal(float deltaX, float deltaY)
    {
        OffsetX.Value += deltaX;
        OffsetY.Value += deltaY;
    }

    /// <inheritdoc/>
    protected override Box SetBoundsLocal(Box bounds, Point anchor)
    {
        var current = GetLocalBoundingBox();
        float scaleX = ScaleFactor(current.Width, bounds.Width);
        float scaleY = ScaleFactor(current.Height, bounds.Height);

        if (!LocalTransform.IsAxisAligned)
        {
            // 旋转/翻转容器采用等比缩放，避免向矩阵注入剪切
            float uniform = MathF.Min(MathF.Abs(scaleX), MathF.Abs(scaleY));
            scaleX = MathF.Sign(scaleX) * uniform;
            scaleY = MathF.Sign(scaleY) * uniform;
        }

        // 缩放绕原点进行；平移使锚点（缩放手势的固定角）保持不动。
        // TRS 模型下：Scale(s) ∘ Translate(t) = Translate(t·s) ∘ Scale(s)
        float deltaX = anchor.X - anchor.X * scaleX;
        float deltaY = anchor.Y - anchor.Y * scaleY;
        OffsetX.Value = deltaX + OffsetX.Value * scaleX;
        OffsetY.Value = deltaY + OffsetY.Value * scaleY;
        ScaleX.Value *= scaleX;
        ScaleY.Value *= scaleY;
        return GetLocalBoundingBox();
    }

    /// <inheritdoc/>
    protected override bool ContainsLocalPoint(Point point)
    {
        foreach (var child in _children)
        {
            if (child.ContainsParentLocalPoint(point))
            {
                return true;
            }
        }

        return false;
    }

    private Box UnionChildrenBounds()
    {
        if (_children.Count == 0)
        {
            return new Box(0f, 0f, 0f, 0f);
        }

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var child in _children)
        {
            var box = child.GetParentLocalBoundingBox();
            minX = MathF.Min(minX, box.MinX);
            minY = MathF.Min(minY, box.MinY);
            maxX = MathF.Max(maxX, box.MaxX);
            maxY = MathF.Max(maxY, box.MaxY);
        }

        return new Box(minX, minY, maxX, maxY);
    }

    private static float ScaleFactor(float current, float target)
    {
        if (current < 1e-6f)
        {
            return 1f;
        }

        float scale = target / current;
        return MathF.Max(MathF.Abs(scale), 0.01f) * MathF.Sign(scale);
    }

    private static Box TransformBox(Box box, Transform transform)
    {
        var p1 = transform.ApplyToPoint(new Point(box.MinX, box.MinY));
        var p2 = transform.ApplyToPoint(new Point(box.MaxX, box.MinY));
        var p3 = transform.ApplyToPoint(new Point(box.MinX, box.MaxY));
        var p4 = transform.ApplyToPoint(new Point(box.MaxX, box.MaxY));

        float minX = MathF.Min(MathF.Min(p1.X, p2.X), MathF.Min(p3.X, p4.X));
        float maxX = MathF.Max(MathF.Max(p1.X, p2.X), MathF.Max(p3.X, p4.X));
        float minY = MathF.Min(MathF.Min(p1.Y, p2.Y), MathF.Min(p3.Y, p4.Y));
        float maxY = MathF.Max(MathF.Max(p1.Y, p2.Y), MathF.Max(p3.Y, p4.Y));
        return new Box(minX, minY, maxX, maxY);
    }
}
