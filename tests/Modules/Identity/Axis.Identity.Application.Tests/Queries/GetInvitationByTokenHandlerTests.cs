using Axis.Identity.Application.Queries.GetInvitationByToken;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Queries;

public sealed class GetInvitationByTokenHandlerTests
{
    private readonly IInvitationRepository _invitationRepo = Substitute.For<IInvitationRepository>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static readonly Guid InvitedById = Guid.NewGuid();

    private GetInvitationByTokenHandler CreateHandler() => new(_invitationRepo);

    [Fact]
    public async Task Handle_WhenTokenNotFound_ReturnsNull()
    {
        _invitationRepo.GetByTokenAsync("missing-token", Arg.Any<CancellationToken>()).ReturnsNull();

        InvitationByTokenDto? dto = await CreateHandler().Handle(
            new GetInvitationByTokenQuery("missing-token"),
            CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenTokenExists_ReturnsInvitationDetails()
    {
        Invitation invitation = Invitation.Create(
            Email.Create("invitee@acme.com").Value,
            WorkspaceId,
            RoleId,
            InvitedById);
        _invitationRepo.GetByTokenAsync(invitation.Token, Arg.Any<CancellationToken>()).Returns(invitation);

        InvitationByTokenDto? dto = await CreateHandler().Handle(
            new GetInvitationByTokenQuery(invitation.Token),
            CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.InvitationId.Should().Be(invitation.Id);
        dto.Email.Should().Be("invitee@acme.com");
        dto.Status.Should().Be("pending");
        dto.ExpiresAt.Should().Be(invitation.ExpiresAt);
    }
}
