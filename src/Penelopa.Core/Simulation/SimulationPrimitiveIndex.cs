using Penelopa.Core.Primitives;

namespace Penelopa.Core.Simulation;

/// <summary>
/// 由当前文档根列表构建的只读索引：按 Id 查图元、按属性名查 Float 属性，
/// 并提供文档顺序与所有 Float 属性的快照式列表。索引在创建后不跟随文档变化。
/// </summary>
public sealed class SimulationPrimitiveIndex
{
    private readonly Dictionary<Guid, Primitive> _byId;
    private readonly List<Primitive> _primitives;
    private readonly List<(Primitive Primitive, FloatPropValue Property)> _floatProperties;

    private SimulationPrimitiveIndex(
        Dictionary<Guid, Primitive> byId,
        List<Primitive> primitives,
        List<(Primitive Primitive, FloatPropValue Property)> floatProperties)
    {
        _byId = byId;
        _primitives = primitives;
        _floatProperties = floatProperties;
    }

    /// <summary>文档遍历顺序（根在前，容器子项随后）。</summary>
    public IReadOnlyList<Primitive> Primitives => _primitives;

    /// <summary>所有图元上的 Float 属性，按文档与属性声明顺序。</summary>
    public IReadOnlyList<(Primitive Primitive, FloatPropValue Property)> FloatProperties
        => _floatProperties;

    public static SimulationPrimitiveIndex Create(IEnumerable<Primitive> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var primitives = new List<Primitive>();
        foreach (var root in roots)
        {
            Collect(root, primitives);
        }

        var byId = new Dictionary<Guid, Primitive>(primitives.Count);
        var floatProperties = new List<(Primitive Primitive, FloatPropValue Property)>();
        foreach (var primitive in primitives)
        {
            if (!byId.TryAdd(primitive.Id, primitive))
            {
                throw new ArgumentException(
                    $"Duplicate primitive id '{primitive.Id}'.",
                    nameof(roots));
            }

            foreach (var property in primitive.Props)
            {
                if (property is FloatPropValue floatProperty)
                {
                    floatProperties.Add((primitive, floatProperty));
                }
            }
        }

        return new SimulationPrimitiveIndex(byId, primitives, floatProperties);
    }

    public bool TryGetPrimitive(Guid id, out Primitive primitive)
    {
        if (_byId.TryGetValue(id, out var found))
        {
            primitive = found;
            return true;
        }

        primitive = null!;
        return false;
    }

    public bool TryGetFloatProperty(
        Guid primitiveId,
        string propertyName,
        out FloatPropValue property)
    {
        if (_byId.TryGetValue(primitiveId, out var primitive))
        {
            foreach (var candidate in primitive.Props)
            {
                if (candidate is FloatPropValue floatProperty
                    && string.Equals(candidate.Name, propertyName, StringComparison.Ordinal))
                {
                    property = floatProperty;
                    return true;
                }
            }
        }

        property = null!;
        return false;
    }

    private static void Collect(Primitive primitive, List<Primitive> target)
    {
        target.Add(primitive);
        if (primitive is Container container)
        {
            foreach (var child in container.Children)
            {
                Collect(child, target);
            }
        }
    }
}
