using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

public sealed class RecordApiOutputEncodingTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Items = "/api/apps/RecordsApp/entities/Item/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Text_that_ends_a_script_tag_and_holds_a_line_separator_is_read_back_unchanged_from_a_body_that_parses_as_JSON()
    {
        await fixture.ResetAsync();
        const string hostile = "</script><script>alert(1)</script>\u2028Desk";
        var body = JsonSerializer.Serialize(new { values = new { name = hostile } });
        using var create = Request(HttpMethod.Post, Items, HostA, body);
        using var created = await fixture.Client.SendAsync(create, CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdRecord = JsonDocument.Parse(await created.Content.ReadAsStringAsync(CancellationToken));
        var id = createdRecord.RootElement.GetProperty("id").GetGuid();

        using var get = Request($"{Items}/{id:D}", HostA);
        using var read = await fixture.Client.SendAsync(get, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var raw = await read.Content.ReadAsStringAsync(CancellationToken);

        using var record = JsonDocument.Parse(raw);
        Assert.Equal(hostile, record.RootElement.GetProperty("values").GetProperty("name").GetString());
        // The server writes the angle brackets as they are and escapes the line separator, so only the separator is asserted on the raw body.
        Assert.DoesNotContain('\u2028', raw);
        Assert.Contains("\\u2028", raw, StringComparison.Ordinal);
    }
}
