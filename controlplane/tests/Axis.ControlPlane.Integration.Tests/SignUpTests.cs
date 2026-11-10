using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.ControlPlane.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>
/// Sign-up on a real PostgreSQL and a real Mailpit: the pending sign-up, its confirm email, the hold on its subdomain and the
/// expiry. The hosted job runner of the service sends the email, so a test waits for it with a deadline. Each test class instance
/// has a container of its own, as the subdomain check tests have, and each test an address and a subdomain of its own.
/// </summary>
public sealed partial class SignUpTests(MailpitFixture mailpit) : IAsyncLifetime
{
    private const string DirectoryDatabase = "axis_directory";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private NpgsqlDataSource _controlPlane = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE {DirectoryDatabase}", connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        _controlPlane = NpgsqlDataSource.Create(ControlPlaneConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        await _controlPlane.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task A_sign_up_answers_202_stores_only_the_hash_of_the_token_and_sends_one_email_whose_link_holds_the_token()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var address = NewAddress();
        var subdomain = NewSubdomain();

        var response = await client.PostAsJsonAsync("/api/sign-up", Form(address, subdomain, language: "vi", application: "approvals"), cancellation);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = await WaitForMessageAsync(address);
        Assert.Equal("no-reply@vn.example.com", message.From);
        Assert.Contains("Chào An Nguyen,", message.Text, StringComparison.Ordinal);
        Assert.Contains($"https://{subdomain}.vn.example.com", message.Text, StringComparison.Ordinal);
        var token = TokenPattern().Match(message.Text).Groups[1].Value;
        Assert.Matches("^[A-Za-z0-9_-]{43}$", token);

        var id = await Scalar<Guid>($"SELECT id FROM control_plane.sign_ups WHERE email = '{address}'");
        Assert.Equal("pending", await Scalar<string>($"SELECT state FROM control_plane.sign_ups WHERE id = '{id}'"));
        Assert.Equal("approvals", await Scalar<string>($"SELECT application FROM control_plane.sign_ups WHERE id = '{id}'"));
        var hash = await Scalar<byte[]>($"SELECT token_hash FROM control_plane.sign_ups WHERE id = '{id}'");
        Assert.Equal(32, hash.Length);
        Assert.Equal(SHA256.HashData(Base64Url.DecodeFromChars(token)), hash);

        // No token anywhere in the database: not in the row, and not in the payload of a job. The email has a job of its own.
        Assert.DoesNotContain(token, await Scalar<string>($"SELECT row_to_json(s)::text FROM control_plane.sign_ups s WHERE id = '{id}'"), StringComparison.Ordinal);
        Assert.Equal(0, await Scalar<long>($"SELECT count(*) FROM control_plane.jobs WHERE payload::text LIKE '%{token}%'"));
        var payload = JsonNode.Parse(await Scalar<string>($"SELECT payload::text FROM control_plane.jobs WHERE kind = 'sendSignUpConfirm'"))!.AsObject();
        Assert.Equal(["signUpId"], payload.Select(property => property.Key));
        Assert.Equal(id.ToString(), payload["signUpId"]!.GetValue<string>());
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendEmail'"));
        Assert.Single(await mailpit.MessagesToAsync(address, cancellation));
    }

    [Fact]
    public async Task A_pending_sign_up_holds_its_subdomain_and_after_24_hours_and_the_expiry_job_the_name_is_free_again()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var subdomain = NewSubdomain();
        var first = NewAddress();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", Form(first, subdomain), cancellation)).StatusCode);

        var check = await CheckAsync(client, subdomain);
        Assert.False(check.GetProperty("free").GetBoolean());
        Assert.Equal("held", check.GetProperty("reason").GetString());
        var second = NewAddress();
        var refused = await client.PostAsJsonAsync("/api/sign-up", Form(second, subdomain), cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("signUp.subdomain.held", (await ErrorsAsync(refused))["/subdomain"]);
        Assert.Equal(0, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE email = '{second}'"));

        // 25 hours later. A pending sign-up past its end holds nothing, even before the job marks it.
        await ControlPlaneDatabase.ExecuteAsync(
            _controlPlane,
            $"UPDATE control_plane.sign_ups SET created_at = created_at - interval '25 hours', expires_at = expires_at - interval '25 hours' WHERE email = '{first}'",
            cancellation);
        Assert.True((await CheckAsync(client, subdomain)).GetProperty("free").GetBoolean());
        Assert.Equal("pending", await Scalar<string>($"SELECT state FROM control_plane.sign_ups WHERE email = '{first}'"));

        var expiry = factory.Services.GetServices<IJobHandler>().Single(handler => handler.Kind == "expireSignUps");
        await expiry.RunAsync(new ClaimedJob(Guid.CreateVersion7(), "expireSignUps", [], 1), cancellation);

        Assert.Equal("expired", await Scalar<string>($"SELECT state FROM control_plane.sign_ups WHERE email = '{first}'"));
        Assert.True((await CheckAsync(client, subdomain)).GetProperty("free").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", Form(second, subdomain), cancellation)).StatusCode);
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE subdomain = '{subdomain}' AND state = 'pending'"));
    }

    [Fact]
    public async Task A_new_sign_up_takes_a_name_whose_pending_sign_up_ran_out_before_the_expiry_job()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var subdomain = NewSubdomain();
        var first = NewAddress();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", Form(first, subdomain), cancellation)).StatusCode);
        await ControlPlaneDatabase.ExecuteAsync(
            _controlPlane,
            $"UPDATE control_plane.sign_ups SET expires_at = now() - interval '1 minute' WHERE email = '{first}'",
            cancellation);

        var response = await client.PostAsJsonAsync("/api/sign-up", Form(NewAddress(), subdomain), cancellation);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("expired", await Scalar<string>($"SELECT state FROM control_plane.sign_ups WHERE email = '{first}'"));
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE subdomain = '{subdomain}' AND state = 'pending'"));
    }

    [Fact]
    public async Task Two_parallel_sign_ups_for_one_subdomain_store_one_and_refuse_the_other_under_the_subdomain()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var subdomain = NewSubdomain();

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/sign-up", Form(NewAddress(), subdomain), cancellation),
            client.PostAsJsonAsync("/api/sign-up", Form(NewAddress(), subdomain), cancellation));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        var refused = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal("signUp.subdomain.held", (await ErrorsAsync(refused))["/subdomain"]);
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE subdomain = '{subdomain}'"));
    }

    [Theory]
    [InlineData("fullName", "", "/fullName", "signUp.required")]
    [InlineData("fullName", "   ", "/fullName", "signUp.required")]
    [InlineData("businessName", "", "/businessName", "signUp.required")]
    [InlineData("email", "", "/email", "signUp.required")]
    [InlineData("subdomain", "", "/subdomain", "signUp.required")]
    [InlineData("language", "", "/language", "signUp.required")]
    [InlineData("email", "not an address", "/email", "signUp.email.invalid")]
    [InlineData("email", "An <an@example.org>", "/email", "signUp.email.invalid")]
    [InlineData("language", "fr", "/language", "signUp.language.invalid")]
    [InlineData("application", "no-such-application", "/application", "signUp.application.unknown")]
    [InlineData("subdomain", "Not_Valid", "/subdomain", "signUp.subdomain.invalid")]
    [InlineData("subdomain", "www", "/subdomain", "signUp.subdomain.reserved")]
    [InlineData("fullName", "TooLong", "/fullName", "signUp.tooLong")]
    public async Task A_form_with_a_wrong_field_is_refused_under_that_field_and_nothing_is_stored(string field, string value, string errorPath, string key)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var form = Form(NewAddress(), NewSubdomain());
        form[field] = value == "TooLong" ? new string('a', 201) : value;

        var response = await client.PostAsJsonAsync("/api/sign-up", form, cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Equal([errorPath], errors.Keys);
        Assert.Equal(key, errors[errorPath]);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task A_form_that_leaves_out_fields_or_is_not_a_JSON_object_is_refused_and_nothing_is_stored()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        // Every wrong field is named at once, a missing application included.
        var response = await client.PostAsJsonAsync("/api/sign-up", new Dictionary<string, object?> { ["email"] = 5, ["language"] = "fr" }, cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Equal(
            ["/application", "/businessName", "/email", "/fullName", "/language", "/subdomain"],
            errors.Keys);
        Assert.Equal("signUp.required", errors["/email"]);
        Assert.Equal("signUp.language.invalid", errors["/language"]);
        Assert.Equal("signUp.application.unknown", errors["/application"]);

        var notAnObject = await client.PostAsync("/api/sign-up", new StringContent("[1]", System.Text.Encoding.UTF8, "application/json"), cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, notAnObject.StatusCode);
        var notJson = await client.PostAsync("/api/sign-up", new StringContent("{", System.Text.Encoding.UTF8, "application/json"), cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, notJson.StatusCode);
        var plainText = await client.PostAsync("/api/sign-up", new StringContent("{}", System.Text.Encoding.UTF8, "text/plain"), cancellation);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, plainText.StatusCode);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task Verification_of_the_emailed_link_answers_pending_with_the_sign_up_and_changes_nothing()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var (address, subdomain, token) = await SignUpAsync(client);
        var before = await Scalar<string>($"SELECT row_to_json(s)::text FROM control_plane.sign_ups s WHERE email = '{address}'");

        var response = await client.PostAsJsonAsync("/api/sign-up/verification", new { token }, cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var answer = await response.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        Assert.Equal("pending", answer.GetProperty("state").GetString());
        Assert.Equal(address, answer.GetProperty("email").GetString());
        Assert.Equal("Contoso Trading", answer.GetProperty("businessName").GetString());
        Assert.Equal($"https://{subdomain}.vn.example.com", answer.GetProperty("address").GetString());
        Assert.Equal(before, await Scalar<string>($"SELECT row_to_json(s)::text FROM control_plane.sign_ups s WHERE email = '{address}'"));
    }

    [Fact]
    public async Task The_first_confirm_verifies_the_sign_up_and_a_second_answers_409_confirmed()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var (address, _, token) = await SignUpAsync(client);
        var id = await Scalar<Guid>($"SELECT id FROM control_plane.sign_ups WHERE email = '{address}'");

        var first = await client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(id, (await first.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("id").GetGuid());
        Assert.Equal("verified", await Scalar<string>($"SELECT state FROM control_plane.sign_ups WHERE id = '{id}'"));
        var second = await client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("confirmed", (await second.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("state").GetString());
        var verification = await client.PostAsJsonAsync("/api/sign-up/verification", new { token }, cancellation);
        Assert.Equal("confirmed", (await verification.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task The_first_confirm_starts_one_trialing_subscription_of_30_days_with_one_audit_record_and_a_second_confirm_adds_none()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var (address, subdomain, token) = await SignUpAsync(client);
        var id = await Scalar<Guid>($"SELECT id FROM control_plane.sign_ups WHERE email = '{address}'");
        Assert.Equal(0, await Scalar<long>($"SELECT count(*) FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'"));

        var before = DateTime.UtcNow;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation)).StatusCode);
        var after = DateTime.UtcNow;

        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'"));
        Assert.Equal("trialing", await Scalar<string>($"SELECT state FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'"));
        Assert.Equal(id, await Scalar<Guid>($"SELECT sign_up_id FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'"));
        var trialEndsAt = await Scalar<DateTime>($"SELECT trial_ends_at FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'");
        Assert.InRange(trialEndsAt, before.AddDays(30), after.AddDays(30));
        Assert.Equal(trialEndsAt, await Scalar<DateTime>($"SELECT next_at FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'"));
        var subscriptionId = await Scalar<Guid>($"SELECT id FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'");
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.audit_records WHERE subject = '{subscriptionId}'"));
        Assert.Equal("account", await Scalar<string>($"SELECT actor_kind FROM control_plane.audit_records WHERE subject = '{subscriptionId}'"));
        Assert.Equal(id.ToString(), await Scalar<string>($"SELECT actor FROM control_plane.audit_records WHERE subject = '{subscriptionId}'"));
        Assert.Equal("subscription.trialing", await Scalar<string>($"SELECT action FROM control_plane.audit_records WHERE subject = '{subscriptionId}'"));

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation)).StatusCode);
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.subscriptions WHERE tenant_id = '{subdomain}'"));
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.audit_records WHERE subject = '{subscriptionId}'"));
    }

    [Fact]
    public async Task Two_parallel_confirms_verify_the_sign_up_once()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var (_, _, token) = await SignUpAsync(client);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation),
            client.PostAsJsonAsync("/api/sign-up/confirm", new { token }, cancellation));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_expired_token_and_a_token_never_issued_are_refused_as_expired_by_both_routes_and_no_row_changes()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var (address, _, token) = await SignUpAsync(client);
        await ControlPlaneDatabase.ExecuteAsync(
            _controlPlane,
            $"UPDATE control_plane.sign_ups SET expires_at = now() - interval '1 minute' WHERE email = '{address}'",
            cancellation);
        var before = await Scalar<string>($"SELECT row_to_json(s)::text FROM control_plane.sign_ups s WHERE email = '{address}'");

        // A pending sign-up past its 24 hours is expired before the expiry job marks it. A wrong token, a malformed one and a missing one are unknown.
        object[] bodies =
        [
            new { token },
            new { token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)) },
            new { token = "not-a-token" },
            new { token = 5 },
            new { },
        ];
        foreach (var body in bodies)
        {
            var verification = await client.PostAsJsonAsync("/api/sign-up/verification", body, cancellation);
            Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
            Assert.Equal("expired", (await verification.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("state").GetString());
            var confirm = await client.PostAsJsonAsync("/api/sign-up/confirm", body, cancellation);
            Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
            Assert.Equal("expired", (await confirm.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("state").GetString());
        }

        Assert.Equal(before, await Scalar<string>($"SELECT row_to_json(s)::text FROM control_plane.sign_ups s WHERE email = '{address}'"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.sign_ups WHERE state = 'verified'"));
    }

    [Fact]
    public async Task The_verification_and_confirm_routes_refuse_a_body_that_is_not_a_JSON_object()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();

        foreach (var route in new[] { "/api/sign-up/verification", "/api/sign-up/confirm" })
        {
            var notAnObject = await client.PostAsync(route, new StringContent("[1]", System.Text.Encoding.UTF8, "application/json"), cancellation);
            Assert.Equal(HttpStatusCode.BadRequest, notAnObject.StatusCode);
            var plainText = await client.PostAsync(route, new StringContent("{}", System.Text.Encoding.UTF8, "text/plain"), cancellation);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, plainText.StatusCode);
        }
    }

    private string ControlPlaneConnectionString => $"{_container.GetConnectionString()};Timeout=5;Command Timeout=5";

    private string DirectoryConnectionString =>
        new NpgsqlConnectionStringBuilder(ControlPlaneConnectionString) { Database = DirectoryDatabase, Pooling = false }.ConnectionString;

    private async Task<WebApplicationFactory<Program>> FactoryAsync()
    {
        var host = await mailpit.SmtpHostAsync();
        var port = await mailpit.SmtpPortAsync();
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ControlPlane:Region", "vn");
            builder.UseSetting("ControlPlane:TenantDomain", "vn.example.com");
            builder.UseSetting("ControlPlane:ConnectionString", ControlPlaneConnectionString);
            builder.UseSetting("ControlPlane:DirectoryConnectionString", DirectoryConnectionString);
            builder.UseSetting("ControlPlane:Dns:Provider", "InMemory");
            builder.UseSetting("ControlPlane:MailFrom", "no-reply@vn.example.com");
            builder.UseSetting("ControlPlane:Mail:Host", host);
            builder.UseSetting("ControlPlane:Mail:Port", port.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("ControlPlane:Mail:UseTls", "false");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Id", "approvals");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Titles:en", "Approvals");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Titles:vi", "Phê duyệt");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Descriptions:en", "Approve requests");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Descriptions:vi", "Duyệt yêu cầu");
        });
    }

    private static string NewAddress() => $"{Guid.NewGuid():N}@example.org";

    private static string NewSubdomain() => $"s{Guid.NewGuid():N}"[..20];

    private static Dictionary<string, object?> Form(string email, string subdomain, string language = "en", string application = "") => new()
    {
        ["fullName"] = "An Nguyen",
        ["email"] = email,
        ["businessName"] = "Contoso Trading",
        ["subdomain"] = subdomain,
        ["language"] = language,
        ["application"] = application,
    };

    // Signs up, waits for the confirm email and returns the address, the subdomain and the token of its link.
    private async Task<(string Address, string Subdomain, string Token)> SignUpAsync(HttpClient client)
    {
        var address = NewAddress();
        var subdomain = NewSubdomain();
        var response = await client.PostAsJsonAsync("/api/sign-up", Form(address, subdomain), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var message = await WaitForMessageAsync(address);
        return (address, subdomain, TokenPattern().Match(message.Text).Groups[1].Value);
    }

    private static async Task<JsonElement> CheckAsync(HttpClient client, string subdomain)
    {
        var response = await client.GetAsync($"/api/sign-up/subdomains/{subdomain}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).Clone();
    }

    // The first text key of each JSON Pointer of the problem's errors.
    private static async Task<Dictionary<string, string>> ErrorsAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return problem.GetProperty("errors").EnumerateObject().ToDictionary(error => error.Name, error => error.Value[0].GetString()!);
    }

    private async Task AssertNothingStoredAsync()
    {
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.sign_ups"));
        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.jobs WHERE kind = 'sendSignUpConfirm'"));
    }

    // The hosted job runner sends the email, so the test waits up to a deadline.
    private async Task<MailpitMessage> WaitForMessageAsync(string address)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var messages = await mailpit.MessagesToAsync(address, cancellation);
            if (messages.Count > 0)
            {
                return messages[0];
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation);
        }

        throw new TimeoutException("The confirm email did not arrive in 30 seconds.");
    }

    private Task<T> Scalar<T>(string sql) => ControlPlaneDatabase.ScalarAsync<T>(_controlPlane, sql, TestContext.Current.CancellationToken);

    [GeneratedRegex("/sign-up/verify#([A-Za-z0-9_-]{43})")]
    private static partial Regex TokenPattern();
}
