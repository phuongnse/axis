using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Testing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.Identity.Infrastructure.Tests.Migrations;

public sealed class IdentityGovernanceMigrationTests : IAsyncLifetime
{
    private const string InitialIdentityMigration = "20260804091803_InitialIdentity";
    private const string GovernanceMigration = "20260806070458_AddIdentityGovernance";
    private const string PlatformAuditMigration = "20260806150336_AllowPlatformScopedAuditEvents";
    private const string SinglePendingInvitationMigration =
        "20260806154703_EnforceSinglePendingWorkspaceInvitationRecipient";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact]
    public void Model_WhenComparedWithMigrationSnapshot_HasNoPendingChanges()
    {
        using IdentityDbContext context = CreateContext(_postgres.GetConnectionString());

        context.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task MigrateAsync_WhenLegacyPersonalOwnerIsValid_BackfillsOneActiveOwnerAndRetiresLegacyOwnership()
    {
        string connectionString = await CreateLegacyDatabaseAsync();
        Guid ownerId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();

        await SeedLegacyWorkspaceAsync(connectionString, workspaceId, ownerId, ownerExists: true);
        await MigrateToLatestAsync(connectionString);

        (Guid UserId, string Role, string Status, int Revision)[] memberships =
            await ReadWorkspaceMembershipsAsync(connectionString, workspaceId);

        memberships.Should().ContainSingle();
        memberships[0].UserId.Should().Be(ownerId);
        memberships[0].Role.Should().Be("Owner");
        memberships[0].Status.Should().Be("Active");
        memberships[0].Revision.Should().Be(1);
        (await ScalarAsync(connectionString,
            "SELECT organization_id FROM workspaces WHERE id = @workspaceId",
            ("workspaceId", workspaceId))).Should().BeNull();
        (await TableExistsAsync(connectionString, "\"Workspaces\"")).Should().BeFalse();
        (await TableExistsAsync(connectionString, "workspaces")).Should().BeTrue();
        (await ColumnExistsAsync(connectionString, "owner_user_id")).Should().BeFalse();
        (await ColumnExistsAsync(connectionString, "owner_email")).Should().BeFalse();
        (await IndexExistsAsync(connectionString, "\"IX_Workspaces_owner_user_id_type\"")).Should().BeFalse();
        (await ConstraintExistsAsync(
            connectionString,
            "workspaces",
            "CK_workspaces_type_organization")).Should().BeTrue();
        (await ConstraintExistsAsync(
            connectionString,
            "workspaces",
            "FK_workspaces_organizations_organization_id")).Should().BeTrue();
        (await IndexExistsAsync(
            connectionString,
            "\"IX_workspace_memberships_workspace_id_user_id\"")).Should().BeTrue();
        (await IndexExistsAsync(
            connectionString,
            "\"IX_organization_memberships_organization_id_user_id\"")).Should().BeTrue();
        (await ScalarAsync(
            connectionString,
            "SELECT revision FROM workspaces WHERE id = @workspaceId",
            ("workspaceId", workspaceId))).Should().Be(1);
        (await ColumnExistsAsync(
            connectionString,
            "source_correlation_digest",
            "workspace_context_transitions")).Should().BeTrue();
        (await ColumnExistsAsync(
            connectionString,
            "next_attempt_at",
            "identity_audit_outbox")).Should().BeTrue();
    }

    [Theory]
    [InlineData("short", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB")]
    public async Task MigrateAsync_WhenTransitionDigestIsMalformed_DatabaseRejectsIt(
        string sourceDigest,
        string targetDigest)
    {
        string connectionString = await CreateLegacyDatabaseAsync();
        Guid ownerId = Guid.NewGuid();
        Guid workspaceId = Guid.NewGuid();
        await SeedLegacyWorkspaceAsync(connectionString, workspaceId, ownerId, ownerExists: true);
        await MigrateToLatestAsync(connectionString);

        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        Func<Task> act = () => ExecuteAsync(
            connection,
            """
            INSERT INTO workspace_context_transitions (
                id, user_id, source_workspace_id, target_workspace_id,
                terminal_audit_event_id, source_correlation_digest,
                target_correlation_digest, created_at, expires_at, retain_until,
                status, revision)
            VALUES (
                @id, @userId, @workspaceId, @workspaceId,
                @terminalAuditEventId, @sourceDigest, @targetDigest,
                now(), now() + interval '5 minutes', now() + interval '1 day',
                'Pending', 1)
            """,
            ("id", Guid.NewGuid()),
            ("userId", ownerId),
            ("workspaceId", workspaceId),
            ("terminalAuditEventId", Guid.NewGuid()),
            ("sourceDigest", sourceDigest),
            ("targetDigest", targetDigest));

        await act.Should().ThrowAsync<PostgresException>();
    }

    [Fact]
    public async Task MigrateAsync_WhenDowngradeIsRequested_RejectsCleanCutover()
    {
        string connectionString = await CreateLegacyDatabaseAsync();
        Guid ownerId = Guid.NewGuid();
        await SeedLegacyWorkspaceAsync(
            connectionString,
            Guid.NewGuid(),
            ownerId,
            ownerExists: true);
        await MigrateToLatestAsync(connectionString);

        Func<Task> act = async () =>
        {
            await using IdentityDbContext context = CreateContext(connectionString);
            await context.Database.MigrateAsync(
                InitialIdentityMigration,
                TestContext.Current.CancellationToken);
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*clean cutover*");
        (await MigrationIsAppliedAsync(connectionString, GovernanceMigration)).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateAsync_WhenLegacyPreflightFails_LeavesLegacySchemaAndDataIntact()
    {
        (Guid? OwnerId, bool OwnerExists, string Type, string Name, string? OwnerEmail, string Error)[] cases =
        [
            (Guid.NewGuid(), true, "Organization", "Legacy Workspace", null, "*every legacy workspace to be Personal*"),
            (null, false, "Personal", "Legacy Workspace", null, "*every legacy workspace to have an owner user*"),
            (Guid.NewGuid(), false, "Personal", "Legacy Workspace", null, "*owner without a user*"),
            (Guid.NewGuid(), true, "Personal", "Legacy Workspace", "mismatch@example.com", "*owner email mismatch*"),
            (Guid.NewGuid(), true, "Personal", new string('x', 101), null, "*name outside the supported length*"),
        ];

        foreach ((Guid? ownerId, bool ownerExists, string type, string name, string? ownerEmail, string error) in cases)
        {
            string connectionString = await CreateLegacyDatabaseAsync();
            Guid workspaceId = Guid.NewGuid();

            await SeedLegacyWorkspaceAsync(
                connectionString,
                workspaceId,
                ownerId,
                ownerExists,
                type,
                name,
                ownerEmail);

            Func<Task> act = () => MigrateToLatestAsync(connectionString);

            await act.Should().ThrowAsync<PostgresException>().WithMessage(error);
            (await MigrationIsAppliedAsync(connectionString, InitialIdentityMigration)).Should().BeTrue();
            (await MigrationIsAppliedAsync(connectionString, GovernanceMigration)).Should().BeFalse();
            (await TableExistsAsync(connectionString, "\"Workspaces\"")).Should().BeTrue();
            (await TableExistsAsync(connectionString, "workspaces")).Should().BeFalse();
            (await TableExistsAsync(connectionString, "workspace_memberships")).Should().BeFalse();
            (await ColumnExistsAsync(connectionString, "owner_user_id", "Workspaces")).Should().BeTrue();
            (await ScalarAsync(
                connectionString,
                "SELECT owner_user_id FROM \"Workspaces\" WHERE id = @workspaceId",
                ("workspaceId", workspaceId))).Should().Be(ownerId);
        }
    }

    [Fact]
    public async Task MigrateAsync_WhenConflictingPendingInvitationsExist_RejectsWithoutChangingIndex()
    {
        string databaseName = $"axis_identity_invitation_migration_{Guid.NewGuid():N}";
        string connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            databaseName);
        User inviter = User.Create(
            "Invitation Administrator",
            Email.Create($"migration-admin-{Guid.NewGuid():N}@example.com").Value);
        inviter.VerifyEmail();
        Organization organization = Organization.Create("Invitation Migration Organization");
        Workspace workspace = Workspace.CreateOrganization(
            "Invitation Migration Workspace",
            WorkspaceSlug.Create($"invitation-migration-{Guid.NewGuid():N}").Value,
            organization.Id);
        DateTime now = DateTime.UtcNow;

        await using (IdentityDbContext seed = CreateContext(connectionString))
        {
            await seed.Database.MigrateAsync(
                PlatformAuditMigration,
                TestContext.Current.CancellationToken);
            seed.Users.Add(inviter);
            seed.Organizations.Add(organization);
            seed.Workspaces.Add(workspace);
            seed.OrganizationMemberships.Add(OrganizationMembership.Create(
                organization.Id,
                inviter.Id,
                OrganizationMembershipRole.Administrator));
            seed.WorkspaceMemberships.Add(WorkspaceMembership.CreateOrganizationMember(
                workspace.Id,
                inviter.Id,
                WorkspaceMembershipRole.Administrator));
            seed.WorkspaceInvitations.AddRange(
                WorkspaceInvitation.Create(
                    organization.Id,
                    workspace.Id,
                    inviter.Id,
                    "conflicting-recipient@example.com",
                    WorkspaceMembershipRole.Member,
                    now,
                    now.AddDays(7),
                    OpaqueTokenGenerator.Create().TokenHash,
                    "member-envelope",
                    "member-delivery"),
                WorkspaceInvitation.Create(
                    organization.Id,
                    workspace.Id,
                    inviter.Id,
                    "conflicting-recipient@example.com",
                    WorkspaceMembershipRole.Administrator,
                    now,
                    now.AddDays(7),
                    OpaqueTokenGenerator.Create().TokenHash,
                    "administrator-envelope",
                    "administrator-delivery"));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Func<Task> act = () => MigrateToLatestAsync(connectionString);

        await act.Should().ThrowAsync<PostgresException>()
            .WithMessage("*conflicting pending invitations exist*");
        (await MigrationIsAppliedAsync(connectionString, PlatformAuditMigration)).Should().BeTrue();
        (await MigrationIsAppliedAsync(connectionString, SinglePendingInvitationMigration)).Should().BeFalse();
        (await IndexExistsAsync(
            connectionString,
            "\"IX_workspace_invitations_workspace_id_normalized_email_request~\"")).Should().BeTrue();
        (await IndexExistsAsync(
            connectionString,
            "\"IX_workspace_invitations_workspace_id_normalized_email\"")).Should().BeFalse();
        (await ScalarAsync(
            connectionString,
            "SELECT count(*) FROM workspace_invitations WHERE workspace_id = @workspaceId",
            ("workspaceId", workspace.Id))).Should().Be(2L);
    }

    private async Task<string> CreateLegacyDatabaseAsync()
    {
        string databaseName = $"axis_identity_governance_migration_{Guid.NewGuid():N}";
        string connectionString = await PostgresModuleTestDatabase.CreateAsync(
            _postgres.GetConnectionString(),
            databaseName);
        await using IdentityDbContext context = CreateContext(connectionString);
        await context.Database.MigrateAsync(InitialIdentityMigration, TestContext.Current.CancellationToken);
        return connectionString;
    }

    private static async Task MigrateToLatestAsync(string connectionString)
    {
        await using IdentityDbContext context = CreateContext(connectionString);
        await context.Database.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
    }

    private static IdentityDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .UseOpenIddict()
            .Options);

    private static async Task SeedLegacyWorkspaceAsync(
        string connectionString,
        Guid workspaceId,
        Guid? ownerId,
        bool ownerExists,
        string type = "Personal",
        string name = "Legacy Workspace",
        string? ownerEmail = null)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        if (ownerExists)
        {
            Guid existingOwnerId = ownerId ?? throw new ArgumentException(
                "An existing owner requires an ID.",
                nameof(ownerId));
            await ExecuteAsync(connection,
                """
                INSERT INTO users (id, full_name, email, status, is_email_verified, created_at)
                VALUES (@ownerId, 'Legacy Owner', @ownerEmail, 'Active', true, now())
                """,
                ("ownerId", existingOwnerId),
                ("ownerEmail", $"legacy-{existingOwnerId:N}@example.com"));
        }

        await ExecuteAsync(connection,
            """
            INSERT INTO "Workspaces" (
                id, name, slug, owner_email, owner_user_id, type, status, created_at)
            VALUES (
                @workspaceId, @name, @slug, @ownerEmail, @ownerId, @type, 'Active', now())
            """,
            ("workspaceId", workspaceId),
            ("slug", $"legacy-{workspaceId:N}"),
            ("name", name),
            ("ownerEmail", ownerEmail ?? $"legacy-{ownerId:N}@example.com"),
            ("ownerId", ownerId.HasValue ? ownerId.Value : DBNull.Value),
            ("type", type));
    }

    private static async Task<(Guid UserId, string Role, string Status, int Revision)[]> ReadWorkspaceMembershipsAsync(
        string connectionString,
        Guid workspaceId)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(
            """
            SELECT user_id, role, status, revision
            FROM workspace_memberships
            WHERE workspace_id = @workspaceId
            """,
            connection);
        command.Parameters.AddWithValue("workspaceId", workspaceId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        List<(Guid UserId, string Role, string Status, int Revision)> memberships = [];
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            memberships.Add((
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return [.. memberships];
    }

    private static async Task<bool> MigrationIsAppliedAsync(string connectionString, string migrationId) =>
        await BooleanScalarAsync(
            connectionString,
            "SELECT EXISTS (SELECT 1 FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = @migrationId)",
            ("migrationId", migrationId));

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName) =>
        await BooleanScalarAsync(
            connectionString,
            "SELECT to_regclass(@tableName) IS NOT NULL",
            ("tableName", tableName));

    private static async Task<bool> IndexExistsAsync(string connectionString, string indexName) =>
        await BooleanScalarAsync(
            connectionString,
            "SELECT to_regclass(@indexName) IS NOT NULL",
            ("indexName", indexName));

    private static async Task<bool> ConstraintExistsAsync(
        string connectionString,
        string tableName,
        string constraintName) =>
        await BooleanScalarAsync(
            connectionString,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.table_constraints
                WHERE table_schema = 'public'
                  AND table_name = @tableName
                  AND constraint_name = @constraintName)
            """,
            ("tableName", tableName),
            ("constraintName", constraintName));

    private static async Task<bool> ColumnExistsAsync(
        string connectionString,
        string columnName,
        string tableName = "workspaces") =>
        await BooleanScalarAsync(
            connectionString,
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @tableName
                  AND column_name = @columnName)
            """,
            ("tableName", tableName),
            ("columnName", columnName));

    private static async Task<bool> BooleanScalarAsync(
        string connectionString,
        string sql,
        params (string Name, object Value)[] parameters) =>
        await ScalarAsync(connectionString, sql, parameters) is bool value && value;

    private static async Task<object?> ScalarAsync(
        string connectionString,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);

        object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return result is DBNull ? null : result;
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);

        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
