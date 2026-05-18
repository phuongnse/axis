using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.Api.Endpoints;

public sealed record AddStepRequest(
    string Name,
    StepType StepType,
    IReadOnlyDictionary<string, object?>? Config);
