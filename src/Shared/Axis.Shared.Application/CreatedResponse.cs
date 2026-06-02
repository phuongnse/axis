namespace Axis.Shared.Application;

/// <summary>
/// Standard response body for a <c>201 Created</c> result: the id of the
/// newly created resource. The resource URL is carried in the Location header.
/// Shared envelope (like <see cref="PagedResult{T}"/>) so every create endpoint
/// emits a typed, code-generatable contract instead of an anonymous object.
/// </summary>
public sealed record CreatedResponse(Guid Id);
