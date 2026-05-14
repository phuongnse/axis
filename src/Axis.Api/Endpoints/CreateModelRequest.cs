namespace Axis.Api.Endpoints;

public record CreateModelRequest(
    string Name,
    string? Description,
    string? Icon,
    string? Color);
