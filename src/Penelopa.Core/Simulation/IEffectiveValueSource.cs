using Penelopa.Core.Primitives;

namespace Penelopa.Core.Simulation;

/// <summary>
/// 读取图元属性的“生效值”。基础实现直接返回模型值；仿真 overlay 在
/// 播放/预览期间覆盖被轨道驱动的属性，但不写入文档模型。
/// </summary>
public interface IEffectiveValueSource
{
    float GetFloat(FloatPropValue property, float baseValue);
}

/// <summary>
/// 仿真播放的临时值层。以属性实例为键，仅保存求值结果；
/// 停止、离开页面或配置失效时清空，不影响模型、历史或保存负载。
/// </summary>
public sealed class SimulationOverlay : IEffectiveValueSource
{
    private readonly Dictionary<FloatPropValue, float> _values =
        new(ReferenceEqualityComparer.Instance);

    public int Count => _values.Count;

    public float GetFloat(FloatPropValue property, float baseValue)
        => _values.TryGetValue(property, out var value) ? value : baseValue;

    public bool TryGet(FloatPropValue property, out float value)
        => _values.TryGetValue(property, out value);

    public void Set(FloatPropValue property, float value)
        => _values[property] = value;

    public void Clear() => _values.Clear();
}
