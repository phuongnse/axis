namespace Axis.Api.Endpoints;

public sealed record AddTransitionRequest(Guid FromStepId, Guid ToStepId, string? Label);
