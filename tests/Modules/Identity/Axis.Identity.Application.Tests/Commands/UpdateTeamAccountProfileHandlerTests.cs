using Axis.Identity.Application.Commands.UpdateTeamAccountProfile;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Application.Tests.Commands;

public class UpdateTeamAccountProfileHandlerTests
{
    private readonly ITeamAccountRepository _teamAccountRepo = Substitute.For<ITeamAccountRepository>();
    private readonly ITeamAccountLogoStorageService _logoStorage = Substitute.For<ITeamAccountLogoStorageService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TeamAccountId = Guid.NewGuid();

    private UpdateTeamAccountProfileHandler CreateHandler() => new(_teamAccountRepo, _logoStorage, _uow);

    private static TeamAccount MakeTeamAccount() =>
        TeamAccount.Create(
            "Acme",
            TeamAccountSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);

    [Fact]
    public async Task UpdateTeamAccountProfile_WhenInvalidLanguage_ReturnsFailure()
    {
        TeamAccount teamAccount = MakeTeamAccount();
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);

        Result result = await CreateHandler().Handle(
            new UpdateTeamAccountProfileCommand(TeamAccountId, "Acme", null, "english", null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _logoStorage.DidNotReceive().UploadLogoAsync(
            Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTeamAccountProfile_WhenValidLanguageTags_Accepted()
    {
        TeamAccount teamAccount = MakeTeamAccount();
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);

        Result result = await CreateHandler().Handle(
            new UpdateTeamAccountProfileCommand(TeamAccountId, "Acme", null, "zh-Hant", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        teamAccount.DefaultLanguage.Should().Be("zh-Hant");
    }

    [Fact]
    public async Task UpdateTeamAccountProfile_WhenSaveFails_DeletesUploadedLogo()
    {
        TeamAccount teamAccount = MakeTeamAccount();
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(TeamAccountId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/team-account-logos/new.png");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("db down")));

        Func<Task> act = () => CreateHandler().Handle(
            new UpdateTeamAccountProfileCommand(TeamAccountId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _logoStorage.Received(1).DeleteLogoAsync(
            "https://bucket.s3.amazonaws.com/team-account-logos/new.png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTeamAccountProfile_WhenOldLogoDeleteFailsAfterSave_StillSucceeds()
    {
        TeamAccount teamAccount = MakeTeamAccount();
        teamAccount.UpdateLogoUrl("https://bucket.s3.amazonaws.com/team-account-logos/old.png");
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(TeamAccountId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/team-account-logos/new.png");
        _logoStorage.DeleteLogoAsync("https://bucket.s3.amazonaws.com/team-account-logos/old.png", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("s3 delete failed")));

        Result result = await CreateHandler().Handle(
            new UpdateTeamAccountProfileCommand(TeamAccountId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTeamAccountProfile_WhenValid_UpdatesWithoutLogo()
    {
        TeamAccount teamAccount = MakeTeamAccount();
        _teamAccountRepo.GetByIdAsync(TeamAccountId, Arg.Any<CancellationToken>()).Returns(teamAccount);

        Result result = await CreateHandler().Handle(
            new UpdateTeamAccountProfileCommand(TeamAccountId, "New Name", "UTC", "en-US", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        teamAccount.Name.Should().Be("New Name");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
