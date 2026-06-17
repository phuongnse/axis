using Axis.Identity.Application.Commands.UpdateWorkspaceProfile;
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

public class UpdateWorkspaceProfileHandlerTests
{
    private readonly IWorkspaceRepository _workspaceRepo = Substitute.For<IWorkspaceRepository>();
    private readonly IWorkspaceLogoStorageService _logoStorage = Substitute.For<IWorkspaceLogoStorageService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private UpdateWorkspaceProfileHandler CreateHandler() => new(_workspaceRepo, _logoStorage, _uow);

    private static Workspace MakeWorkspace() =>
        Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value,
            Email.Create("owner@acme.com").Value,
            WellKnownSubscriptionPlans.FreeId);

    [Fact]
    public async Task UpdateWorkspaceProfile_WhenInvalidLanguage_ReturnsFailure()
    {
        Workspace Workspace = MakeWorkspace();
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);

        Result result = await CreateHandler().Handle(
            new UpdateWorkspaceProfileCommand(WorkspaceId, "Acme", null, "english", null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _logoStorage.DidNotReceive().UploadLogoAsync(
            Arg.Any<Guid>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWorkspaceProfile_WhenValidLanguageTags_Accepted()
    {
        Workspace Workspace = MakeWorkspace();
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);

        Result result = await CreateHandler().Handle(
            new UpdateWorkspaceProfileCommand(WorkspaceId, "Acme", null, "zh-Hant", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Workspace.DefaultLanguage.Should().Be("zh-Hant");
    }

    [Fact]
    public async Task UpdateWorkspaceProfile_WhenSaveFails_DeletesUploadedLogo()
    {
        Workspace Workspace = MakeWorkspace();
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(WorkspaceId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/Workspace-logos/new.png");
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new InvalidOperationException("db down")));

        Func<Task> act = () => CreateHandler().Handle(
            new UpdateWorkspaceProfileCommand(WorkspaceId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _logoStorage.Received(1).DeleteLogoAsync(
            "https://bucket.s3.amazonaws.com/Workspace-logos/new.png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWorkspaceProfile_WhenOldLogoDeleteFailsAfterSave_StillSucceeds()
    {
        Workspace Workspace = MakeWorkspace();
        Workspace.UpdateLogoUrl("https://bucket.s3.amazonaws.com/Workspace-logos/old.png");
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);
        byte[] logo = [0x89, 0x50, 0x4E, 0x47];
        _logoStorage.UploadLogoAsync(WorkspaceId, logo, "image/png", Arg.Any<CancellationToken>())
            .Returns("https://bucket.s3.amazonaws.com/Workspace-logos/new.png");
        _logoStorage.DeleteLogoAsync("https://bucket.s3.amazonaws.com/Workspace-logos/old.png", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("s3 delete failed")));

        Result result = await CreateHandler().Handle(
            new UpdateWorkspaceProfileCommand(WorkspaceId, "Acme", null, null, logo, "image/png"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateWorkspaceProfile_WhenValid_UpdatesWithoutLogo()
    {
        Workspace Workspace = MakeWorkspace();
        _workspaceRepo.GetByIdAsync(WorkspaceId, Arg.Any<CancellationToken>()).Returns(Workspace);

        Result result = await CreateHandler().Handle(
            new UpdateWorkspaceProfileCommand(WorkspaceId, "New Name", "UTC", "en-US", null, null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        Workspace.Name.Should().Be("New Name");
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
