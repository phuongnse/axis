using Axis.Identity.Application.Commands.UpdateTenantProfile;
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

public class UpdateTenantProfileHandlerTests
{
    private readonly ITenantRepository _tenantRepo = Substitute.For<ITenantRepository>();
    private readonly ITenantLogoStorageService _logoStorage = Substitute.For<ITenantLogoStorageService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid TenantId = Guid.NewGuid();

    private UpdateTenantProfileHandler CreateHandler() => new(_tenantRepo, _logoStorage, _uow);

    private static Tenant MakeTenant() =>
        Tenant.Create(
            "Acme",
            TenantSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);

    [Fact]
    public async Task UpdateTenantProfile_WhenInvalidLanguage_ReturnsFailure()
    {
        Tenant Tenant = MakeTenant();
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);

        Result result = await CreateHandler().Handle(
            new UpdateTenantProfileCommand(TenantId, "Acme", null, "english", null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _logoStorage.DidNotReceive().UploadLogoAsync(
            Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTenantProfile_WhenValidLanguageTags_Accepted()
    {
        Tenant Tenant = MakeTenant();
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);

        Result result = await CreateHandler().Handle(
            new UpdateTenantProfileCommand(TenantId, "Acme", null, "zh-Hant", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Tenant.DefaultLanguage.Should().Be("zh-Hant");
    }

    [Fact]
    public async Task UpdateTenantProfile_WhenSaveFails_DeletesUploadedLogo()
    {
        Tenant Tenant = MakeTenant();
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(TenantId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/Tenant-logos/new.png");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("db down")));

        Func<Task> act = () => CreateHandler().Handle(
            new UpdateTenantProfileCommand(TenantId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _logoStorage.Received(1).DeleteLogoAsync(
            "https://bucket.s3.amazonaws.com/Tenant-logos/new.png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTenantProfile_WhenOldLogoDeleteFailsAfterSave_StillSucceeds()
    {
        Tenant Tenant = MakeTenant();
        Tenant.UpdateLogoUrl("https://bucket.s3.amazonaws.com/Tenant-logos/old.png");
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(TenantId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/Tenant-logos/new.png");
        _logoStorage.DeleteLogoAsync("https://bucket.s3.amazonaws.com/Tenant-logos/old.png", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("s3 delete failed")));

        Result result = await CreateHandler().Handle(
            new UpdateTenantProfileCommand(TenantId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateTenantProfile_WhenValid_UpdatesWithoutLogo()
    {
        Tenant Tenant = MakeTenant();
        _tenantRepo.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Tenant);

        Result result = await CreateHandler().Handle(
            new UpdateTenantProfileCommand(TenantId, "New Name", "UTC", "en-US", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Tenant.Name.Should().Be("New Name");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
