namespace Penelopa.Core.Simulation;

/// <summary>仿真页的播放状态。</summary>
public enum SimulationState
{
    /// <summary>显示基础布局，尚未建立检查会话。</summary>
    Ready,

    /// <summary>由定位/步进建立、时钟未运行的检查会话。</summary>
    Preview,

    /// <summary>时钟正在推进。</summary>
    Playing,

    /// <summary>播放中暂停，保留当前时刻。</summary>
    Paused,

    /// <summary>非循环播放已到达时间轴终点并保持末帧。</summary>
    Finished,
}
