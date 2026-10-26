using Atlas.Core;
using Atlas.Core.Items;
using Atlas.Core.Layout;
using Atlas.Core.Placement;
using Penelopa.Web.Components.Simulation;
using Xunit;

namespace Penelopa.Web.Tests.Simulation;

public class SimulationWorkspaceDefinitionTests
{
    [Fact]
    public void Definition_UsesDefaultSimulationLayout()
    {
        var definition = SimulationWorkspaceDefinition.Create();

        Assert.Equal(SimulationWorkspaceDefinition.WorkspaceName, definition.WorkspaceId.Value);

        var root = Assert.IsType<SplitNode>(FindNode(definition, "root"));
        Assert.Equal(SplitAxis.BlockChildren, root.Axis);
        Assert.Equal(SplitBasisKind.FixedPixels, root.Basis.Kind);
        Assert.Equal(208d, root.Basis.Value);
        Assert.Equal(SplitAnchor.Second, root.Basis.Anchor);
        Assert.Equal("work-area", root.FirstId.Value);
        Assert.Equal("timeline-group", root.SecondId.Value);

        var workArea = Assert.IsType<SplitNode>(FindNode(definition, "work-area"));
        Assert.Equal(SplitAxis.InlineChildren, workArea.Axis);
        Assert.Equal(SplitBasisKind.FixedPixels, workArea.Basis.Kind);
        Assert.Equal(220d, workArea.Basis.Value);
        Assert.Equal(SplitAnchor.First, workArea.Basis.Anchor);
        Assert.Equal("tree-group", workArea.FirstId.Value);
        Assert.Equal("right", workArea.SecondId.Value);

        var right = Assert.IsType<SplitNode>(FindNode(definition, "right"));
        Assert.Equal(SplitAxis.InlineChildren, right.Axis);
        Assert.Equal(SplitBasisKind.FixedPixels, right.Basis.Kind);
        Assert.Equal(320d, right.Basis.Value);
        Assert.Equal(SplitAnchor.Second, right.Basis.Anchor);
        Assert.Equal("canvas-group", right.FirstId.Value);
        Assert.Equal("inspector-group", right.SecondId.Value);
    }

    [Fact]
    public void RequiredRegions_CannotBeClosedOrAutoHidden()
    {
        var definition = SimulationWorkspaceDefinition.Create();

        foreach (var id in new[] { "tree", "inspector", "timeline" })
        {
            var item = FindItem(definition, id);
            Assert.Equal(DockItemKind.Tool, item.Kind);
            Assert.Equal(DockItemCapabilities.CanMove, item.Capabilities);
            Assert.False(item.Capabilities.HasFlag(DockItemCapabilities.CanClose));
            Assert.False(item.Capabilities.HasFlag(DockItemCapabilities.CanAutoHide));
        }

        var canvas = FindItem(definition, "canvas");
        Assert.Equal(DockItemKind.Document, canvas.Kind);
        Assert.Equal(DockItemCapabilities.CanMove, canvas.Capabilities);
        Assert.False(canvas.Capabilities.HasFlag(DockItemCapabilities.CanClose));
        Assert.False(canvas.Capabilities.HasFlag(DockItemCapabilities.CanSplit));
        Assert.NotNull(canvas.DocumentState);
        Assert.False(canvas.DocumentState!.Value.IsPinned);
        Assert.False(canvas.DocumentState!.Value.IsPreview);
    }

    [Fact]
    public void ToolGroups_HaveUniqueRegionsAndCollapsedToolbars()
    {
        var definition = SimulationWorkspaceDefinition.Create();

        var treeGroup = Assert.IsType<GroupNode>(FindNode(definition, "tree-group"));
        Assert.Equal(LogicalRegion.InlineStartUpper, treeGroup.ToolbarRegion);
        var inspectorGroup = Assert.IsType<GroupNode>(FindNode(definition, "inspector-group"));
        Assert.Equal(LogicalRegion.InlineEndUpper, inspectorGroup.ToolbarRegion);
        var timelineGroup = Assert.IsType<GroupNode>(FindNode(definition, "timeline-group"));
        Assert.Equal(LogicalRegion.BlockEndInlineStart, timelineGroup.ToolbarRegion);
        var canvasGroup = Assert.IsType<GroupNode>(FindNode(definition, "canvas-group"));
        Assert.Null(canvasGroup.ToolbarRegion);

        foreach (var group in new[] { treeGroup, inspectorGroup, timelineGroup, canvasGroup })
        {
            Assert.Equal(GroupVisibility.Expanded, group.Visibility);
            Assert.Equal(GroupRetentionPolicy.Persistent, group.RetentionPolicy);
        }

        Assert.Equal(2, definition.ToolBars.Count);
        Assert.All(
            definition.ToolBars,
            toolbar => Assert.Equal(ToolBarVisibility.Collapsed, toolbar.Visibility));

        Assert.Equal(3, definition.ToolBarOrders.Count);
        AssertOrder(definition, LogicalRegion.InlineStartUpper, "tree");
        AssertOrder(definition, LogicalRegion.InlineEndUpper, "inspector");
        AssertOrder(definition, LogicalRegion.BlockEndInlineStart, "timeline");
    }

    [Fact]
    public async Task Definition_ProducesValidWorkspace()
    {
        var result = new AtlasWorkspaceFactory().Create(
            SimulationWorkspaceDefinition.Create());

        Assert.True(result.Succeeded, result.Error?.DiagnosticId);
        Assert.NotNull(result.Value);
        await result.Value!.DisposeAsync();
    }

    private static LayoutNode FindNode(
        Atlas.Core.Definitions.AtlasWorkspaceDefinition definition,
        string id)
    {
        return Assert.Single(definition.Nodes, node => node.Id.Value == id);
    }

    private static DockItem FindItem(
        Atlas.Core.Definitions.AtlasWorkspaceDefinition definition,
        string id)
    {
        return Assert.Single(definition.Items, item => item.Id.Value == id);
    }

    private static void AssertOrder(
        Atlas.Core.Definitions.AtlasWorkspaceDefinition definition,
        LogicalRegion region,
        params string[] itemIds)
    {
        var order = Assert.Single(
            definition.ToolBarOrders,
            candidate => candidate.Region == region);
        Assert.Equal(itemIds, order.ItemIds.Select(itemId => itemId.Value));
    }
}
