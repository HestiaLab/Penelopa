using Penelopa.Core.Alignment;
using Xunit;

namespace Penelopa.Core.Tests;

public class TransformTests
{
    [Fact]
    public void ApplyToPoint_Translation()
    {
        var t = Transform.Translate(3f, 4f);
        Assert.Equal(new Point(5f, 6f), t.ApplyToPoint(new Point(2f, 2f)));
    }

    [Fact]
    public void ApplyToPoint_Scale()
    {
        var t = Transform.Scale(2f, 3f);
        Assert.Equal(new Point(2f, 6f), t.ApplyToPoint(new Point(1f, 2f)));
    }

    [Fact]
    public void ApplyToPoint_Rotation90()
    {
        var t = Transform.Rotate(90f);
        var p = t.ApplyToPoint(new Point(1f, 0f));
        Assert.Equal(0f, p.X, 3);
        Assert.Equal(1f, p.Y, 3); // counter-clockwise in world (Y up)
    }

    [Fact]
    public void Multiply_AppliesOtherFirst()
    {
        // Scale then translate: point (1,0) → scale (2,0) → translate (7,0).
        var t = Transform.Translate(5f, 0f).Multiply(Transform.Scale(2f, 2f));
        Assert.Equal(new Point(7f, 0f), t.ApplyToPoint(new Point(1f, 0f)));
    }

    [Fact]
    public void Invert_Translation()
    {
        var t = Transform.Translate(3f, -4f);
        var inv = t.Invert();
        var result = inv.ApplyToPoint(t.ApplyToPoint(new Point(5f, 5f)));
        Assert.Equal(5f, result.X, 3);
        Assert.Equal(5f, result.Y, 3);
    }

    [Fact]
    public void Invert_Rotation_ComposesToIdentity()
    {
        var t = Transform.Rotate(30f);
        var composed = t.Invert().Multiply(t);
        var p = composed.ApplyToPoint(new Point(10f, -7f));
        Assert.Equal(10f, p.X, 3);
        Assert.Equal(-7f, p.Y, 3);
    }

    [Fact]
    public void Invert_Singular_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Transform.Scale(0f, 1f).Invert());
    }

    [Fact]
    public void ApplyToVector_IgnoresTranslation()
    {
        var t = Transform.Translate(100f, 100f);
        Assert.Equal(new Point(1f, 0f), t.ApplyToVector(1f, 0f));
    }

    [Fact]
    public void IsAxisAligned_DetectsRotation()
    {
        Assert.True(Transform.Translate(1f, 2f).IsAxisAligned);
        Assert.True(Transform.Scale(2f, 3f).IsAxisAligned);
        Assert.False(Transform.Rotate(45f).IsAxisAligned);
    }

    [Fact]
    public void IsIdentity_DetectsIdentityWithinTolerance()
    {
        Assert.True(Transform.Identity.IsIdentity);
        Assert.True(Transform.Translate(0f, 0f).IsIdentity);
        Assert.True(Transform.Rotate(360f).IsIdentity);
        Assert.False(Transform.Translate(1f, 0f).IsIdentity);
        Assert.False(Transform.Rotate(45f).IsIdentity);
        Assert.False(Transform.Scale(2f, 1f).IsIdentity);
        Assert.False(Transform.Scale(-1f, 1f).IsIdentity);
    }
}
