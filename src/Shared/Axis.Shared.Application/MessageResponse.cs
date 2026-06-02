namespace Axis.Shared.Application;

/// <summary>
/// Standard response body for an action whose only payload is a human-readable
/// message (e.g. confirmation text). Shared envelope so endpoints emit a typed,
/// code-generatable contract instead of an anonymous object.
/// </summary>
public sealed record MessageResponse(string Message);
