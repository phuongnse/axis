namespace Axis.Api.Endpoints;

public sealed record ConfigureStepRequest(
    string Name,
    IReadOnlyDictionary<string, object?>? Config);
