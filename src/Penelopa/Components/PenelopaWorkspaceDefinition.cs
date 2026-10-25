using Atlas.Core;
using Atlas.Core.Definitions;
using Atlas.Core.Items;
using Atlas.Core.Layout;
using Atlas.Core.Placement;

namespace Penelopa.Components;

/// <summary>
/// Penelopa 工作区的内容种类常量，由工作区定义和 <see cref="PenelopaWorkspace"/> 中的
/// <see cref="AtlasContentRoute"/> 声明共享。集中管理这些字符串可防止任一侧重命名时
/// 静默产生 "Content is not registered" 运行时错误。
/// </summary>
public static class PenelopaContentKinds
{
    public const string Tools = "penelopa-tools";
    public const string Tree = "penelopa-tree";
    public const string Canvas = "penelopa-canvas";
    public const string Props = "penelopa-props";
}

/// <summary>
/// 构建 Penelopa 工作区：Atlas 六区域可停靠布局，两侧对称 300px 侧栏围绕全高文档画布，
/// 外加四个空折叠组保持工具栏槽位可用。布局复刻原声明式 razor 树；两种形式产出相同的
/// <see cref="AtlasWorkspaceDefinition"/>。
/// </summary>
internal static class PenelopaWorkspaceDefinition
{
    internal static AtlasWorkspaceDefinition Create(string workspaceName)
    {
        if (string.IsNullOrWhiteSpace(workspaceName))
        {
            throw new ArgumentException("A workspace name is required.", nameof(workspaceName));
        }

        var tools = Tool("tools", "Tools", PenelopaContentKinds.Tools);
        var tree = Tool("tree", "Primitives", PenelopaContentKinds.Tree);
        var canvas = Document("canvas", "Diagram", PenelopaContentKinds.Canvas);
        var props = Tool("props", "Properties", PenelopaContentKinds.Props);

        var toolsGroup = ToolGroup(
            "tools-group",
            LogicalRegion.InlineStartUpper,
            GroupVisibility.Expanded,
            tools.Id,
            tree.Id);
        var leftLower = ToolGroup(
            "left-lower",
            LogicalRegion.InlineStartLower,
            GroupVisibility.Collapsed);
        var docsGroup = DocumentGroup("docs-group", canvas.Id);
        var propsGroup = ToolGroup(
            "props-group",
            LogicalRegion.InlineEndUpper,
            GroupVisibility.Expanded,
            props.Id);
        var rightLower = ToolGroup(
            "right-lower",
            LogicalRegion.InlineEndLower,
            GroupVisibility.Collapsed);
        var bottomLeft = ToolGroup(
            "bottom-left",
            LogicalRegion.BlockEndInlineStart,
            GroupVisibility.Collapsed);
        var bottomRight = ToolGroup(
            "bottom-right",
            LogicalRegion.BlockEndInlineEnd,
            GroupVisibility.Collapsed);

        // 左/右停靠栏对称 300px；每个停靠栏的内部分割和底部停靠栏默认为 0.5 比例基数，
        // 与声明式形式未指定 Basis 时的行为一致
        var leftDock = Split(
            "left-dock",
            SplitAxis.BlockChildren,
            SplitBasis.Proportional(0.5d),
            toolsGroup.Id,
            leftLower.Id);
        var rightDock = Split(
            "right-dock",
            SplitAxis.BlockChildren,
            SplitBasis.Proportional(0.5d),
            propsGroup.Id,
            rightLower.Id);
        var editorRight = Split(
            "editor-right",
            SplitAxis.InlineChildren,
            SplitBasis.FixedPixels(300d, SplitAnchor.Second),
            docsGroup.Id,
            rightDock.Id);
        var mainArea = Split(
            "main-area",
            SplitAxis.InlineChildren,
            SplitBasis.FixedPixels(300d, SplitAnchor.First),
            leftDock.Id,
            editorRight.Id);
        var bottomDock = Split(
            "bottom-dock",
            SplitAxis.InlineChildren,
            SplitBasis.Proportional(0.5d),
            bottomLeft.Id,
            bottomRight.Id);
        var root = Split(
            "root",
            SplitAxis.BlockChildren,
            SplitBasis.Proportional(0.78d),
            mainArea.Id,
            bottomDock.Id);

        LayoutNode[] nodes =
        {
            root,
            mainArea,
            leftDock,
            editorRight,
            rightDock,
            bottomDock,
            toolsGroup,
            leftLower,
            docsGroup,
            propsGroup,
            rightLower,
            bottomLeft,
            bottomRight,
        };
        DockItem[] items =
        {
            tools,
            tree,
            canvas,
            props,
        };

        // 声明式收集器从非空工具组推导这些；程序式定义显式提供
        var toolBars = new[]
        {
            new ToolBarState(LogicalSide.InlineStart, ToolBarVisibility.Expanded),
            new ToolBarState(LogicalSide.InlineEnd, ToolBarVisibility.Expanded),
        };
        var toolBarOrders = new[]
        {
            new ToolBarOrderState(LogicalRegion.InlineStartUpper, new[] { tools.Id, tree.Id }),
            new ToolBarOrderState(LogicalRegion.InlineStartLower, Array.Empty<DockItemId>()),
            new ToolBarOrderState(LogicalRegion.InlineEndUpper, new[] { props.Id }),
            new ToolBarOrderState(LogicalRegion.InlineEndLower, Array.Empty<DockItemId>()),
            new ToolBarOrderState(LogicalRegion.BlockEndInlineStart, Array.Empty<DockItemId>()),
            new ToolBarOrderState(LogicalRegion.BlockEndInlineEnd, Array.Empty<DockItemId>()),
        };

        return new AtlasWorkspaceDefinition(
            new WorkspaceId(workspaceName),
            root.Id,
            nodes,
            items,
            toolBars,
            toolBarOrders);
    }

    private static GroupNode ToolGroup(
        string id,
        LogicalRegion toolbarRegion,
        GroupVisibility visibility,
        params DockItemId[] itemIds)
    {
        // 空组仅在持久保留策略且无选中项时有效（由 GroupNode 构造函数强制）
        return new GroupNode(
            new LayoutNodeId(id),
            DockItemKind.Tool,
            GroupRetentionPolicy.Persistent,
            visibility,
            itemIds,
            itemIds.Length == 0 ? null : itemIds[0],
            toolbarRegion: toolbarRegion);
    }

    private static GroupNode DocumentGroup(string id, params DockItemId[] itemIds)
    {
        // 声明式形式默认为 Scroll 溢出和 Adjacent 激活的文档组编辑器状态
        return new GroupNode(
            new LayoutNodeId(id),
            DockItemKind.Document,
            GroupRetentionPolicy.Persistent,
            GroupVisibility.Expanded,
            itemIds,
            itemIds[0],
            new EditorGroupState(
                EditorOverflowMode.Scroll,
                EditorActivationPolicy.Adjacent));
    }

    private static SplitNode Split(
        string id,
        SplitAxis axis,
        SplitBasis basis,
        LayoutNodeId firstId,
        LayoutNodeId secondId)
    {
        return new SplitNode(
            new LayoutNodeId(id),
            axis,
            basis,
            default,
            firstId,
            secondId);
    }

    private static DockItem Tool(string id, string title, string contentKind)
    {
        return new DockItem(
            new DockItemId(id),
            DockItemKind.Tool,
            new ContentReference(contentKind, id),
            DockItemCapabilityPresets.Tool,
            title,
            iconKey: null);
    }

    private static DockItem Document(string id, string title, string contentKind)
    {
        return new DockItem(
            new DockItemId(id),
            DockItemKind.Document,
            new ContentReference(contentKind, id),
            DockItemCapabilityPresets.Document,
            title,
            iconKey: null,
            new DocumentState(isPinned: false, isPreview: false));
    }
}
