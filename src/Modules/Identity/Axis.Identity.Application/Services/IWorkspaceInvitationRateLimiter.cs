using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

public interface IWorkspaceInvitationRateLimiter
{
    Task<Result> AcquireCreateAsync(
        Guid inviterUserId,
        Guid workspaceId,
        string normalizedEmail,
        CancellationToken ct = default);
    Task<Result> AcquireResendAsync(
        Guid inviterUserId,
        Guid invitationId,
        CancellationToken ct = default);
    Task<Result> AcquireExchangeAsync(
        string requestPartition,
        string tokenHash,
        CancellationToken ct = default);
}
