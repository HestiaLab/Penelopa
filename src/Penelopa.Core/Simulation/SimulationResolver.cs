using Penelopa.Core.Primitives;

namespace Penelopa.Core.Simulation;

/// <summary>已解析到具体图元与 Float 属性的轨道。</summary>
public sealed record ResolvedSimulationTrack(
    SimulationTrack Track,
    Primitive Primitive,
    FloatPropValue Property)
{
    public SimulationTrackKey Key => Track.Key;
}

/// <summary>轨道解析结果：可求值的 Active 轨道与目标已失效的 Dormant 轨道。</summary>
public sealed class SimulationResolution
{
    public SimulationResolution(
        IReadOnlyList<ResolvedSimulationTrack> active,
        IReadOnlyList<SimulationTrack> dormant)
    {
        Active = active;
        Dormant = dormant;
    }

    public IReadOnlyList<ResolvedSimulationTrack> Active { get; }

    public IReadOnlyList<SimulationTrack> Dormant { get; }
}

/// <summary>把配置轨道解析到当前文档；只按 Id/属性名匹配，不修改任何状态。</summary>
public static class SimulationResolver
{
    public static SimulationResolution Resolve(
        SimulationConfig config,
        SimulationPrimitiveIndex index)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(index);

        var active = new List<ResolvedSimulationTrack>();
        var dormant = new List<SimulationTrack>();
        foreach (var track in config.Tracks)
        {
            if (index.TryGetPrimitive(track.PrimitiveId, out var primitive)
                && index.TryGetFloatProperty(track.PrimitiveId, track.PropertyName, out var property))
            {
                active.Add(new ResolvedSimulationTrack(track, primitive, property));
            }
            else
            {
                dormant.Add(track);
            }
        }

        return new SimulationResolution(active, dormant);
    }
}
