namespace Penelopa.Core.Simulation;

/// <summary>校验问题的来源：结构/领域限制，或关键帧值不满足目标属性约束。</summary>
public enum SimulationIssueKind
{
    Structure,
    Value,
}

/// <summary>一条可定位到轨道/关键帧的仿真校验问题。</summary>
public sealed record SimulationValidationIssue(
    SimulationIssueKind Kind,
    Guid? PrimitiveId,
    string? PropertyName,
    int? KeyframeIndex,
    string Message);
