using System.Collections.ObjectModel;

namespace Penelopa.Core.Simulation;

/// <summary>轨道的稳定标识：图元 Id + Float 属性名。</summary>
public readonly record struct SimulationTrackKey(Guid PrimitiveId, string PropertyName);

/// <summary>
/// 一个 (图元, Float 属性) 的动画轨道。构造时复制关键帧列表，
/// 保证配置对象不可变；排序与领域约束由校验器保证。
/// </summary>
public sealed record SimulationTrack
{
    private readonly ReadOnlyCollection<SimulationKeyframe> _keyframes;

    public SimulationTrack(
        Guid primitiveId,
        string propertyName,
        IEnumerable<SimulationKeyframe> keyframes)
    {
        ArgumentNullException.ThrowIfNull(keyframes);
        if (propertyName is null)
        {
            throw new ArgumentNullException(nameof(propertyName));
        }

        var copy = keyframes.ToArray();
        if (copy.Length == 0)
        {
            throw new ArgumentException(
                "A track must contain at least one keyframe.",
                nameof(keyframes));
        }

        PrimitiveId = primitiveId;
        PropertyName = propertyName;
        _keyframes = Array.AsReadOnly(copy);
    }

    public Guid PrimitiveId { get; }

    public string PropertyName { get; }

    public IReadOnlyList<SimulationKeyframe> Keyframes => _keyframes;

    public SimulationTrackKey Key => new(PrimitiveId, PropertyName);
}
