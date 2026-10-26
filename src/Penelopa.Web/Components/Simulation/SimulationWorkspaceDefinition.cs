using Atlas.Core;
using Atlas.Core.Definitions;
using Atlas.Core.Items;
using Atlas.Core.Layout;
using Atlas.Core.Placement;

namespace Penelopa.Web.Components.Simulation;

/// <summary>
/// 仿真页的内容种类常量，由工作区定义与页面内容路由共享，避免任一侧重命名后
/// 静默产生 "Content is not registered" 运行时错误。
/// </summary>
internal static class SimulationContentKinds
{
    public const string Tree = "penelopa-simulation-tree";
    public const string Canvas = "penelopa-simulation-canvas";
    public const string Inspector = "penelopa-simulation-inspector";
    public const string Timeline = "penelopa-simulation-timeline";
}

/// <summary>
/// 构建仿真页的 Atlas 工作区：顶部传输条由页面固定，工作区为
/// 左树 / 中画布 / 右检视器 + 通栏底部时间轴。四个区域均不可关闭或自动隐藏，
/// 但允许移动、折叠与分割条调整；每次进入页面重建默认布局。
/// </summary>
internal static class SimulationWorkspaceDefinition
{
    internal const string WorkspaceName = "penelopa-simulation";

    internal static AtlasWorkspaceDefinition Create()
    {
        var tree = Tool("tree", "Primitives", SimulationContentKinds.Tree);
        var canvas = Document("canvas", "Canvas", SimulationContentKinds.Canvas);
        var inspector = Tool("inspector", "Inspector", SimulationContentKinds.Inspector);
        var timeline = Tool("timeline", "Timeline", SimulationContentKinds.Timeline);

        var treeGroup = ToolGroup(
            "tree-group",
            LogicalRegion.InlineStartUpper,
            tree.Id);
        var canvasGroup = DocumentGroup("canvas-group", canvas.Id);
        var inspectorGroup = ToolGroup(
            "inspector-group",
            LogicalRegion.InlineEndUpper,
            inspector.Id);
        var timelineGroup = ToolGroup(
            "timeline-group",
            LogicalRegion.BlockEndInlineStart,
            timeline.Id);

        // 画布占剩余宽度：左树固定 220px，右检视器固定 320px；时间轴通栏固定
        // 208px。Atlas 0.3.0 的约束只作用于分割条交互，初始固定轨道不会被钳位。
        var right = Split(
            "right",
            SplitAxis.InlineChildren,
            SplitBasis.FixedPixels(320d, SplitAnchor.Second),
            canvasGroup.Id,
            inspectorGroup.Id,
            new SplitConstraints(160d, null, 160d, 360d));
        var workArea = Split(
            "work-area",
            SplitAxis.InlineChildren,
            SplitBasis.FixedPixels(220d, SplitAnchor.First),
            treeGroup.Id,
            right.Id,
            new SplitConstraints(160d, 360d, 360d, null));
        var root = Split(
            "root",
            SplitAxis.BlockChildren,
            SplitBasis.FixedPixels(208d, SplitAnchor.Second),
            workArea.Id,
            timelineGroup.Id,
            new SplitConstraints(240d, null, 128d, 320d));

        LayoutNode[] nodes =
        {
            root,
            workArea,
            right,
            treeGroup,
            canvasGroup,
            inspectorGroup,
            timelineGroup,
        };
        DockItem[] items =
        {
            tree,
            canvas,
            inspector,
            timeline,
        };

        // 三个工具组必须声明唯一逻辑区域；两侧工具栏折叠，使页面只保留自定义
        // 顶栏，同时仍满足 Atlas 的组放置校验。
        var toolBars = new[]
        {
            new ToolBarState(LogicalSide.InlineStart, ToolBarVisibility.Collapsed),
            new ToolBarState(LogicalSide.InlineEnd, ToolBarVisibility.Collapsed),
        };
        var toolBarOrders = new[]
        {
            new ToolBarOrderState(LogicalRegion.InlineStartUpper, new[] { tree.Id }),
            new ToolBarOrderState(LogicalRegion.InlineEndUpper, new[] { inspector.Id }),
            new ToolBarOrderState(
                LogicalRegion.BlockEndInlineStart,
                new[] { timeline.Id }),
        };

        return new AtlasWorkspaceDefinition(
            new WorkspaceId(WorkspaceName),
            root.Id,
            nodes,
            items,
            toolBars,
            toolBarOrders);
    }

    private static GroupNode ToolGroup(
        string id,
        LogicalRegion toolbarRegion,
        DockItemId itemId)
    {
        return new GroupNode(
            new LayoutNodeId(id),
            DockItemKind.Tool,
            GroupRetentionPolicy.Persistent,
            GroupVisibility.Expanded,
            new[] { itemId },
            itemId,
            toolbarRegion: toolbarRegion);
    }

    private static GroupNode DocumentGroup(string id, DockItemId itemId)
    {
        return new GroupNode(
            new LayoutNodeId(id),
            DockItemKind.Document,
            GroupRetentionPolicy.Persistent,
            GroupVisibility.Expanded,
            new[] { itemId },
            itemId,
            new EditorGroupState(
                EditorOverflowMode.Scroll,
                EditorActivationPolicy.Adjacent));
    }

    private static SplitNode Split(
        string id,
        SplitAxis axis,
        SplitBasis basis,
        LayoutNodeId firstId,
        LayoutNodeId secondId,
        SplitConstraints constraints)
    {
        return new SplitNode(
            new LayoutNodeId(id),
            axis,
            basis,
            constraints,
            firstId,
            secondId);
    }

    private static DockItem Tool(string id, string title, string contentKind)
    {
        // 只保留移动能力：必需区域不可关闭，也不能自动隐藏到边缘。
        return new DockItem(
            new DockItemId(id),
            DockItemKind.Tool,
            new ContentReference(contentKind, id),
            DockItemCapabilities.CanMove,
            title);
    }

    private static DockItem Document(string id, string title, string contentKind)
    {
        // 画布是唯一文档：不可关闭、不可分割或固定，只允许移动。
        return new DockItem(
            new DockItemId(id),
            DockItemKind.Document,
            new ContentReference(contentKind, id),
            DockItemCapabilities.CanMove,
            title,
            documentState: new DocumentState(isPinned: false, isPreview: false));
    }
}
