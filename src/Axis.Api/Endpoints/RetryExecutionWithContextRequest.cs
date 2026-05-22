namespace Axis.Api.Endpoints;

public sealed record RetryExecutionWithContextRequest(
    IReadOnlyDictionary<string, object?> ModifiedContext);
