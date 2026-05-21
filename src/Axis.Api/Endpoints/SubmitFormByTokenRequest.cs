namespace Axis.Api.Endpoints;

public sealed record SubmitFormByTokenRequest(
    IReadOnlyDictionary<string, object?> Data);
