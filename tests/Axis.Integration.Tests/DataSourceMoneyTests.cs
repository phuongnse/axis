using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// A grouped data source sums a money field per currency. ExpenseTotalsByCurrency groups expenses
/// by currency, with expenses = count and amountSum = sum(amount), sorted by currency.
/// </summary>
public sealed class DataSourceMoneyTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Rows = "/api/apps/RecordsApp/data-sources/ExpenseTotalsByCurrency/rows";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Money_sum_grouped_by_its_currency_field_is_one_exact_total_per_currency_including_the_null_group()
    {
        await fixture.ResetAsync();
        foreach (var (currency, amount) in new (string? Currency, string Amount)[]
        {
            ("USD", "0.10"),
            ("USD", "0.20"),
            ("EUR", "1234567890123.45"),
            ("EUR", "0.01"),
            (null, "5.00"),
        })
        {
            await fixture.InsertAsync(TenantA, "Expense", new Dictionary<string, string?>
            {
                ["title"] = "Expense",
                ["currency"] = currency,
                ["amount"] = amount,
            });
        }

        using var request = Request(Rows, HostA);
        using var response = await fixture.Client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var rows = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));

        // Every value is the raw JSON the endpoint wrote, so no digit of a total is lost.
        Assert.Equal(
            [
                """{"currency":"EUR","expenses":2,"amountSum":1234567890123.46}""",
                """{"currency":"USD","expenses":2,"amountSum":0.30}""",
                """{"currency":null,"expenses":1,"amountSum":5.00}""",
            ],
            rows.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("values").GetRawText()));
    }

    [Fact]
    public async Task User_without_a_read_grant_on_the_data_source_gets_403()
    {
        await fixture.ResetAsync();
        using var manager = fixture.CreateClient().SignedInAs("manager");

        using var request = Request(HttpMethod.Get, Rows, HostA);
        using var response = await manager.SendAsync(request, CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
