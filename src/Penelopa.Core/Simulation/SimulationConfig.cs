using System.Collections.ObjectModel;

namespace Penelopa.Core.Simulation;

/// <summary>一份完整的仿真配置。构造时复制轨道列表，实例不可变。</summary>
public sealed record SimulationConfig
{
    private readonly ReadOnlyCollection<SimulationTrack> _tracks;

    public SimulationConfig(bool loop, IEnumerable<SimulationTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        Loop = loop;
        _tracks = Array.AsReadOnly(tracks.ToArray());
    }

    public static SimulationConfig Empty { get; } = new(false, Array.Empty<SimulationTrack>());

    /// <summary>全局循环开关。</summary>
    public bool Loop { get; }

    public IReadOnlyList<SimulationTrack> Tracks => _tracks;
}
