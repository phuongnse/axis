using Axis.Identity.Application.Queries.GetExternalRegistrationSession;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Queries;

public class GetExternalRegistrationSessionHandlerTests
{
    private readonly IExternalRegistrationSessionRepository _sessionRepo =
        Substitute.For<IExternalRegistrationSessionRepository>();

    private GetExternalRegistrationSessionHandler CreateHandler() => new(_sessionRepo);

    private static ExternalRegistrationSession MakeSession() =>
        ExternalRegistrationSession.Create(
            ExternalIdentityProvider.Google,
            "provider-key",
            Email.Create("sso@example.com").Value!,
            "SSO User");

    [Fact]
    public async Task Handle_WhenSessionIsValid_ReturnsDto()
    {
        ExternalRegistrationSession session = MakeSession();
        _sessionRepo.GetByIdAsync(session.Id).Returns(session);

        ExternalRegistrationSessionDto? result = await CreateHandler().Handle(
            new GetExternalRegistrationSessionQuery(session.Id),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Email.Should().Be("sso@example.com");
        result.DisplayName.Should().Be("SSO User");
    }

    [Fact]
    public async Task Handle_WhenSessionMissing_ReturnsNull()
    {
        Guid id = Guid.NewGuid();
        _sessionRepo.GetByIdAsync(id).Returns((ExternalRegistrationSession?)null);

        ExternalRegistrationSessionDto? result = await CreateHandler().Handle(
            new GetExternalRegistrationSessionQuery(id),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenSessionAlreadyCompleted_ReturnsNull()
    {
        ExternalRegistrationSession session = MakeSession();
        session.MarkCompleted();
        _sessionRepo.GetByIdAsync(session.Id).Returns(session);

        ExternalRegistrationSessionDto? result = await CreateHandler().Handle(
            new GetExternalRegistrationSessionQuery(session.Id),
            CancellationToken.None);

        result.Should().BeNull();
    }
}
