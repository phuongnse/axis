using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.Api.Endpoints;

public sealed record AddTriggerRequest(
    TriggerType TriggerType,
    IReadOnlyDictionary<string, object?>? Config);
