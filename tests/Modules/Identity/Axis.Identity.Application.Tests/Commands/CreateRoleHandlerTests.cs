using Axis.Identity.Application.Commands.CreateRole;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Identity.Application.Tests.Commands;

public class CreateRoleHandlerTests
{
    private readonly IRoleRepository _roleRepo = Substitute.For<IRoleRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private static readonly Guid WorkspaceId = Guid.NewGuid();

    private CreateRoleHandler CreateHandler() => new(_roleRepo, _uow);

    private static CreateRoleCommand ValidCommand() => new(
        WorkspaceId,
        Name: "Manager",
        Description: "Can manage workflows",
        Permissions: ["workflow:definition:read", "workflow:definition:write"]);

    [Fact]
    public async Task CreateRole_WhenNameIsUnique_CreatesAndPersistsRole()
    {
        _roleRepo.NameExistsAsync("Manager", WorkspaceId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        await _roleRepo.Received(1).AddAsync(
            Arg.Is<Role>(r => r.Name == "Manager" && r.workspaceId == WorkspaceId),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRole_WhenNameIsDuplicate_ReturnsConflict()
    {
        _roleRepo.NameExistsAsync("Manager", WorkspaceId).Returns(true);

        Result<Guid> result = await CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        result.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task CreateRole_WhenPermissionsAreEmpty_ReturnsBusinessRuleFailure()
    {
        CreateRoleCommand command = ValidCommand() with { Permissions = [] };
        _roleRepo.NameExistsAsync(Arg.Any<string>(), WorkspaceId).Returns(false);

        Result<Guid> result = await CreateHandler().Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.Error.Should().Contain("at least one permission");
    }
}
