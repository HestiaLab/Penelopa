namespace Penelopa.Core.Alignment;

/// <summary>
/// 二维仿射变换矩阵（3×3，列向量约定）。
/// </summary>
/// <remarks>
/// <code>
/// | a  c  tx |     x' = a*x + c*y + tx
/// | b  d  ty |     y' = b*x + d*y + ty
/// | 0  0  1  |
/// </code>
/// </remarks>
public struct Transform
{
    /// <summary>X 缩放分量。</summary>
    public float A { get; set; }

    /// <summary>影响 Y 的剪切分量。</summary>
    public float B { get; set; }

    /// <summary>影响 X 的剪切分量。</summary>
    public float C { get; set; }

    /// <summary>Y 缩放分量。</summary>
    public float D { get; set; }

    /// <summary>X 平移分量。</summary>
    public float Tx { get; set; }

    /// <summary>Y 平移分量。</summary>
    public float Ty { get; set; }

    public Transform(float a, float b, float c, float d, float tx, float ty)
        => (A, B, C, D, Tx, Ty) = (a, b, c, d, tx, ty);

    public static Transform Translate(float tx, float ty) => new(1, 0, 0, 1, tx, ty);

    /// <summary>创建旋转变换（角度制，世界空间逆时针）。</summary>
    public static Transform Rotate(float degrees)
    {
        float radians = degrees * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Transform(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>创建绕原点的缩放变换。</summary>
    public static Transform Scale(float scaleX, float scaleY) => new(scaleX, 0, 0, scaleY, 0, 0);

    /// <summary>单位变换。</summary>
    public static Transform Identity => new(1, 0, 0, 1, 0, 0);

    /// <summary>矩阵复合：<c>this ∘ other</c>，先应用 <paramref name="other"/>。</summary>
    public Transform Multiply(Transform other)
    {
        return new Transform(
            A * other.A + C * other.B,
            B * other.A + D * other.B,
            A * other.C + C * other.D,
            B * other.C + D * other.D,
            A * other.Tx + C * other.Ty + Tx,
            B * other.Tx + D * other.Ty + Ty);
    }

    /// <summary>求逆矩阵。行列式为零时抛出异常。</summary>
    public Transform Invert()
    {
        float det = A * D - B * C;
        if (MathF.Abs(det) < 1e-6f)
        {
            throw new InvalidOperationException("Cannot invert a singular transform.");
        }

        float invDet = 1f / det;
        return new Transform(
            D * invDet,
            -B * invDet,
            -C * invDet,
            A * invDet,
            (C * Ty - D * Tx) * invDet,
            (B * Tx - A * Ty) * invDet);
    }

    /// <summary>对点应用完整变换（含平移）。</summary>
    public Point ApplyToPoint(Point point)
        => new(A * point.X + C * point.Y + Tx, B * point.X + D * point.Y + Ty);

    /// <summary>对向量仅应用线性部分（不含平移）。</summary>
    public Point ApplyToVector(float dx, float dy)
        => new(A * dx + C * dy, B * dx + D * dy);

    /// <summary>变换是否轴对齐（无旋转/剪切），世界轴缩放不会引入剪切。</summary>
    public bool IsAxisAligned => MathF.Abs(B) < 1e-6f && MathF.Abs(C) < 1e-6f;

    /// <summary>是否为单位变换（允许旋转 360° 等浮点误差）。</summary>
    public bool IsIdentity
        => MathF.Abs(A - 1f) < IdentityEpsilon
            && MathF.Abs(B) < IdentityEpsilon
            && MathF.Abs(C) < IdentityEpsilon
            && MathF.Abs(D - 1f) < IdentityEpsilon
            && MathF.Abs(Tx) < IdentityEpsilon
            && MathF.Abs(Ty) < IdentityEpsilon;

    private const float IdentityEpsilon = 1e-4f;
}
