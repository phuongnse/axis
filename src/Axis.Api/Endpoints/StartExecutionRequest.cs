namespace Axis.Api.Endpoints;

public sealed record StartExecutionRequest(
    IReadOnlyDictionary<string, object?>? Input);
