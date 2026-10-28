using System.Net;
using System.Text.Json;
using Axis.Data.Naming;
using Axis.Data.Records;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The money fields of the RecordsApp fixture: an expense's <c>amountVnd</c> has the fixed currency
/// <c>VND</c> and its <c>amount</c> takes the currency from its <c>currency</c> field.
/// </summary>
public sealed class RecordMoneyEndpointTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Expenses = "/api/apps/RecordsApp/entities/Expense/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Money_columns_have_the_precision_and_scale_of_their_currency()
    {
        Assert.True(fixture.Model.TryGetEntity("Expense", out var expense));
        var table = EntityNaming.QualifiedTable(EntityNaming.Table(expense.Id));

        Assert.Equal("numeric(18,0)", await ColumnTypeAsync(table, "amountVnd"));
        Assert.Equal("numeric(18,2)", await ColumnTypeAsync(table, "amount"));
    }

    [Fact]
    public async Task Money_is_written_and_read_as_a_decimal_with_its_scale()
    {
        await fixture.ResetAsync();
        using var requester = fixture.CreateClient().SignedInAs("requester");

        using var created = await SendAsync(
            requester, HttpMethod.Post, Expenses, """{ "values": { "title": "Taxi", "amountVnd": 1250000, "currency": "USD", "amount": 12.5 } }""");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).RootElement.GetProperty("id").GetString();

        using var read = await SendAsync(requester, HttpMethod.Get, $"{Expenses}/{id}", null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var record = await ReadJsonAsync(read);
        var values = record.RootElement.GetProperty("values");
        Assert.Equal("1250000", values.GetProperty("amountVnd").GetRawText());
        Assert.Equal("12.50", values.GetProperty("amount").GetRawText());
    }

    [Theory]
    [InlineData("""{ "title": "Taxi", "amountVnd": 1.5 }""", "amountVnd", 0)]
    [InlineData("""{ "title": "Taxi", "currency": "USD", "amount": 1.234 }""", "amount", 2)]
    public async Task Money_with_too_many_fraction_digits_is_400_at_the_field_and_writes_nothing(string values, string field, long digits)
    {
        await fixture.ResetAsync();
        using var requester = fixture.CreateClient().SignedInAs("requester");

        using var response = await SendAsync(requester, HttpMethod.Post, Expenses, $$"""{ "values": {{values}} }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.BadRequest);
        var error = Assert.Single(problem.RootElement.GetProperty("errors").EnumerateObject());
        Assert.Equal($"/values/{field}", error.Name);
        Assert.Equal(RecordInputMessages.FractionDigits(digits), Assert.Single(error.Value.EnumerateArray()).GetString());
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Expense")).RowCount);
    }

    [Fact]
    public async Task User_without_a_grant_cannot_create_an_expense()
    {
        await fixture.ResetAsync();
        using var binh = fixture.CreateClient().SignedInAs("binh");

        using var response = await SendAsync(binh, HttpMethod.Post, Expenses, """{ "values": { "title": "Taxi", "amountVnd": 1 } }""");

        using var problem = await ReadProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
        Assert.Equal(0, (await fixture.TableShapeAsync(TenantA, "Expense")).RowCount);
    }

    private async Task<string?> ColumnTypeAsync(string table, string field) =>
        (string?)await fixture.ScalarAsync(
            TenantA,
            $"SELECT format_type(atttypid, atttypmod) FROM pg_attribute WHERE attrelid = '{table}'::regclass AND attname = '{EntityNaming.Column(field)}'");

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, string? body)
    {
        using var request = Request(method, path, HostA, body);
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
}
