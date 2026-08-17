using Axis.Identity.Application;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Identity.Infrastructure.Tests.Repositories;

[Collection("IdentityDb")]
public sealed class WorkspaceInvitationRepositoryTests(IdentityDatabaseFixture db) : IAsyncLifetime
{
    private IdentityDbContext _context = null!;
    private WorkspaceInvitationRepository _repository = null!;

    public ValueTask InitializeAsync()
    {
        _context = db.CreateContext();
        _repository = new WorkspaceInvitationRepository(_context);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        WorkspaceInvitation[] invitations = _context.ChangeTracker
            .Entries<WorkspaceInvitation>()
            .Select(entry => entry.Entity)
            .ToArray();
        _context.WorkspaceInvitations.RemoveRange(invitations);
        await _context.SaveChangesAsync(CancellationToken.None);
        await _context.DisposeAsync();
    }

    [Theory]
    [InlineData(WorkspaceInvitationSortField.Email, false)]
    [InlineData(WorkspaceInvitationSortField.Status, false)]
    [InlineData(WorkspaceInvitationSortField.Role, true)]
    [InlineData(WorkspaceInvitationSortField.Created, true)]
    [InlineData(WorkspaceInvitationSortField.Expires, false)]
    public async Task ListForWorkspaceAsync_WhenSortIsExplicit_OrdersWholeDatasetByRequestedField(
        WorkspaceInvitationSortField sortBy,
        bool firstInvitationFirst)
    {
        (Guid organizationId, Guid workspaceId, Guid inviterId) = await SeedWorkspaceAsync();
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation first = CreateInvitation(
            Guid.NewGuid(),
            organizationId,
            workspaceId,
            inviterId,
            "zulu@example.com",
            WorkspaceMembershipRole.Administrator,
            now.AddDays(-2),
            now.AddDays(5));
        first.Revoke(first.Revision, now);
        first.RecordModification(ActorSnapshot.User(inviterId, "Invitation Sort Admin"), now);
        WorkspaceInvitation second = CreateInvitation(
            Guid.NewGuid(),
            organizationId,
            workspaceId,
            inviterId,
            "alpha@example.com",
            WorkspaceMembershipRole.Member,
            now.AddDays(-1),
            now.AddDays(1));
        _context.WorkspaceInvitations.AddRange(first, second);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<WorkspaceInvitation> rows = await _repository.ListForWorkspaceAsync(
            workspaceId,
            0,
            20,
            sortBy,
            CollectionSortDirection.Ascending,
            TestContext.Current.CancellationToken);

        rows.Select(invitation => invitation.Id).Should().Equal(
            firstInvitationFirst ? [first.Id, second.Id] : [second.Id, first.Id]);
    }

    [Fact]
    public async Task ListForWorkspaceAsync_WhenDeliverySortIsExplicit_UsesCurrentTokenBeforePaging()
    {
        (Guid organizationId, Guid workspaceId, Guid inviterId) = await SeedWorkspaceAsync();
        DateTime now = DateTime.UtcNow;
        WorkspaceInvitation delivered = CreateInvitation(
            Guid.NewGuid(),
            organizationId,
            workspaceId,
            inviterId,
            "delivered@example.com",
            WorkspaceMembershipRole.Member,
            now.AddDays(-2),
            now.AddDays(5));
        delivered.MarkDelivered(delivered.Revision);
        delivered.RecordModification(ActorSnapshot.System(), now);
        WorkspaceInvitation pending = CreateInvitation(
            Guid.NewGuid(),
            organizationId,
            workspaceId,
            inviterId,
            "pending@example.com",
            WorkspaceMembershipRole.Member,
            now.AddDays(-1),
            now.AddDays(5));
        _context.WorkspaceInvitations.AddRange(delivered, pending);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<WorkspaceInvitation> rows = await _repository.ListForWorkspaceAsync(
            workspaceId,
            0,
            1,
            WorkspaceInvitationSortField.Delivery,
            CollectionSortDirection.Ascending,
            TestContext.Current.CancellationToken);

        rows.Should().ContainSingle().Which.Id.Should().Be(delivered.Id);
    }

    [Fact]
    public async Task ListForWorkspaceAsync_WhenCreatedTies_UsesIdAsDeterministicDefaultTieBreaker()
    {
        (Guid organizationId, Guid workspaceId, Guid inviterId) = await SeedWorkspaceAsync();
        DateTime now = DateTime.UtcNow;
        Guid lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        WorkspaceInvitation higher = CreateInvitation(
            higherId,
            organizationId,
            workspaceId,
            inviterId,
            "higher@example.com",
            WorkspaceMembershipRole.Member,
            now,
            now.AddDays(7));
        WorkspaceInvitation lower = CreateInvitation(
            lowerId,
            organizationId,
            workspaceId,
            inviterId,
            "lower@example.com",
            WorkspaceMembershipRole.Member,
            now,
            now.AddDays(7));
        _context.WorkspaceInvitations.AddRange(higher, lower);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<WorkspaceInvitation> rows = await _repository.ListForWorkspaceAsync(
            workspaceId,
            0,
            20,
            ct: TestContext.Current.CancellationToken);

        rows.Select(invitation => invitation.Id).Should().Equal(lowerId, higherId);
    }

    private async Task<(Guid OrganizationId, Guid WorkspaceId, Guid InviterId)> SeedWorkspaceAsync()
    {
        User inviter = User.Create(
            "Invitation Sort Admin",
            Email.Create($"invitation-sort-{Guid.NewGuid():N}@example.com").Value);
        Organization organization = Organization.Create("Invitation Sort Organization");
        Workspace workspace = Workspace.CreateOrganization(
            "Invitation Sort Workspace",
            WorkspaceSlug.Create($"invitation-sort-{Guid.NewGuid():N}").Value,
            organization.Id);
        _context.Users.Add(inviter);
        _context.Organizations.Add(organization);
        _context.Workspaces.Add(workspace);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (organization.Id, workspace.Id, inviter.Id);
    }

    private static WorkspaceInvitation CreateInvitation(
        Guid id,
        Guid organizationId,
        Guid workspaceId,
        Guid inviterId,
        string email,
        WorkspaceMembershipRole role,
        DateTime createdAt,
        DateTime expiresAt)
    {
        string uniqueTokenHash = $"{Guid.NewGuid():N}{Guid.NewGuid():N}";
        WorkspaceInvitation invitation = WorkspaceInvitation.Create(
            id,
            organizationId,
            workspaceId,
            inviterId,
            email,
            role,
            createdAt,
            expiresAt,
            uniqueTokenHash,
            $"protected-{Guid.NewGuid():N}",
            $"sort-{Guid.NewGuid():N}");
        invitation.InitializeMetadata(ActorSnapshot.User(inviterId, "Invitation Sort Admin"));
        return invitation;
    }
}
