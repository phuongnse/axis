using Axis.Identity.Application.Queries.ListServiceIdentities;
using Axis.Identity.Application.Repositories;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class ListServiceIdentitiesHandlerTests
{
    [Fact]
    public async Task Handle_WhenActorIsNotAdministrator_DeniesBeforeIdentityRead()
    {
        IServiceIdentityRepository identities = Substitute.For<IServiceIdentityRepository>();
        ListServiceIdentitiesHandler handler = new(
            Substitute.For<IWorkspaceMembershipRepository>(),
            identities);

        Result<IReadOnlyList<ServiceIdentityDto>> result = await handler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        await identities.DidNotReceive().ListAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
