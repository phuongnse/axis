using System.Text.Json;
using Axis.Rules.Domain;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Rules.Infrastructure.Tests.Fixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Rules.Infrastructure.Tests.Persistence;

[Collection("RulesDb")]
public sealed class RuleSubjectMigrationTests(RulesDatabaseFixture db)
{
    [Fact]
    public async Task AddRuleSubjects_WhenLegacyActorsExist_PreservesIdsAsHuman()
    {
        string databaseName = $"axis_rules_subject_migration_{Guid.NewGuid():N}";
        string connectionString = await db.CreateDatabaseAsync(databaseName);
        DbContextOptions<RulesDbContext> options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using RulesDbContext context = new(options);
        await context.Database.MigrateAsync(
            "20260805092650_InitialRules",
            TestContext.Current.CancellationToken);

        Guid workspaceId = Guid.NewGuid();
        Guid definitionId = Guid.NewGuid();
        Guid versionId = Guid.NewGuid();
        Guid bindingId = Guid.NewGuid();
        Guid createdBy = Guid.NewGuid();
        Guid updatedBy = Guid.NewGuid();
        Guid archivedBy = Guid.NewGuid();
        Guid publishedBy = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        string revisionHistory = JsonSerializer.Serialize(new[]
        {
            new
            {
                revision = 1,
                definitionKey = "legacy.rule",
                definitionVersion = 1,
                targetType = "invoice-field",
                targetId = "field-1",
                useCaseOrTrigger = "record.validate",
                inputMappings = new Dictionary<string, object>(),
                priority = 0,
                enabled = true,
                failureBehavior = 0,
                updatedByUserId = updatedBy,
                updatedAt = now,
            },
        });

        await using (NpgsqlConnection connection = new(connectionString))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await InsertLegacyRowsAsync(
                connection,
                workspaceId,
                definitionId,
                versionId,
                bindingId,
                createdBy,
                updatedBy,
                archivedBy,
                publishedBy,
                now,
                revisionHistory);
        }

        await context.Database.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);

        await using NpgsqlConnection verification = new(connectionString);
        await verification.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = verification.CreateCommand();
        command.CommandText =
            """
            SELECT d.created_by_subject_kind,
                   d.created_by_subject_id,
                   d.updated_by_subject_kind,
                   d.updated_by_subject_id,
                   d.archived_by_subject_kind,
                   d.archived_by_subject_id,
                   v.published_by_subject_kind,
                   v.published_by_subject_id,
                   b.created_by_subject_kind,
                   b.created_by_subject_id,
                   b.updated_by_subject_kind,
                   b.updated_by_subject_id,
                   b.revision_history
            FROM rule_definitions d
            JOIN rule_definition_versions v ON v.rule_definition_id = d.id
            CROSS JOIN rule_bindings b
            WHERE d.id = @definition_id AND b.id = @binding_id
            """;
        command.Parameters.AddWithValue("definition_id", definitionId);
        command.Parameters.AddWithValue("binding_id", bindingId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();

        reader.GetString(0).Should().Be("Human");
        reader.GetGuid(1).Should().Be(createdBy);
        reader.GetString(2).Should().Be("Human");
        reader.GetGuid(3).Should().Be(updatedBy);
        reader.GetString(4).Should().Be("Human");
        reader.GetGuid(5).Should().Be(archivedBy);
        reader.GetString(6).Should().Be("Human");
        reader.GetGuid(7).Should().Be(publishedBy);
        reader.GetString(8).Should().Be("Human");
        reader.GetGuid(9).Should().Be(createdBy);
        reader.GetString(10).Should().Be("Human");
        reader.GetGuid(11).Should().Be(updatedBy);
        using JsonDocument revisions = JsonDocument.Parse(reader.GetString(12));
        JsonElement revision = revisions.RootElement.EnumerateArray().Single();
        revision.TryGetProperty("updatedByUserId", out _).Should().BeFalse();
        revision.GetProperty("updatedBySubjectKind").GetInt32().Should().Be((int)RuleSubjectKind.Human);
        revision.GetProperty("updatedBySubjectId").GetGuid().Should().Be(updatedBy);
    }

    private static async Task InsertLegacyRowsAsync(
        NpgsqlConnection connection,
        Guid workspaceId,
        Guid definitionId,
        Guid versionId,
        Guid bindingId,
        Guid createdBy,
        Guid updatedBy,
        Guid archivedBy,
        Guid publishedBy,
        DateTime now,
        string revisionHistory)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO rule_definitions
                (id, workspace_id, definition_key, name, description, origin,
                 expression_language_version, revision, latest_published_version, active_version,
                 condition, output, created_by_user_id, updated_by_user_id, created_at, updated_at,
                 archived_by_user_id, archived_at, inputs)
            VALUES
                (@definition_id, @workspace_id, 'legacy.rule', 'Legacy rule', 'Legacy description', 'Workspace',
                 1, 3, 1, NULL, @condition, @output, @created_by, @updated_by, @now, @now,
                 @archived_by, @now, @inputs);

            INSERT INTO rule_definition_versions
                (id, rule_definition_id, version_number, name, description, expression_language_version,
                 condition, output, published_by_user_id, published_at, inputs)
            VALUES
                (@version_id, @definition_id, 1, 'Legacy rule', 'Legacy description', 1,
                 @condition, @output, @published_by, @now, @inputs);

            INSERT INTO rule_bindings
                (id, workspace_id, definition_key, definition_version, target_type, target_id,
                 use_case_or_trigger, priority, enabled, failure_behavior, revision,
                 created_by_user_id, updated_by_user_id, created_at, updated_at,
                 input_mappings, revision_history)
            VALUES
                (@binding_id, @workspace_id, 'legacy.rule', 1, 'invoice-field', 'field-1',
                 'record.validate', 0, TRUE, 'FailClosed', 1,
                 @created_by, @updated_by, @now, @now, @mappings, @revision_history);
            """;
        command.Parameters.AddWithValue("definition_id", definitionId);
        command.Parameters.AddWithValue("workspace_id", workspaceId);
        command.Parameters.AddWithValue("version_id", versionId);
        command.Parameters.AddWithValue("binding_id", bindingId);
        command.Parameters.AddWithValue("created_by", createdBy);
        command.Parameters.AddWithValue("updated_by", updatedBy);
        command.Parameters.AddWithValue("archived_by", archivedBy);
        command.Parameters.AddWithValue("published_by", publishedBy);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.Add(new NpgsqlParameter("condition", NpgsqlDbType.Jsonb) { Value = "{}" });
        command.Parameters.Add(new NpgsqlParameter("output", NpgsqlDbType.Jsonb) { Value = "{}" });
        command.Parameters.Add(new NpgsqlParameter("inputs", NpgsqlDbType.Jsonb) { Value = "[]" });
        command.Parameters.Add(new NpgsqlParameter("mappings", NpgsqlDbType.Jsonb) { Value = "{}" });
        command.Parameters.Add(new NpgsqlParameter("revision_history", NpgsqlDbType.Jsonb) { Value = revisionHistory });
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
