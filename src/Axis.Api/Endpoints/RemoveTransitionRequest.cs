namespace Axis.Api.Endpoints;

public sealed record RemoveTransitionRequest(Guid FromStepId, Guid ToStepId);
