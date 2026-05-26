using Axis.Identity.Application.Commands.UpdateOrganizationProfile;
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

public class UpdateOrganizationProfileHandlerTests
{
    private readonly IOrganizationRepository _orgRepo = Substitute.For<IOrganizationRepository>();
    private readonly IOrganizationLogoStorageService _logoStorage = Substitute.For<IOrganizationLogoStorageService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid OrgId = Guid.NewGuid();

    private UpdateOrganizationProfileHandler CreateHandler() => new(_orgRepo, _logoStorage, _uow);

    private static Organization MakeOrganization() =>
        Organization.Create(
            "Acme",
            OrganizationSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);

    [Fact]
    public async Task UpdateOrganizationProfile_WhenInvalidLanguage_ReturnsFailure()
    {
        Organization org = MakeOrganization();
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);

        Result result = await CreateHandler().Handle(
            new UpdateOrganizationProfileCommand(OrgId, "Acme", null, "english", null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _logoStorage.DidNotReceive().UploadLogoAsync(
            Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOrganizationProfile_WhenValidLanguageTags_Accepted()
    {
        Organization org = MakeOrganization();
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);

        Result result = await CreateHandler().Handle(
            new UpdateOrganizationProfileCommand(OrgId, "Acme", null, "zh-Hant", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        org.DefaultLanguage.Should().Be("zh-Hant");
    }

    [Fact]
    public async Task UpdateOrganizationProfile_WhenSaveFails_DeletesUploadedLogo()
    {
        Organization org = MakeOrganization();
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(OrgId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/org-logos/new.png");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("db down")));

        Func<Task> act = () => CreateHandler().Handle(
            new UpdateOrganizationProfileCommand(OrgId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _logoStorage.Received(1).DeleteLogoAsync(
            "https://bucket.s3.amazonaws.com/org-logos/new.png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOrganizationProfile_WhenOldLogoDeleteFailsAfterSave_StillSucceeds()
    {
        Organization org = MakeOrganization();
        org.UpdateLogoUrl("https://bucket.s3.amazonaws.com/org-logos/old.png");
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(OrgId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/org-logos/new.png");
        _logoStorage.DeleteLogoAsync("https://bucket.s3.amazonaws.com/org-logos/old.png", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("s3 delete failed")));

        Result result = await CreateHandler().Handle(
            new UpdateOrganizationProfileCommand(OrgId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateOrganizationProfile_WhenValid_UpdatesWithoutLogo()
    {
        Organization org = MakeOrganization();
        _orgRepo.GetByIdAsync(OrgId, Arg.Any<CancellationToken>()).Returns(org);

        Result result = await CreateHandler().Handle(
            new UpdateOrganizationProfileCommand(OrgId, "New Name", "UTC", "en-US", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        org.Name.Should().Be("New Name");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
