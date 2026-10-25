using Penelopa.Core.Interaction;
using Penelopa.Core.Primitives;
using Penelopa.Core.Services;

namespace Penelopa.Core.Tests;

/// <summary>把交互控制器接到真实的 <see cref="PrimitiveService"/>，用于验证事务/取消/选择规范化。</summary>
internal sealed class ServiceInteractionHost : IEditorInteractionHost
{
    public ServiceInteractionHost(PrimitiveService service)
    {
        Service = service;
    }

    public PrimitiveService Service { get; }

    public List<Primitive> Notified { get; } = new();

    public int MutationStarts { get; private set; }

    public int MutationCommits { get; private set; }

    public int MutationCancels { get; private set; }

    public IReadOnlyList<Primitive> GetSelection() => Service.GetSelection().ToList();

    public bool IsSelected(Primitive primitive) => Service.GetSelection().Contains(primitive);

    public void SetSelected(Primitive primitive) => Service.SetSelected(primitive);

    public void AppendSelected(Primitive primitive) => Service.AppendSelected(primitive);

    public void ToggleSelected(Primitive primitive)
    {
        if (IsSelected(primitive))
        {
            Service.SetSelectedRange(Service.GetSelection().Where(p => !ReferenceEquals(p, primitive)));
        }
        else
        {
            Service.AppendSelected(primitive);
        }
    }

    public void ClearSelection() => Service.ClearSelection();

    public void PanByCss(float deltaX, float deltaY)
    {
    }

    public void BeginMutation()
    {
        MutationStarts++;
        Service.BeginTransaction();
    }

    public void CommitMutation()
    {
        MutationCommits++;
        Service.CommitTransaction();
    }

    public void CancelMutation()
    {
        MutationCancels++;
        Service.CancelTransaction();
    }

    public void NotifyPrimitivesChanged(IReadOnlyList<Primitive> primitives)
    {
        Notified.AddRange(primitives);
        Service.NotifyPrimitivesChanged(primitives);
    }
}
