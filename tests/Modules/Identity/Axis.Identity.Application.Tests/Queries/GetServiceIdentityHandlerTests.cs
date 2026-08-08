using Axis.Identity.Application.Queries.GetServiceIdentity;
using Axis.Identity.Application.Repositories;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetServiceIdentityHandlerTests
{
    [Fact]
    public async Task Handle_WhenIdentityIsMissing_ReturnsNotFoundBeforeAuthorityLookup()
    {
        IWorkspaceMembershipRepository memberships = Substitute.For<IWorkspaceMembershipRepository>();
        GetServiceIdentityHandler handler = new(
            memberships,
            Substitute.For<IServiceIdentityRepository>());

        Result<ServiceIdentityDto> result = await handler.Handle(
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await memberships.DidNotReceive().GetActiveAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
