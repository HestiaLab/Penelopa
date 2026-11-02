using System.Collections.Concurrent;
using Penelopa.Core.Primitives;

namespace Penelopa.Core.Services;

/// <summary>
/// 稳定 type id 与图元工厂的注册表。核心类型在静态构造中注册；
/// 扩展通过 <see cref="Register{T}(string)"/> 或显式工厂注册，序列化不再依赖 .NET 类型名。
/// </summary>
public sealed class PrimitiveTypeRegistry
{
    private readonly ConcurrentDictionary<string, Func<Primitive>> _factories =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> _exactTypeIds = new();
    private (string TypeId, Func<Primitive, bool> Matches)[] _resolvers =
        Array.Empty<(string TypeId, Func<Primitive, bool> Matches)>();

    public static PrimitiveTypeRegistry Default { get; } = CreateDefault();

    public void Register(
        string typeId,
        Func<Primitive> factory,
        Func<Primitive, bool>? matches = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(factory);

        _factories[typeId] = factory;
        if (matches is not null)
        {
            var current = Volatile.Read(ref _resolvers);
            var next = new (string TypeId, Func<Primitive, bool> Matches)[current.Length + 1];
            next[0] = (typeId, matches);
            Array.Copy(current, 0, next, 1, current.Length);
            Volatile.Write(ref _resolvers, next);
        }
    }

    /// <summary>注册无参构造的图元类型；扩展图元只需调用此方法。</summary>
    public void Register<T>(string typeId) where T : Primitive, new()
    {
        Register(typeId, () => new T(), primitive => primitive is T);
        _exactTypeIds[typeof(T)] = typeId;
    }

    public bool TryCreate(string typeId, out Primitive? primitive)
    {
        primitive = null;
        if (!_factories.TryGetValue(typeId, out var factory))
        {
            return false;
        }

        primitive = factory();
        return true;
    }

    public bool TryResolveTypeId(Primitive primitive, out string typeId)
    {
        ArgumentNullException.ThrowIfNull(primitive);

        // 叶子图元的运行时类型固定，优先走精确类型表，避免绘制热路径逐帧扫描谓词。
        if (_exactTypeIds.TryGetValue(primitive.GetType(), out typeId!))
        {
            return true;
        }

        foreach (var (candidate, matches) in Volatile.Read(ref _resolvers))
        {
            if (matches(primitive))
            {
                typeId = candidate;
                return true;
            }
        }

        typeId = string.Empty;
        return false;
    }

    public string ResolveTypeId(Primitive primitive)
        => TryResolveTypeId(primitive, out var typeId)
            ? typeId
            : throw new InvalidOperationException(
                $"Primitive type '{primitive.GetType().Name}' is not registered.");

    private static PrimitiveTypeRegistry CreateDefault()
    {
        var registry = new PrimitiveTypeRegistry();
        registry.Register<Circle>("penelopa.circle");
        registry.Register<Rectangle>("penelopa.rect");
        registry.Register<Triangle>("penelopa.triangle");
        registry.Register(
            "penelopa.container.offset",
            () => Container.CreateOffset("Offset Container", 0f, 0f),
            p => p is Container { Kind: ContainerKind.Offset });
        registry.Register(
            "penelopa.container.rotation",
            () => Container.CreateRotation("Rotate Container", 0f),
            p => p is Container { Kind: ContainerKind.Rotation });
        registry.Register(
            "penelopa.container.flip",
            () => Container.CreateFlip("Flip Container", flipX: false, flipY: false),
            p => p is Container { Kind: ContainerKind.Flip });
        return registry;
    }
}
