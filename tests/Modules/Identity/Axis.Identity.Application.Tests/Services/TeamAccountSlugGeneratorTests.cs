using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Services;

public class TeamAccountSlugGeneratorTests
{
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();

    private TeamAccountSlugGenerator CreateGenerator() => new(_teamAccountRepo);

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
        _teamAccountRepo.SlugExistsAsync(Arg.Any<TeamAccountSlug>(), Arg.Any<CancellationToken>())
            .Returns(false);

        TeamAccountSlug result = await CreateGenerator()
            .GenerateUniqueSlugAsync("Acme Corp", CancellationToken.None);

        result.Value.Should().Be("acme-corp");
    }

    [Fact]
    public async Task GenerateUniqueSlugAsync_WhenBaseCollides_ReturnsSuffixedSlug()
    {
        // First check (base) collides, second check (suffixed) is free.
        _teamAccountRepo.SlugExistsAsync(Arg.Any<TeamAccountSlug>(), Arg.Any<CancellationToken>())
            .Returns(true, false);

        TeamAccountSlug result = await CreateGenerator()
            .GenerateUniqueSlugAsync("Acme Corp", CancellationToken.None);

        result.Value.Should().NotBe("acme-corp");
        result.Value.Should().MatchRegex(@"^acme-corp-\d{4}$");
    }

    [Fact]
    public async Task GenerateUniqueSlugAsync_WhenNameExceedsMaxLength_TruncatesInsteadOfGuidFallback()
    {
        string longName = new('a', 80);
        _teamAccountRepo.SlugExistsAsync(Arg.Any<TeamAccountSlug>(), Arg.Any<CancellationToken>())
            .Returns(false);

        TeamAccountSlug result = await CreateGenerator()
            .GenerateUniqueSlugAsync(longName, CancellationToken.None);

        result.Value.Length.Should().BeLessThanOrEqualTo(63);
        result.Value.Should().NotStartWith("team-account-"); // not the random GUID fallback
        result.Value.Should().MatchRegex("^a+$");
    }
}
