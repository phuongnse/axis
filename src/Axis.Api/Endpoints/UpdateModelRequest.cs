namespace Axis.Api.Endpoints;

public record UpdateModelRequest(
    string Name,
    string? Description,
    string? Icon,
    string? Color);
