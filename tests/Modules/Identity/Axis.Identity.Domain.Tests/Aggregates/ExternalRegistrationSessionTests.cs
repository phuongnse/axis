using Axis.Identity.Domain;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.Aggregates;

public class ExternalRegistrationSessionTests
{
    [Fact]
    public void Create_WithValidInput_InitializesSession()
    {
        Email email = Email.Create("user@example.com").Value!;

        ExternalRegistrationSession session = ExternalRegistrationSession.Create(
            ExternalIdentityProvider.Microsoft,
            "ms-123",
            email,
            "Jane Doe");

        session.Email.Should().Be(email);
        session.Provider.Should().Be(ExternalIdentityProvider.Microsoft);
        session.ProviderKey.Should().Be("ms-123");
        session.DisplayName.Should().Be("Jane Doe");
        session.IsCompleted.Should().BeFalse();
        session.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void MarkCompleted_WhenActive_SetsCompletedAt()
    {
        Email email = Email.Create("user@example.com").Value!;
        ExternalRegistrationSession session = ExternalRegistrationSession.Create(
            ExternalIdentityProvider.GitHub,
            "gh-123",
            email,
            "Dev User");

        session.MarkCompleted();

        session.IsCompleted.Should().BeTrue();
    }
}
