using Microsoft.AspNetCore.Components;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;

namespace Penelopa.Web.Components.Panels.Props;

/// <summary>
/// 属性输入组件的共享状态：多选公共属性的冲突显示、稳定 id/ARIA 关联、
/// 校验错误的字段绑定，以及经由服务的原子批量写入。
/// </summary>
public abstract class PropEditorBase : ComponentBase
{
    /// <summary>当前编辑的全部图元；单选时只有一个。</summary>
    [Parameter]
    public IReadOnlyList<Primitive> Owners { get; set; } = Array.Empty<Primitive>();

    /// <summary>代表性属性（来自第一个选中图元）；所有 owner 都含同名同类型属性。</summary>
    [Parameter]
    public PropValue PropValue { get; set; } = null!;

    [Inject]
    protected IPrimitiveService PrimitiveService { get; set; } = null!;

    protected string Text { get; private set; } = string.Empty;

    protected string? Error { get; set; }

    protected bool HasConflict { get; private set; }

    protected string FieldId { get; private set; } = "penelopa-prop";

    protected string ErrorId => FieldId + "-error";

    protected string? ConflictPlaceholder => HasConflict ? "Multiple values" : null;

    protected override void OnParametersSet()
    {
        FieldId = BuildFieldId();
        RefreshFromOwners();
        Error = null;
    }

    /// <summary>把一个属性值格式化为输入框文本。</summary>
    protected abstract string Format(PropValue property);

    protected void OnInput(ChangeEventArgs e)
    {
        Text = e.Value?.ToString() ?? string.Empty;
    }

    protected void OnChange(ChangeEventArgs e)
        => Commit(e.Value?.ToString() ?? string.Empty);

    /// <summary>把文本提交给领域层；成功时刷新显示值，失败时保留用户输入并显示错误。</summary>
    protected bool Commit(string text)
    {
        Text = text;

        // 冲突占位不提交：清空输入框本身不代表“把属性设为空”
        if (HasConflict && string.IsNullOrWhiteSpace(text))
        {
            Error = null;
            RefreshFromOwners();
            return true;
        }

        if (PrimitiveService.TrySetProperty(Owners, PropValue.Name, text, out var error))
        {
            Error = null;
            RefreshFromOwners();
            return true;
        }

        Error = error;
        return false;
    }

    /// <summary>从所有权图元重新读取当前值后调用，供结构化编辑器同步自己的显示状态。</summary>
    protected virtual void OnRefreshed()
    {
    }

    private void RefreshFromOwners()
    {
        if (Owners.Count == 0)
        {
            Text = string.Empty;
            HasConflict = false;
            OnRefreshed();
            return;
        }

        var values = new List<object?>(Owners.Count);
        foreach (var owner in Owners)
        {
            var property = owner.Props.FirstOrDefault(
                candidate => string.Equals(candidate.Name, PropValue.Name, StringComparison.Ordinal));
            if (property is null)
            {
                Text = string.Empty;
                HasConflict = true;
                OnRefreshed();
                return;
            }

            values.Add(property.GetBoxedValue());
        }

        HasConflict = values.Distinct().Count() > 1;
        Text = HasConflict ? string.Empty : Format(PropValue);
        OnRefreshed();
    }

    private string BuildFieldId()
    {
        var owner = Owners.FirstOrDefault();
        var ownerToken = owner is null ? "none" : owner.Id.ToString("N");
        var nameToken = new string(PropValue.Name
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray());
        return $"penelopa-prop-{ownerToken}-{nameToken}";
    }
}
