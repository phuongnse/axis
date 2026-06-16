using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Services;

public class TenantSlugGeneratorTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();

    private TenantSlugGenerator CreateGenerator() => new(_tenantRepo);

    [Theory]
    [InlineData("O'Brien & Co.", "o-brien-co")]
    [InlineData("  Acme  Corp  ", "acme-corp")]
    [InlineData("***", "")]
    public void GenerateBaseSlug_WhenInputIsProvided_ReturnsUrlSafeSlug(string input, string expected)
    {
        CreateGenerator().GenerateBaseSlug(input).Should().Be(expected);
    }

    [Fact]
    public void GenerateBaseSlug_WhenNull_ReturnsEmpty()
    {
        CreateGenerator().GenerateBaseSlug(null!).Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateUniqueSlugAsync_WhenBaseIsFree_ReturnsBase()
    {
        _tenantRepo.SlugExistsAsync(Arg.Any<TenantSlug>(), Arg.Any<CancellationToken>())
            .Returns(false);

        TenantSlug result = await CreateGenerator()
            .GenerateUniqueSlugAsync("Acme Corp", CancellationToken.None);

        result.Value.Should().Be("acme-corp");
    }

    [Fact]
    public async Task GenerateUniqueSlugAsync_WhenBaseCollides_ReturnsSuffixedSlug()
    {
        // First check (base) collides, second check (suffixed) is free.
        _tenantRepo.SlugExistsAsync(Arg.Any<TenantSlug>(), Arg.Any<CancellationToken>())
            .Returns(true, false);

        TenantSlug result = await CreateGenerator()
            .GenerateUniqueSlugAsync("Acme Corp", CancellationToken.None);

        result.Value.Should().NotBe("acme-corp");
        result.Value.Should().MatchRegex(@"^acme-corp-\d{4}$");
    }

    [Fact]
    public async Task GenerateUniqueSlugAsync_WhenNameExceedsMaxLength_TruncatesInsteadOfGuidFallback()
    {
        string longName = new('a', 80);
        _tenantRepo.SlugExistsAsync(Arg.Any<TenantSlug>(), Arg.Any<CancellationToken>())
            .Returns(false);

        TenantSlug result = await CreateGenerator()
            .GenerateUniqueSlugAsync(longName, CancellationToken.None);

        result.Value.Length.Should().BeLessThanOrEqualTo(63);
        result.Value.Should().NotStartWith("Tenant-"); // not the random GUID fallback
        result.Value.Should().MatchRegex("^a+$");
    }
}
