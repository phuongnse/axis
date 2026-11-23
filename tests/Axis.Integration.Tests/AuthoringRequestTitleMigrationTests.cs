using System.Text.Json.Nodes;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// Upgrading a tenant database stores a request's plan as an object, see
/// docs/reference/authoring-request-storage.md: a plan stored as text becomes the plan's shape with that text as its summary
/// and every group empty, and a request with no plan stays without one.
/// </summary>
public sealed class AuthoringRequestTitleMigrationTests
{
    private const string BeforeTitle = "20261121080135_AddPublishRequestRun";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrating_a_database_with_a_text_plan_stores_it_as_the_summary_of_a_plan_with_empty_groups()
    {
        var connectionString = await PostgreSqlServer.CreateDatabaseAsync("authoring_request_title", CancellationToken);
        await using var context = new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseNpgsql(connectionString).Options);
        await context.GetService<IMigrator>().MigrateAsync(BeforeTitle, CancellationToken);
        var planned = Guid.CreateVersion7();
        var planning = Guid.CreateVersion7();
        var application = Guid.NewGuid();

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO axis.authoring_requests (id, application_id, author, text, state, plan)
            VALUES ({planned}, {application}, 'author', 'Add a cost centre.', 'awaitingApproval', 'Add a cost centre entity.'),
                   ({planning}, {application}, 'author', 'Add a supplier.', 'planning', NULL)
            """,
            CancellationToken);

        await context.Database.MigrateAsync(CancellationToken);

        var expected = JsonNode.Parse(
            """
            {
              "summary": "Add a cost centre entity.",
              "questions": [], "changes": [], "decisions": [], "risks": [], "checks": [], "checklist": [], "details": []
            }
            """);
        var (plan, title) = await PlanAsync(connectionString, planned);
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(plan!)), plan);
        Assert.Null(title);
        Assert.Equal((null, null), await PlanAsync(connectionString, planning));
    }

    private static async Task<(string? Plan, string? Title)> PlanAsync(string connectionString, Guid id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken);
        await using var command = new NpgsqlCommand("SELECT plan::text, title FROM axis.authoring_requests WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        Assert.True(await reader.ReadAsync(CancellationToken));
        return (
            await reader.IsDBNullAsync(0, CancellationToken) ? null : reader.GetString(0),
            await reader.IsDBNullAsync(1, CancellationToken) ? null : reader.GetString(1));
    }
}
