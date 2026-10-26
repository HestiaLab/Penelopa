using Penelopa.Core.Primitives;

namespace Penelopa.Core.Simulation;

/// <summary>
/// 一次仿真页会话的运行时。持有会话基础值快照，负责 Active/Dormant 解析、
/// 状态机推进与临时属性写入；不依赖 Blazor/渲染，也不产生文档历史。
/// 所有方法都应在 UI/会话线程调用；渲染线程只读取属性。
/// </summary>
public sealed class SimulationRuntime
{
    private readonly SimulationPrimitiveIndex _index;
    private readonly List<(FloatPropValue Property, float Value)> _baseValues = new();

    private SimulationConfig _config;
    private SimulationResolution _resolution;
    private IReadOnlyList<SimulationValidationIssue> _issues;
    private double _duration;
    private double _currentTime;
    private double _playStartedAt;
    private double _playStartTime;
    private bool _loop;
    private bool _hasSession;

    public SimulationRuntime(IReadOnlyList<Primitive> roots, SimulationConfig config)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(config);

        _index = SimulationPrimitiveIndex.Create(roots);
        _config = config;
        _loop = config.Loop;
        _resolution = SimulationResolver.Resolve(config, _index);
        _issues = SimulationValidator.Validate(config, _index);
        _duration = SimulationTimeline.GetDuration(
            _resolution.Active.Select(track => track.Track));
        State = SimulationState.Ready;
    }

    public SimulationState State { get; private set; }

    public double CurrentTime => _currentTime;

    public double Duration => _duration;

    public bool Loop => _loop;

    public SimulationConfig Config => _config;

    public IReadOnlyList<ResolvedSimulationTrack> ActiveTrackBindings => _resolution.Active;

    public IReadOnlyList<SimulationTrack> DormantTracks => _resolution.Dormant;

    public IReadOnlyList<SimulationValidationIssue> ValidationIssues => _issues;

    public bool CanPlay => _issues.Count == 0
        && _resolution.Active.Count > 0
        && _duration > 0d;

    /// <summary>从 Ready 进入 Preview，并在给定时刻求值。</summary>
    public bool TryEnterPreview(
        double time,
        out IReadOnlyList<SimulationValidationIssue> issues)
    {
        if (!TryGetPlaybackBlockers(out issues))
        {
            return false;
        }

        EnsureSession();
        State = SimulationState.Preview;
        _currentTime = SimulationTimeline.Clamp(time, _duration);
        Apply(_currentTime);
        return true;
    }

    /// <summary>开始/恢复播放；Finished 或位于终点的暂停状态从 0 重新开始。</summary>
    public bool TryPlay(
        double nowSeconds,
        out IReadOnlyList<SimulationValidationIssue> issues)
    {
        if (!double.IsFinite(nowSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }

        if (State == SimulationState.Playing)
        {
            issues = Array.Empty<SimulationValidationIssue>();
            return true;
        }

        if (!TryGetPlaybackBlockers(out issues))
        {
            return false;
        }

        EnsureSession();
        if (State is SimulationState.Ready or SimulationState.Finished
            || _currentTime >= _duration)
        {
            _currentTime = 0d;
        }

        State = SimulationState.Playing;
        _playStartedAt = nowSeconds;
        _playStartTime = _currentTime;
        Apply(_currentTime);
        return true;
    }

    /// <summary>推进播放；非循环到达 T 时进入 Finished，循环时按 T 取模。</summary>
    public void Advance(double nowSeconds)
    {
        if (State != SimulationState.Playing)
        {
            return;
        }

        if (!double.IsFinite(nowSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }

        var elapsed = Math.Max(0d, nowSeconds - _playStartedAt);
        var time = _playStartTime + elapsed;
        if (_loop)
        {
            _currentTime = SimulationTimeline.Wrap(time, _duration);
        }
        else if (time >= _duration)
        {
            _currentTime = _duration;
            State = SimulationState.Finished;
        }
        else
        {
            _currentTime = time;
        }

        Apply(_currentTime);
    }

    public void Pause(double nowSeconds)
    {
        if (State != SimulationState.Playing)
        {
            return;
        }

        Advance(nowSeconds);
        if (State == SimulationState.Playing)
        {
            State = SimulationState.Paused;
        }
    }

    /// <summary>定位到指定时刻；Playing 中必须先暂停。</summary>
    public bool TrySeek(
        double time,
        out IReadOnlyList<SimulationValidationIssue> issues)
    {
        if (State == SimulationState.Playing)
        {
            issues = new[] { CreateIssue("Pause playback before seeking.") };
            return false;
        }

        if (State == SimulationState.Ready)
        {
            if (!TryEnterPreview(time, out issues))
            {
                return false;
            }

            if (!_loop && _currentTime >= _duration)
            {
                State = SimulationState.Finished;
            }

            return true;
        }

        if (!TryGetPlaybackBlockers(out issues))
        {
            return false;
        }

        _currentTime = SimulationTimeline.Clamp(time, _duration);
        if (!_loop && _currentTime >= _duration)
        {
            State = SimulationState.Finished;
        }
        else if (State == SimulationState.Finished)
        {
            State = SimulationState.Paused;
        }

        Apply(_currentTime);
        return true;
    }

    /// <summary>以 1/60s 等帧步进；从 Ready 步进会进入 Preview。</summary>
    public bool TryStep(
        double delta,
        out IReadOnlyList<SimulationValidationIssue> issues)
    {
        if (!double.IsFinite(delta))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        if (State == SimulationState.Playing)
        {
            issues = new[] { CreateIssue("Pause playback before stepping.") };
            return false;
        }

        return TrySeek(_currentTime + delta, out issues);
    }

    /// <summary>结束会话、恢复全部基础值并回到 Ready；配置不变。</summary>
    public void Stop()
    {
        RestoreBaseValues();
        _hasSession = false;
        State = SimulationState.Ready;
        _currentTime = 0d;
        _playStartedAt = 0d;
        _playStartTime = 0d;
    }

    /// <summary>
    /// 在非播放状态更新配置。暂停/定位会话会立即按当前 t 重算；
    /// 配置导致无活动轨道或 T=0 时自动 Stop 并恢复基础值。
    /// </summary>
    public bool TryUpdateConfig(
        SimulationConfig config,
        out IReadOnlyList<SimulationValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (State == SimulationState.Playing)
        {
            issues = new[] { CreateIssue("Pause or stop playback before editing the simulation.") };
            return false;
        }

        var resolution = SimulationResolver.Resolve(config, _index);
        var validation = SimulationValidator.Validate(config, _index);
        if (validation.Count > 0)
        {
            issues = validation;
            return false;
        }

        var duration = SimulationTimeline.GetDuration(
            resolution.Active.Select(track => track.Track));

        _config = config;
        _loop = config.Loop;
        _resolution = resolution;
        _issues = validation;
        _duration = duration;
        issues = Array.Empty<SimulationValidationIssue>();

        if (!_hasSession)
        {
            State = SimulationState.Ready;
            _currentTime = 0d;
            return true;
        }

        if (_resolution.Active.Count == 0 || _duration <= 0d)
        {
            Stop();
            return true;
        }

        _currentTime = SimulationTimeline.Clamp(_currentTime, _duration);
        if (State == SimulationState.Finished && _currentTime < _duration)
        {
            State = SimulationState.Paused;
        }

        Apply(_currentTime);
        return true;
    }

    public void SetLoop(bool loop)
    {
        if (_loop == loop)
        {
            return;
        }

        _loop = loop;
        _config = new SimulationConfig(loop, _config.Tracks);
    }

    /// <summary>重新应用当前帧；用于动画范围 Fit 临时改写属性之后。</summary>
    public void ReapplyCurrentFrame()
    {
        if (_hasSession)
        {
            Apply(_currentTime);
        }
    }

    public SimulationReadout CreateReadout()
    {
        var values = new Dictionary<SimulationTrackKey, float>(_resolution.Active.Count);
        foreach (var resolved in _resolution.Active)
        {
            values[resolved.Key] = resolved.Property.Value;
        }

        return new SimulationReadout(State, _currentTime, _duration, _loop, values);
    }

    private bool TryGetPlaybackBlockers(
        out IReadOnlyList<SimulationValidationIssue> issues)
    {
        if (_issues.Count > 0)
        {
            issues = _issues;
            return false;
        }

        if (_resolution.Active.Count == 0)
        {
            issues = new[] { CreateIssue("No active animation tracks.") };
            return false;
        }

        if (_duration <= 0d)
        {
            issues = new[] { CreateIssue("The animation has no duration; add a keyframe after the first one.") };
            return false;
        }

        issues = Array.Empty<SimulationValidationIssue>();
        return true;
    }

    private void EnsureSession()
    {
        if (_hasSession)
        {
            return;
        }

        _baseValues.Clear();
        foreach (var entry in _index.FloatProperties)
        {
            _baseValues.Add((entry.Property, entry.Property.Value));
        }

        _hasSession = true;
    }

    private void Apply(double time)
    {
        if (!_hasSession)
        {
            return;
        }

        foreach (var (property, baseValue) in _baseValues)
        {
            property.Value = baseValue;
        }

        foreach (var resolved in _resolution.Active)
        {
            resolved.Property.Value = SimulationEvaluator.Evaluate(resolved.Track, time);
        }
    }

    private void RestoreBaseValues()
    {
        if (!_hasSession)
        {
            return;
        }

        foreach (var (property, baseValue) in _baseValues)
        {
            property.Value = baseValue;
        }

        _baseValues.Clear();
    }

    private static SimulationValidationIssue CreateIssue(string message)
        => new(SimulationIssueKind.Structure, null, null, null, message);
}
