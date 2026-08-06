using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

public sealed class CreateOrganizationIdempotencyRepositoryTests
{
    [Fact]
    public void CreateScopedKey_WhenRawKeyIsReusedByDifferentUsers_UsesDifferentScopes()
    {
        string first = CreateOrganizationIdempotencyRepository.CreateScopedKey(Guid.NewGuid(), "key");
        string second = CreateOrganizationIdempotencyRepository.CreateScopedKey(Guid.NewGuid(), "key");

        first.Should().NotBe(second);
    }

    [Fact]
    public void CreateScopedKey_WhenUserAndRawKeyMatch_IsStable()
    {
        Guid userId = Guid.NewGuid();

        string first = CreateOrganizationIdempotencyRepository.CreateScopedKey(userId, "key");
        string second = CreateOrganizationIdempotencyRepository.CreateScopedKey(userId, "key");

        first.Should().Be(second).And.HaveLength(64);
    }
}
