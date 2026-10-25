using Penelopa.Core.Alignment;
using Penelopa.Core.Primitives;

namespace Penelopa.Core.Interaction;

/// <summary>
/// 画布编辑手势控制器：点击选择、拖拽移动、角柄缩放。
/// 纯模型逻辑，接收世界坐标和 CSS 坐标，通过世界契约修改图元。
/// </summary>
/// <remarks>
/// 手势不变量：
/// <list type="bullet">
/// <item>按下后指针移动超过阈值才成为拖拽，避免点击时误移动。</item>
/// <item>拖拽/缩放期间不通知，<see cref="IEditorInteractionHost.NotifyPrimitivesChanged"/> 在提交时触发一次。</item>
/// <item>首次真实修改前打开宿主事务；取消时由事务快照精确恢复属性与子元素，而不是重新计算 SetBounds。</item>
/// </list>
/// </remarks>
public sealed class EditorInteractionController
{
    /// <summary>拖拽阈值（世界单位，与 CSS 像素 1:1）。</summary>
    public const float DragThresholdWorld = 3f;

    private readonly IEditorInteractionHost _host;

    private ControllerState _state = ControllerState.Idle;

    // 按下快照（点击/拖拽决策）
    private Point _pressStart;
    private Point _pressStartCss;
    private Primitive? _pressHit;
    private bool _pressDeferredCommit;

    // 拖拽快照
    private List<Primitive> _dragItems = new();
    private Point _lastWorld;
    private Point _lastCss;
    private bool _isPanGesture;

    // 缩放快照
    private Primitive? _resizeTarget;
    private Box _resizeOriginalBounds;
    private ResizeHandle _resizeHandle;

    // 钻取快照：同一位置连续点击，候选链从最外层容器向最深叶子递进
    private Point? _lastClickPosition;
    private DateTime _lastClickTime;
    private IReadOnlyList<Primitive> _drillCandidates = Array.Empty<Primitive>();
    private int _drillIndex;

    // 空白按下，可能转为平移手势
    private bool _panPending;

    // 宿主是否已被告知当前手势正在修改（首次实际修改时捕获一次）
    private bool _mutationCaptured;

    private const float DrillSlopWorld = 4f;
    private static readonly TimeSpan DrillInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>当前控制器状态（供测试和诊断）。</summary>
    public ControllerState State => _state;

    public EditorInteractionController(IEditorInteractionHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>处理指针按下，接收世界/CSS 坐标和命中结果。</summary>
    public void PointerDown(Point world, Point css, HitTestResult hit, bool ctrl)
    {
        _pressStart = world;
        _pressStartCss = css;

        if (hit.Handle is ResizeHandle handle && hit.Primitive is not null)
        {
            _resizeTarget = hit.Primitive;
            _resizeOriginalBounds = _resizeTarget.GetWorldBoundingBox();
            _resizeHandle = handle;
            _state = ControllerState.Resizing;
            return;
        }

        if (hit.Primitive is not null)
        {
            var primitive = hit.Primitive;
            if (ctrl)
            {
                // Ctrl 点击也遵循钻取方向：追加最外层候选（已选中则切换移除）
                var candidates = hit.Candidates is { Count: > 0 }
                    ? hit.Candidates
                    : new[] { primitive };
                var ctrlTarget = candidates[0];
                if (_host.IsSelected(ctrlTarget))
                {
                    _host.ToggleSelected(ctrlTarget);
                    _state = ControllerState.Idle;
                    return;
                }

                _host.AppendSelected(ctrlTarget);
                _pressHit = ctrlTarget;
                _pressDeferredCommit = false;
                _state = ControllerState.Pressed;
                return;
            }

            if (_host.IsSelected(primitive) && _host.GetSelection().Count > 1)
            {
                // 点击已选中的多选成员：延迟到松开时决策（单击折叠选择 / 拖拽移动整组）
                _pressHit = primitive;
                _pressDeferredCommit = true;
                _state = ControllerState.Pressed;
                return;
            }

            // 钻取选择：首击选最外层候选（嵌套时为根容器），同位置连续点击逐层向下
            var target = SelectDrillTarget(world, hit);
            _host.SetSelected(target);
            _pressHit = target;
            _pressDeferredCommit = false;
            _state = ControllerState.Pressed;
            return;
        }

        if (hit.InUnionBox)
        {
            // 点击在多选并集框内：整组拖拽
            _pressHit = null;
            _pressDeferredCommit = false;
            _state = ControllerState.Pressed;
            return;
        }

        // 空白按下清空选择；拖拽越过阈值则转为平移
        _host.ClearSelection();
        ClearDrillContext();
        _pressHit = null;
        _pressDeferredCommit = false;
        _panPending = true;
        _state = ControllerState.Pressed;
    }

    /// <summary>处理指针移动。</summary>
    public void PointerMove(Point world, Point css)
    {
        switch (_state)
        {
            case ControllerState.Pressed:
                if (MathF.Abs(world.X - _pressStart.X) > DragThresholdWorld
                    || MathF.Abs(world.Y - _pressStart.Y) > DragThresholdWorld)
                {
                    BeginDrag(world, css);
                }

                break;

            case ControllerState.Dragging:
                if (_isPanGesture)
                {
                    _host.PanByCss(css.X - _lastCss.X, css.Y - _lastCss.Y);
                    _lastCss = css;
                }
                else
                {
                    float dx = world.X - _lastWorld.X;
                    float dy = world.Y - _lastWorld.Y;
                    foreach (var item in _dragItems)
                    {
                        item.Translate(dx, dy);
                    }

                    _lastWorld = world;
                }

                break;

            case ControllerState.Resizing:
                CaptureMutation();
                var newBounds = ResizeMath.ComputeBounds(_resizeOriginalBounds, _resizeHandle, world);
                var anchor = ResizeMath.FixedCorner(_resizeOriginalBounds, _resizeHandle);
                _resizeTarget!.SetBounds(newBounds, anchor);
                break;
        }
    }

    /// <summary>处理指针松开，提交手势。</summary>
    public void PointerUp(Point world)
    {
        switch (_state)
        {
            case ControllerState.Pressed:
                if (_pressDeferredCommit && _pressHit is not null)
                {
                    // 多选成员的单击折叠为该成员
                    _host.SetSelected(_pressHit);
                }

                break;

            case ControllerState.Dragging:
                if (!_isPanGesture)
                {
                    if (_mutationCaptured)
                    {
                        _host.CommitMutation();
                    }

                    _host.NotifyPrimitivesChanged(_dragItems);
                }

                break;

            case ControllerState.Resizing:
                if (_resizeTarget is not null)
                {
                    if (_mutationCaptured)
                    {
                        _host.CommitMutation();
                    }

                    _host.NotifyPrimitivesChanged(new[] { _resizeTarget });
                }

                break;
        }

        ResetGesture();
    }

    /// <summary>
    /// 取消当前手势（ESC / pointercancel / 丢失捕获 / 窗口失焦）。
    /// 通过宿主事务快照精确恢复手势前的属性与子元素，再通知面板刷新。
    /// </summary>
    public void Cancel()
    {
        var affected = new List<Primitive>();
        switch (_state)
        {
            case ControllerState.Dragging when !_isPanGesture:
                affected.AddRange(_dragItems);
                break;

            case ControllerState.Resizing when _resizeTarget is not null:
                affected.Add(_resizeTarget);
                break;
        }

        if (_mutationCaptured)
        {
            _host.CancelMutation();
            if (affected.Count > 0)
            {
                _host.NotifyPrimitivesChanged(affected);
            }
        }

        ResetGesture();
    }

    /// <summary>
    /// 解析钻取目标：同一位置重复点击同一候选链时推进索引，否则重置到最外层。
    /// </summary>
    private Primitive SelectDrillTarget(Point world, HitTestResult hit)
    {
        var candidates = hit.Candidates is { Count: > 0 } ? hit.Candidates : new[] { hit.Primitive! };
        var now = DateTime.UtcNow;

        bool sameChain = _drillCandidates.Count > 0
            && ReferenceEquals(candidates[^1], _drillCandidates[^1]);
        bool sameSpot = _lastClickPosition is not null
            && MathF.Abs(world.X - _lastClickPosition.Value.X) <= DrillSlopWorld
            && MathF.Abs(world.Y - _lastClickPosition.Value.Y) <= DrillSlopWorld;
        bool withinInterval = now - _lastClickTime <= DrillInterval;

        if (sameChain && sameSpot && withinInterval && _drillIndex < candidates.Count - 1)
        {
            _drillIndex++;
        }
        else if (!(sameChain && sameSpot && withinInterval))
        {
            _drillIndex = 0;
        }

        _lastClickPosition = world;
        _lastClickTime = now;
        _drillCandidates = candidates;
        return candidates[_drillIndex];
    }

    private void ClearDrillContext()
    {
        _lastClickPosition = null;
        _drillCandidates = Array.Empty<Primitive>();
        _drillIndex = 0;
    }

    private void CaptureMutation()
    {
        if (_mutationCaptured)
        {
            return;
        }

        _mutationCaptured = true;
        _host.BeginMutation();
    }

    private void BeginDrag(Point world, Point css)
    {
        if (_panPending)
        {
            // 空白拖拽在 CSS 空间平移视口，从按下点计算全量位移使内容跟随指针
            _isPanGesture = true;
            _host.PanByCss(css.X - _pressStartCss.X, css.Y - _pressStartCss.Y);
            _lastCss = css;
            _state = ControllerState.Dragging;
            return;
        }

        _dragItems = _host.GetSelection().ToList();
        CaptureMutation();

        // 从按下点计算全量位移，使选区从按下位置开始跟随指针（而非从越过阈值处）
        float dx = world.X - _pressStart.X;
        float dy = world.Y - _pressStart.Y;
        foreach (var item in _dragItems)
        {
            item.Translate(dx, dy);
        }

        _lastWorld = world;
        _state = ControllerState.Dragging;
    }

    private void ResetGesture()
    {
        _pressHit = null;
        _pressDeferredCommit = false;
        _dragItems = new List<Primitive>();
        _resizeTarget = null;
        _panPending = false;
        _isPanGesture = false;
        _mutationCaptured = false;
        _state = ControllerState.Idle;
    }
}

/// <summary>交互控制器的手势状态。</summary>
public enum ControllerState
{
    Idle,
    Pressed,
    Dragging,
    Resizing,
}
