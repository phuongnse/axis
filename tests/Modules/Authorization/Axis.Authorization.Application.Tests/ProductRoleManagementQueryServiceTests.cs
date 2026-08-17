using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.Authorization.Application.Tests;

public sealed class ProductRoleManagementQueryServiceTests
{
    [Fact]
    public async Task Get_WhenAdministrator_ReturnsExactLanguageAndCurrentAssignments()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid actorId = Guid.NewGuid();
        Guid policyVersionId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        IAuthorizationAdministratorAuthority administrators = Substitute.For<IAuthorizationAdministratorAuthority>();
        administrators.IsAdministratorAsync(
                workspaceId,
                SubjectReference.Human(actorId),
                Arg.Any<CancellationToken>())
            .Returns(true);
        IInstalledProductPolicyStore policies = Substitute.For<IInstalledProductPolicyStore>();
        policies.ListAsync(workspaceId, Arg.Any<CancellationToken>())
            .Returns([
                new StoredProductPolicy(
                    workspaceId,
                    new ProductPolicyComponent(
                        "reference",
                        policyVersionId,
                        [
                            new ProductPolicyRole(
                                "Caseworker",
                                new Dictionary<string, ProductRolePresentation>
                                {
                                    ["en"] = new("Caseworker", "Reviews cases"),
                                    ["vi-VN"] = new("Chuyên viên", "Xử lý hồ sơ"),
                                }),
                            new ProductPolicyRole(
                                "Applicant",
                                new Dictionary<string, ProductRolePresentation>
                                {
                                    ["en"] = new("Applicant", null),
                                }),
                        ],
                        []),
                    "{}",
                    "{}",
                    DateTimeOffset.UtcNow),
            ]);
        IProductRoleAssignmentStore assignments = Substitute.For<IProductRoleAssignmentStore>();
        assignments.ListAsync(workspaceId, Arg.Any<CancellationToken>())
            .Returns([
                new StoredProductRoleAssignment(
                    Guid.NewGuid(),
                    workspaceId,
                    SubjectReference.Service(targetId),
                    policyVersionId,
                    "Caseworker",
                    true,
                    3,
                    DateTimeOffset.UtcNow,
                    null,
                    DateTimeOffset.UtcNow,
                    ActorSnapshot.User(actorId, "Administrator"),
                    ActorSnapshot.User(actorId, "Administrator")),
            ]);

        ProductRoleManagementResult result = await new ProductRoleManagementQueryService(
            administrators,
            policies,
            assignments).GetAsync(
                workspaceId,
                SubjectReference.Human(actorId),
                "vi-VN",
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Roles.Should().Contain(value =>
            value.RoleKey == "Caseworker" && value.DisplayName == "Chuyên viên");
        result.Roles.Should().Contain(value =>
            value.RoleKey == "Applicant" && value.DisplayName == "Applicant");
        ProductRoleAssignmentDto assignment = result.Assignments.Should().ContainSingle().Subject;
        assignment.Subject.Kind.Should().Be(SubjectKind.Service);
        assignment.Subject.SubjectId.Should().Be(targetId);
        assignment.Revision.Should().Be(3);
    }

    [Fact]
    public async Task Get_WhenAdministratorMissing_DeniesWithoutReadingPolicyState()
    {
        IAuthorizationAdministratorAuthority administrators = Substitute.For<IAuthorizationAdministratorAuthority>();
        IInstalledProductPolicyStore policies = Substitute.For<IInstalledProductPolicyStore>();
        IProductRoleAssignmentStore assignments = Substitute.For<IProductRoleAssignmentStore>();

        ProductRoleManagementResult result = await new ProductRoleManagementQueryService(
            administrators,
            policies,
            assignments).GetAsync(
                Guid.NewGuid(),
                SubjectReference.Human(Guid.NewGuid()),
                "en",
                TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("authority_denied");
        await policies.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await assignments.DidNotReceive().ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
