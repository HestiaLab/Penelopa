using Astrea.Components.Tree;
using Penelopa.Core.Primitives;

namespace Penelopa.Web.Components;

/// <summary>
/// Maps the primitive tree to Astrea nodes once per structural change. Node
/// text is resolved by the consuming NodeTemplate so renames update without a
/// rebuild.
/// </summary>
internal sealed class PrimitiveTreeSnapshot
{
    internal const string ItemCssClass = "penelopa-tree-item";
    internal const string ExpandedIcon = "#penelopa-tree-chevron-down";
    internal const string CollapsedIcon = "#penelopa-tree-chevron-right";

    private PrimitiveTreeSnapshot(
        IReadOnlyList<TreeNode> nodes,
        IReadOnlyDictionary<string, Primitive> byId)
    {
        Nodes = nodes;
        ById = byId;
    }

    public static PrimitiveTreeSnapshot Empty { get; } = new(
        Array.Empty<TreeNode>(),
        new Dictionary<string, Primitive>(StringComparer.Ordinal));

    public IReadOnlyList<TreeNode> Nodes { get; }

    public IReadOnlyDictionary<string, Primitive> ById { get; }

    public static PrimitiveTreeSnapshot Create(
        IReadOnlyList<Primitive> roots,
        IReadOnlySet<Guid>? animatedIds = null)
    {
        var byId = new Dictionary<string, Primitive>(StringComparer.Ordinal);
        var nodes = new List<TreeNode>(roots.Count);
        foreach (var root in roots)
        {
            nodes.Add(BuildNode(root, animatedIds, byId));
        }

        return new PrimitiveTreeSnapshot(nodes, byId);
    }

    public static string IconFor(TreeNode node)
        => node.BindData is Primitive primitive ? IconFor(primitive) : "square";

    public static string IconFor(Primitive primitive) => primitive switch
    {
        Circle => "circle",
        Rectangle => "square",
        Triangle => "triangle",
        Container => "container",
        _ => "square",
    };

    private static TreeNode BuildNode(
        Primitive primitive,
        IReadOnlySet<Guid>? animatedIds,
        Dictionary<string, Primitive> byId)
    {
        var id = primitive.Id.ToString("N");
        byId[id] = primitive;

        var node = new TreeNode(
            primitive.Name.Value,
            bindData: primitive,
            id: id,
            isExpanded: true)
        {
            ExpandedIcon = ExpandedIcon,
            CollapsedIcon = CollapsedIcon,
        };

        var classes = ItemCssClass;
        if (primitive is Container)
        {
            classes += " is-container";
        }

        if (animatedIds?.Contains(primitive.Id) == true)
        {
            classes += " has-animation";
        }

        node.SetCssClass(classes);

        if (primitive is Container container)
        {
            foreach (var child in container.Children)
            {
                node.Add(BuildNode(child, animatedIds, byId));
            }
        }

        return node;
    }
}
