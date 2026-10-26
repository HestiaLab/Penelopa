using Penelopa.Core.Simulation;

namespace Penelopa.Web.Services;

/// <summary>
/// 仿真页内部的帧读数通道。页面以 20Hz 发布，时间/数值等小组件订阅，
/// 避免整个页面（含图元树与时间轴）按播放帧率重渲染。
/// </summary>
public sealed class SimulationReadoutChannel
{
    public SimulationReadout? Current { get; private set; }

    public event Action<SimulationReadout>? Updated;

    public void Publish(SimulationReadout readout)
    {
        ArgumentNullException.ThrowIfNull(readout);
        Current = readout;
        Updated?.Invoke(readout);
    }
}
