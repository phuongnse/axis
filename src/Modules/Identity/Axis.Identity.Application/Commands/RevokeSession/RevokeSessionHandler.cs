using Axis.Identity.Application.Services;
using Axis.Shared.Application.CQRS;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Commands.RevokeSession;

public sealed class RevokeSessionHandler(ISessionStore sessionStore)
    : ICommandHandler<RevokeSessionCommand>
{
    public async Task<Result> Handle(RevokeSessionCommand command, CancellationToken cancellationToken)
    {
        if (command.SessionId is null)
            await sessionStore.RevokeAllAsync(command.UserId, cancellationToken);
        else
            await sessionStore.RevokeAsync(command.SessionId, command.UserId, cancellationToken);

        return Result.Success();
    }
}
