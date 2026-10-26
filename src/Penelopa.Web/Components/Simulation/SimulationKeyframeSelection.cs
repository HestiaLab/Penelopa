namespace Penelopa.Web.Components.Simulation;

/// <summary>时间轴/检视器共享的关键帧选择：属性名 + 轨道内下标。</summary>
public readonly record struct SimulationKeyframeSelection(string PropertyName, int Index);
