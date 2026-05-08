using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Commands.RevokeSession;

/// <summary>
/// Revokes a specific session by ID, or all sessions when SessionId is null ("sign out everywhere").
/// </summary>
public record RevokeSessionCommand(string? SessionId, Guid UserId) : ICommand;
