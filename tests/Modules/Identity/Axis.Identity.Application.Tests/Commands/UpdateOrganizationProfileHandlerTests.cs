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
