namespace Penelopa.Core.Simulation;

/// <summary>某一帧的只读状态快照，供播放条与检视器以 20Hz 消费。</summary>
public sealed record SimulationReadout(
    SimulationState State,
    double CurrentTime,
    double Duration,
    bool Loop,
    IReadOnlyDictionary<SimulationTrackKey, float> TrackValues);
