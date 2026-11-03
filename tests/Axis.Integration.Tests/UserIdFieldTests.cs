using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

public sealed class UserIdFieldTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Manager = "/api/apps/StepApp/entities/Department/fields/manager/users";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_user_who_may_write_the_field_finds_the_profiles_of_the_tenant_by_name_or_email_and_resolves_one_id()
    {
        await fixture.InsertProfileAsync(TenantA, "quill-ada", "Ada Quillfeather", email: "ada@mail.example");
        await fixture.InsertProfileAsync(TenantA, "quill-bo", "Bo Quillfeather", email: "bo@quillmail.example");
        await fixture.InsertProfileAsync(TenantA, "quill-cy", "Cy Plain", email: "cy@quillfeather-mail.example");
        await fixture.InsertProfileAsync(TenantB, "quill-dee", "Dee Quillfeather", email: "dee@quillfeather.example");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var byName = await GetAsync(maria, TenantA, Manager + "?search=QUILLFEATHER");
        using var byEmail = await GetAsync(maria, TenantA, Manager + "?search=@quillmail");
        using var byId = await GetAsync(maria, TenantA, Manager + "?id=quill-bo&search=nobody");
        using var unknownId = await GetAsync(maria, TenantA, Manager + "?id=quill-dee");

        using var named = await ReadAsync(byName);
        Assert.Equal(
            [("quill-ada", "Ada Quillfeather"), ("quill-bo", "Bo Quillfeather"), ("quill-cy", "Cy Plain")],
            Items(named));
        using var mailed = await ReadAsync(byEmail);
        Assert.Equal([("quill-bo", "Bo Quillfeather")], Items(mailed));
        using var resolved = await ReadAsync(byId);
        Assert.Equal([("quill-bo", "Bo Quillfeather")], Items(resolved));
        using var missing = await ReadAsync(unknownId);
        Assert.Empty(Items(missing));
        Assert.DoesNotContain("email", named.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Someone_who_may_not_write_the_field_is_refused_and_so_is_an_anonymous_caller()
    {
        foreach (var user in new[] { "carl", "otto" })
        {
            using var client = fixture.CreateClient().SignedInAs(user);
            using var denied = await GetAsync(client, TenantA, Manager + "?search=quill");
            using var problem = await RecordApiFixture.ReadProblemAsync(denied, HttpStatusCode.Forbidden);
            Assert.Equal("You have no grant for this action.", problem.RootElement.GetProperty("title").GetString());
        }

        using var anonymous = await GetAsync(fixture.CreateClient(), TenantA, Manager + "?search=quill");
        using var anonymousProblem = await RecordApiFixture.ReadProblemAsync(anonymous, HttpStatusCode.Unauthorized);
        Assert.Equal("Sign in to read or write this data.", anonymousProblem.RootElement.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("/api/apps/StepApp/entities/Department/fields/name/users")]
    [InlineData("/api/apps/StepApp/entities/Department/fields/missing/users")]
    [InlineData("/api/apps/StepApp/entities/Nothing/fields/manager/users")]
    public async Task A_field_that_is_not_a_user_id_field_and_an_unknown_entity_are_not_found(string path)
    {
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var response = await GetAsync(maria, TenantA, path);

        await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
    }

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string tenant, string path)
    {
        using var request = RecordApiFixture.Request(path, Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static (string Id, string DisplayName)[] Items(JsonDocument document) =>
        [.. document.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (item.GetProperty("id").GetString()!, item.GetProperty("displayName").GetString()!))];
}
