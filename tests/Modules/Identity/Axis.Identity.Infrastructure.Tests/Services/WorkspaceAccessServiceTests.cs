using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.Subscriptions;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Services;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace Axis.Identity.Infrastructure.Tests.Services;

public sealed class WorkspaceAccessServiceTests
{
    private readonly IWorkspaceRepository _WorkspaceRepository = Substitute.For<IWorkspaceRepository>();

    private WorkspaceAccessService CreateSut() =>
        new(_WorkspaceRepository);

    private static Workspace ActiveWorkspace()
    {
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        Workspace.BeginProvisioning();
        Workspace.CompleteProvisioning();
        return Workspace;
    }

    [Fact]
    public async Task EvaluateAsync_WhenWorkspaceNotFound_ReturnsForbidden()
    {
        Guid workspaceId = Guid.NewGuid();
        _WorkspaceRepository.GetByIdAsync(workspaceId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        Result result = await CreateSut().EvaluateAsync(workspaceId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Workspace is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenWorkspaceIsActive_ReturnsSuccess()
    {
        Workspace Workspace = ActiveWorkspace();
        _WorkspaceRepository.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);

        Result result = await CreateSut().EvaluateAsync(Workspace.Id);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_WhenWorkspaceIsArchived_ReturnsForbidden()
    {
        Workspace Workspace = ActiveWorkspace();
        Workspace.Archive();
        _WorkspaceRepository.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);

        Result result = await CreateSut().EvaluateAsync(Workspace.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Workspace is not available.");
    }

    [Fact]
    public async Task EvaluateAsync_WhenWorkspaceIsProvisioning_ReturnsNotReadyMessage()
    {
        Workspace Workspace = Workspace.Create(
            "Acme",
            WorkspaceSlug.Create("acme").Value!,
            Email.Create("admin@acme.com").Value!,
            WellKnownSubscriptionPlans.FreeId);
        Workspace.BeginProvisioning();
        _WorkspaceRepository.GetByIdAsync(Workspace.Id, Arg.Any<CancellationToken>())
            .Returns(Workspace);

        Result result = await CreateSut().EvaluateAsync(Workspace.Id);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        result.Error.Should().Be("Workspace is still being set up. Try again shortly.");
    }
}
