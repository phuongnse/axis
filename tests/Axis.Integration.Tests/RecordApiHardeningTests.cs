using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordApiHardeningTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    // Drops the Item table if the text ever reached SQL as an identifier. It has no '/', so routing keeps it in one segment.
    private const string Injection = "\";DROP TABLE \"entities\".\"e_5e1d7a208c4b4f3e9b2a3d6c1e0f4a03\";--";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public static TheoryData<string> Attempts => new() { "entity", "values", "sort" };

    [Theory]
    [MemberData(nameof(Attempts))]
    public async Task Injection_attempt_is_a_404_or_400_problem_and_leaves_the_table_unchanged(string attempt)
    {
        await fixture.ResetAsync();
        using (var create = Request(HttpMethod.Post, Items, HostA, """{ "values": { "name": "Desk" } }"""))
        using (var created = await fixture.Client.SendAsync(create, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var before = await fixture.TableShapeAsync(TenantA, "Item");
        var injectedName = JsonEncodedText.Encode("name" + Injection).ToString();
        using var request = attempt switch
        {
            "entity" => Request($"/api/apps/RecordsApp/entities/{Uri.EscapeDataString("Item" + Injection)}/records", HostA),
            "values" => Request(HttpMethod.Post, Items, HostA, $$"""{ "values": { "name": "Desk", "{{injectedName}}": "x" } }"""),
            _ => Request($"{Items}?sort={Uri.EscapeDataString("name" + Injection)}", HostA),
        };

        using var response = await fixture.Client.SendAsync(request, CancellationToken);

        switch (attempt)
        {
            case "entity":
                using (var problem = await ReadProblemAsync(response, HttpStatusCode.NotFound))
                {
                    Assert.Equal("The application has no entity with this name.", problem.RootElement.GetProperty("title").GetString());
                }

                break;
            case "values":
                using (var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest))
                {
                    var key = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject()).Name;
                    Assert.StartsWith("/values/name\"", key, StringComparison.Ordinal);
                }

                break;
            default:
                using (var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest))
                {
                    Assert.Equal("sort", Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject()).Name);
                }

                break;
        }

        var after = await fixture.TableShapeAsync(TenantA, "Item");
        Assert.Equal(before.Columns, after.Columns);
        Assert.Equal(1, after.RowCount);
        Assert.Equal(before.RowCount, after.RowCount);
    }

    [Fact]
    public async Task Unexpected_storage_failure_is_a_500_problem_without_the_table_name_or_exception_text()
    {
        await fixture.ResetAsync();
        Assert.True(fixture.Model.TryGetEntity("Item", out var item));
        var table = EntityNaming.Table(item.Id);

        // The fixture is shared by the class, so the table is renamed back whatever the outcome.
        await fixture.ExecuteAsync(TenantA, $"ALTER TABLE {EntityNaming.QualifiedTable(table)} RENAME TO {EntityNaming.Quote("x_" + table)}");
        try
        {
            using var request = Request(Items, HostA);
            using var response = await fixture.Client.SendAsync(request, CancellationToken);

            using var problem = await ReadProblemAsync(response, HttpStatusCode.InternalServerError);
            Assert.DoesNotContain(table, problem.RootElement.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            await fixture.ExecuteAsync(TenantA, $"ALTER TABLE {EntityNaming.QualifiedTable("x_" + table)} RENAME TO {EntityNaming.Quote(table)}");
        }
    }
}
