using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The record API refuses a <c>sort</c> on a sensitive field, because its stored value is encrypted, and still sorts on
/// any other field. It runs on the FilesApp fixture in tenant A, where the <c>note</c> field of a claim is <c>sensitive</c>.
/// </summary>
public sealed class RecordSensitiveSortTests(RecordApiFixture fixture) : IClassFixture<RecordApiFixture>
{
    private const string Claims = "/api/apps/FilesApp/entities/Claim/records";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("note")]
    [InlineData("-note")]
    public async Task A_sort_on_a_sensitive_field_is_a_400_keyed_sort(string sort)
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");

        using var response = await requester.SendAsync(Request(HttpMethod.Get, $"{Claims}?sort={sort}", HostA), CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("sort", out _));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("-title")]
    public async Task A_sort_on_another_field_is_a_200(string sort)
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");

        using var response = await requester.SendAsync(Request(HttpMethod.Get, $"{Claims}?sort={sort}", HostA), CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
