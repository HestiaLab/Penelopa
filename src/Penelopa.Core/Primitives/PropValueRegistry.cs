using System.Collections.Concurrent;

namespace Penelopa.Core.Primitives;

/// <summary>
/// 属性值装箱/回读的注册表。核心类型在静态构造中注册；扩展属性值类型
/// 只需注册一对转换委托，即可进入序列化与历史快照。
/// </summary>
public sealed class PropValueRegistry
{
    internal readonly record struct Handler(
        Func<PropValue, object?> Get,
        Action<PropValue, object> Set);

    private readonly ConcurrentDictionary<Type, Handler> _handlers = new();

    public static PropValueRegistry Default { get; } = CreateDefault();

    public void Register<T>(Func<T, object?> get, Action<T, object> set)
        where T : PropValue
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);
        _handlers[typeof(T)] = new Handler(
            value => get((T)value),
            (value, boxed) => set((T)value, boxed));
    }

    internal bool TryGet(Type type, out Handler handler)
        => _handlers.TryGetValue(type, out handler);

    private static PropValueRegistry CreateDefault()
    {
        var registry = new PropValueRegistry();
        registry.Register<FloatPropValue>(
            value => value.Value,
            (value, boxed) => value.Value = PropValue.ToSingle(boxed));
        registry.Register<DoublePropValue>(
            value => value.Value,
            (value, boxed) => value.Value = PropValue.ToDouble(boxed));
        registry.Register<IntPropValue>(
            value => value.Value,
            (value, boxed) => value.Value = PropValue.ToInt32(boxed));
        registry.Register<BoolPropValue>(
            value => value.Value,
            (value, boxed) => value.Value = PropValue.ToBoolean(boxed));
        registry.Register<StringPropValue>(
            value => value.Value,
            (value, boxed) => value.Value = PropValue.ToText(boxed));
        registry.Register<UintPropValue>(
            value => value.Value,
            (value, boxed) => value.Value = PropValue.ToUInt32(boxed));
        registry.Register<ColorPropValue>(
            value => value.Value,
            (value, boxed) => value.Value = PropValue.ToUInt32(boxed));
        return registry;
    }
}
