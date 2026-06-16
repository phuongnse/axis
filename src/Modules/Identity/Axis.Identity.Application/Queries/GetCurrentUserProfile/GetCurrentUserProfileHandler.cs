using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Application.CQRS;

namespace Axis.Identity.Application.Queries.GetCurrentUserProfile;

public sealed class GetCurrentUserProfileHandler(IUserRepository userRepository)
    : IQueryHandler<GetCurrentUserProfileQuery, CurrentUserProfileDto?>
{
    public async Task<CurrentUserProfileDto?> Handle(
        GetCurrentUserProfileQuery query,
        CancellationToken cancellationToken)
    {
        User? user = await userRepository.GetByIdPlatformWideAsync(query.UserId, cancellationToken);
        if (user is null)
            return null;

        return new CurrentUserProfileDto(
            user.Id,
            user.Email.Value,
            user.FirstName,
            user.LastName,
            $"{user.FirstName} {user.LastName}",
            user.AvatarUrl,
            user.Status == UserStatus.Active,
            query.TeamAccountId,
            query.Permissions);
    }
}
