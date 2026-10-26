using System.Globalization;
using System.Text.Json;

namespace Penelopa.Core.Primitives;

/// <summary>附加在图元上的命名属性值。</summary>
public abstract class PropValue
{
    public string Name { get; set; }

    protected PropValue(string name)
    {
        Name = name;
    }

    /// <summary>验证当前值是否满足属性约束（加载文档时使用）。</summary>
    public virtual bool IsValidValue(out string? error)
    {
        error = null;
        return true;
    }

    /// <summary>解析并校验文本；只有成功时才写入值。属性面板与命令边界使用。</summary>
    public virtual bool TrySetText(string text, out string? error)
    {
        error = $"Property '{Name}' cannot be edited.";
        return false;
    }

    /// <summary>
    /// 将属性值装箱为 <see cref="object"/>，用于序列化和历史快照。
    /// 未知的 <see cref="PropValue"/> 子类型会抛出 <see cref="NotSupportedException"/>，
    /// 而不是让历史/序列化静默丢弃该属性。
    /// </summary>
    public object? GetBoxedValue() => this switch
    {
        FloatPropValue fp => fp.Value,
        DoublePropValue dp => dp.Value,
        IntPropValue ip => ip.Value,
        BoolPropValue bp => bp.Value,
        StringPropValue sp => sp.Value,
        UintPropValue up => up.Value,
        _ => throw UnsupportedType(),
    };

    /// <summary>
    /// 从装箱值设置属性。JSON 回读的值是 <see cref="JsonElement"/>，
    /// 因此同时接受 JsonElement 和普通装箱值（来自历史快照）。
    /// 未知的 <see cref="PropValue"/> 子类型会抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    public void SetBoxedValue(object value)
    {
        switch (this)
        {
            case FloatPropValue fp: fp.Value = ToSingle(value); break;
            case DoublePropValue dp: dp.Value = ToDouble(value); break;
            case IntPropValue ip: ip.Value = ToInt32(value); break;
            case BoolPropValue bp: bp.Value = ToBoolean(value); break;
            case StringPropValue sp: sp.Value = ToText(value); break;
            case UintPropValue up: up.Value = ToUInt32(value); break;
            default: throw UnsupportedType();
        }
    }

    private NotSupportedException UnsupportedType()
        => new($"PropValue type '{GetType().Name}' is not supported. Register a conversion before using it in history or serialization.");

    private static float ToSingle(object value)
        => value is JsonElement element ? element.GetSingle() : Convert.ToSingle(value);

    private static double ToDouble(object value)
        => value is JsonElement element ? element.GetDouble() : Convert.ToDouble(value);

    private static int ToInt32(object value)
        => value is JsonElement element ? element.GetInt32() : Convert.ToInt32(value);

    private static bool ToBoolean(object value)
        => value is JsonElement element ? element.GetBoolean() : Convert.ToBoolean(value);

    private static string ToText(object value)
        => value is JsonElement element ? element.GetString() ?? string.Empty : Convert.ToString(value) ?? string.Empty;

    private static uint ToUInt32(object value)
        => value is JsonElement element ? element.GetUInt32() : Convert.ToUInt32(value);
}

public interface IPropValue<T>
{
    T Value { get; set; }
}

public class IntPropValue : PropValue, IPropValue<int>
{
    public int Value { get; set; }

    public IntPropValue(string name, int value) : base(name)
    {
        Value = value;
    }

    public override bool TrySetText(string text, out string? error)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            error = "Enter a whole number.";
            return false;
        }

        Value = value;
        error = null;
        return true;
    }
}

public class FloatPropValue : PropValue, IPropValue<float>
{
    private readonly float? _exclusiveMinimum;
    private readonly float? _minimum;
    private readonly float? _maximum;

    public float Value { get; set; }

    public FloatPropValue(string name, float value) : base(name)
    {
        Value = value;
    }

    public FloatPropValue(
        string name,
        float value,
        float? exclusiveMinimum,
        float? minimum = null,
        float? maximum = null) : base(name)
    {
        Value = value;
        _exclusiveMinimum = exclusiveMinimum;
        _minimum = minimum;
        _maximum = maximum;
    }

    public override bool IsValidValue(out string? error) => Validate(Value, out error);

    /// <summary>校验候选值而不写入；仿真关键帧等非属性面板路径使用。</summary>
    public bool ValidateValue(float value, out string? error) => Validate(value, out error);

    public override bool TrySetText(string text, out string? error)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            error = "Enter a valid number.";
            return false;
        }

        if (!Validate(value, out error))
        {
            return false;
        }

        Value = value;
        return true;
    }

    private bool Validate(float value, out string? error)
    {
        if (!float.IsFinite(value))
        {
            error = "Value must be a finite number.";
            return false;
        }

        if (!NumericValidation.WithinMagnitude(value, out error))
        {
            return false;
        }

        if (_exclusiveMinimum is { } exclusiveMin && value <= exclusiveMin)
        {
            error = $"Value must be greater than {exclusiveMin.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (_minimum is { } min && value < min)
        {
            error = $"Value must be at least {min.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (_maximum is { } max && value > max)
        {
            error = $"Value must be at most {max.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        error = null;
        return true;
    }
}

public class DoublePropValue : PropValue, IPropValue<double>
{
    public double Value { get; set; }

    public DoublePropValue(string name, double value) : base(name)
    {
        Value = value;
    }

    public override bool IsValidValue(out string? error)
        => NumericValidation.IsFinite(Value, out error) && NumericValidation.WithinMagnitude(Value, out error);

    public override bool TrySetText(string text, out string? error)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            error = "Enter a valid number.";
            return false;
        }

        if (!NumericValidation.IsFinite(value, out error) || !NumericValidation.WithinMagnitude(value, out error))
        {
            return false;
        }

        Value = value;
        return true;
    }
}

public class BoolPropValue : PropValue, IPropValue<bool>
{
    public bool Value { get; set; }

    public BoolPropValue(string name, bool value) : base(name)
    {
        Value = value;
    }

    public override bool TrySetText(string text, out string? error)
    {
        if (!bool.TryParse(text, out var value))
        {
            error = "Choose true or false.";
            return false;
        }

        Value = value;
        error = null;
        return true;
    }
}

public class StringPropValue : PropValue, IPropValue<string>
{
    private readonly bool _allowEmpty;
    private readonly bool _trim;

    public string Value { get; set; }

    /// <summary>允许的最大字符数；属性编辑器据此设置 maxlength。</summary>
    public int MaxLength { get; }

    public StringPropValue(string name, string value) : base(name)
    {
        Value = value;
        _allowEmpty = true;
        MaxLength = int.MaxValue;
    }

    public StringPropValue(
        string name,
        string value,
        bool allowEmpty,
        int maxLength,
        bool trim = false) : base(name)
    {
        if (maxLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLength));
        }

        Value = value;
        _allowEmpty = allowEmpty;
        MaxLength = maxLength;
        _trim = trim;
    }

    public override bool IsValidValue(out string? error) => Validate(Value, out error);

    public override bool TrySetText(string text, out string? error)
    {
        var candidate = text ?? string.Empty;
        if (_trim)
        {
            candidate = candidate.Trim();
        }

        if (!Validate(candidate, out error))
        {
            return false;
        }

        Value = candidate;
        return true;
    }

    private bool Validate(string value, out string? error)
    {
        if (!_allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            error = $"{Name} cannot be empty.";
            return false;
        }

        if (value.Length > MaxLength)
        {
            error = $"{Name} cannot be longer than {MaxLength} characters.";
            return false;
        }

        error = null;
        return true;
    }
}

public class UintPropValue : PropValue, IPropValue<uint>
{
    public uint Value { get; set; }

    public UintPropValue(string name, uint value) : base(name)
    {
        Value = value;
    }

    public override bool TrySetText(string text, out string? error)
    {
        if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            error = "Enter a whole number between 0 and 4294967295.";
            return false;
        }

        Value = value;
        error = null;
        return true;
    }
}

/// <summary>数值属性的公共范围校验：拒绝 NaN/Infinity 与超出编辑器量级的用户输入。</summary>
internal static class NumericValidation
{
    private const double MaxMagnitude = 1e7;

    public static bool IsFinite(double value, out string? error)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            error = "Value must be a finite number.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool WithinMagnitude(double value, out string? error)
    {
        if (Math.Abs(value) > MaxMagnitude)
        {
            error = "Value is too large.";
            return false;
        }

        error = null;
        return true;
    }
}
