using System.Buffers.Text;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Axis.ControlPlane.Jobs;
using Axis.Core.Telemetry;
using Microsoft.AspNetCore.Builder;
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
    private const string Proxy = "10.0.0.5";
    private const string CompanyDomain = "contoso.example";

    private static readonly byte[] _challengeKey = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();

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

        var response = await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, address, subdomain, language: "vi", application: "approvals"), cancellation);

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
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, first, subdomain), cancellation)).StatusCode);

        var check = await CheckAsync(client, subdomain);
        Assert.False(check.GetProperty("free").GetBoolean());
        Assert.Equal("held", check.GetProperty("reason").GetString());
        var second = NewAddress();
        var refused = await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, second, subdomain), cancellation);
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
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, second, subdomain), cancellation)).StatusCode);
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
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, first, subdomain), cancellation)).StatusCode);
        await ControlPlaneDatabase.ExecuteAsync(
            _controlPlane,
            $"UPDATE control_plane.sign_ups SET expires_at = now() - interval '1 minute' WHERE email = '{first}'",
            cancellation);

        var response = await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, NewAddress(), subdomain), cancellation);

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

        var forms = new[] { await SolvedFormAsync(client, NewAddress(), subdomain), await SolvedFormAsync(client, NewAddress(), subdomain) };

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/sign-up", forms[0], cancellation),
            client.PostAsJsonAsync("/api/sign-up", forms[1], cancellation));

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
        var form = await SolvedFormAsync(client, NewAddress(), NewSubdomain());
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
        var response = await client.PostAsJsonAsync("/api/sign-up", new Dictionary<string, object?> { ["altcha"] = await SolveAsync(client), ["email"] = 5, ["language"] = "fr" }, cancellation);
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

    [Fact]
    public async Task A_sign_up_with_a_freshly_solved_challenge_answers_202_and_stores_the_solution_as_used_until_it_expires()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var address = NewAddress();

        var response = await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, address, NewSubdomain()), cancellation);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE email = '{address}'"));
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.sign_up_challenges WHERE octet_length(challenge) = 32 AND expires_at > now()"));
    }

    [Fact]
    public async Task A_sign_up_with_a_missing_malformed_wrong_or_expired_solution_is_refused_under_altcha_and_nothing_is_stored_while_a_signed_one_passes()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var future = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();
        var otherKey = Enumerable.Range(0, 32).Select(index => (byte)(index + 1)).ToArray();
        var solutions = new (string Case, string? Altcha)[]
        {
            ("missing", null),
            ("blank", "  "),
            ("not base64", "not base64!"),
            ("not json", Convert.ToBase64String("not json"u8)),
            ("wrong number", Forge(_challengeKey, future, number: 5, claimed: 6)),
            ("another key", Forge(otherKey, future, number: 5, claimed: 5)),
            ("expired", Forge(_challengeKey, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(), number: 5, claimed: 5)),
        };

        foreach (var (name, altcha) in solutions)
        {
            var form = Form(NewAddress(), NewSubdomain());
            if (altcha is not null)
            {
                form["altcha"] = altcha;
            }

            var response = await client.PostAsJsonAsync("/api/sign-up", form, cancellation);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, name);
            var errors = await ErrorsAsync(response);
            Assert.Equal(["/altcha"], errors.Keys);
            Assert.Equal("signUp.altcha.refused", errors["/altcha"]);
            await AssertNothingStoredAsync();
        }

        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.sign_up_challenges"));
        var signed = Form(NewAddress(), NewSubdomain());
        signed["altcha"] = Forge(_challengeKey, future, number: 5, claimed: 5);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", signed, cancellation)).StatusCode);
    }

    [Fact]
    public async Task A_second_sign_up_that_reuses_an_accepted_solution_is_refused_under_altcha_and_stores_no_row_for_its_email()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var first = await SolvedFormAsync(client, NewAddress(), NewSubdomain());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/sign-up", first, cancellation)).StatusCode);
        var secondAddress = NewAddress();
        var second = Form(secondAddress, NewSubdomain());
        second["altcha"] = first["altcha"];

        var response = await client.PostAsJsonAsync("/api/sign-up", second, cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("signUp.altcha.refused", (await ErrorsAsync(response))["/altcha"]);
        Assert.Equal(0, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE email = '{secondAddress}'"));
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.sign_up_challenges"));
    }

    [Fact]
    public async Task Two_parallel_sign_ups_with_one_solution_store_one_and_refuse_the_other_under_altcha()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var altcha = await SolveAsync(client);
        var forms = new[] { Form(NewAddress(), NewSubdomain()), Form(NewAddress(), NewSubdomain()) };
        forms[0]["altcha"] = altcha;
        forms[1]["altcha"] = altcha;

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/sign-up", forms[0], cancellation),
            client.PostAsJsonAsync("/api/sign-up", forms[1], cancellation));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Accepted);
        var refused = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal("signUp.altcha.refused", (await ErrorsAsync(refused))["/altcha"]);
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.sign_ups"));
    }

    [Fact]
    public async Task A_solution_is_used_up_by_a_try_that_a_field_check_refuses()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var form = await SolvedFormAsync(client, NewAddress(), NewSubdomain(), language: "fr");

        var wrongField = await client.PostAsJsonAsync("/api/sign-up", form, cancellation);
        form["language"] = "en";
        var again = await client.PostAsJsonAsync("/api/sign-up", form, cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, wrongField.StatusCode);
        Assert.Equal(["/language"], (await ErrorsAsync(wrongField)).Keys);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(["/altcha"], (await ErrorsAsync(again)).Keys);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task Six_tries_without_a_solution_from_one_client_address_are_refused_under_altcha_and_do_not_count_so_a_seventh_with_a_solution_answers_202()
    {
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var refused = await TryAsync(client, "203.0.113.1", solve: false);
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("signUp.altcha.refused", (await ErrorsAsync(refused))["/altcha"]);
        }

        var accepted = await TryAsync(client, "203.0.113.1");

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.sign_ups"));
    }

    [Fact]
    public async Task The_expiry_job_deletes_used_solutions_past_their_expiry_and_keeps_the_others()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();

        // Starting the host applies the migrations.
        using var client = factory.CreateClient();
        await ControlPlaneDatabase.ExecuteAsync(
            _controlPlane,
            """
            INSERT INTO control_plane.sign_up_challenges (challenge, expires_at) VALUES
                (sha256('past'::bytea), now() - interval '1 minute'),
                (sha256('future'::bytea), now() + interval '5 minutes')
            """,
            cancellation);

        var expiry = factory.Services.GetServices<IJobHandler>().Single(handler => handler.Kind == "expireSignUps");
        await expiry.RunAsync(new ClaimedJob(Guid.CreateVersion7(), "expireSignUps", [], 1), cancellation);

        Assert.Equal(0, await Scalar<long>("SELECT count(*) FROM control_plane.sign_up_challenges WHERE challenge = sha256('past'::bytea)"));
        Assert.Equal(1, await Scalar<long>("SELECT count(*) FROM control_plane.sign_up_challenges WHERE challenge = sha256('future'::bytea)"));
    }

    private string ControlPlaneConnectionString => $"{_container.GetConnectionString()};Timeout=5;Command Timeout=5";

    private string DirectoryConnectionString =>
        new NpgsqlConnectionStringBuilder(ControlPlaneConnectionString) { Database = DirectoryDatabase, Pooling = false }.ConnectionString;

    [Fact]
    public async Task The_sixth_try_in_an_hour_from_one_client_address_answers_429_with_retry_after_and_limit_address_and_another_address_passes()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await TryAsync(client, "203.0.113.1")).StatusCode);
        }

        var refusedAddress = NewAddress();
        var refused = await TryAsync(client, "203.0.113.1", refusedAddress);
        var other = await TryAsync(client, "203.0.113.2");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(cancellation);
        Assert.Equal("address", problem.GetProperty("limit").GetString());
        Assert.Equal(0, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE email = '{refusedAddress}'"));
        Assert.Empty(await mailpit.MessagesToAsync(refusedAddress, cancellation));
        Assert.Equal(HttpStatusCode.Accepted, other.StatusCode);
    }

    [Fact]
    public async Task A_try_with_an_invalid_body_counts_toward_the_address_limit()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await TryAsync(client, "203.0.113.1", form: new Dictionary<string, object?>())).StatusCode);
        }

        var refused = await TryAsync(client, "203.0.113.1");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("address", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("limit").GetString());
    }

    [Fact]
    public async Task The_21st_try_in_a_day_for_one_company_domain_answers_429_with_limit_domain_whatever_the_client_address()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await TryAsync(client, $"198.51.100.{attempt + 1}", $"{Guid.NewGuid():N}@{CompanyDomain}");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var refusedAddress = $"{Guid.NewGuid():N}@{CompanyDomain.ToUpperInvariant()}";
        var refused = await TryAsync(client, "198.51.100.100", refusedAddress);
        var otherDomain = await TryAsync(client, "198.51.100.101");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.True(refused.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("domain", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("limit").GetString());
        Assert.Equal(0, await Scalar<long>($"SELECT count(*) FROM control_plane.sign_ups WHERE lower(email) = '{refusedAddress.ToLowerInvariant()}'"));
        Assert.Equal(HttpStatusCode.Accepted, otherDomain.StatusCode);
    }

    [Fact]
    public async Task Addresses_at_a_public_mailbox_domain_count_one_by_one_and_never_reach_the_domain_limit()
    {
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var response = await TryAsync(client, $"192.0.2.{attempt + 1}", $"{Guid.NewGuid():N}@gmail.com");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
    }

    [Fact]
    public async Task At_a_public_mailbox_domain_one_address_counts_in_lower_case_without_its_tag()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var name = Guid.NewGuid().ToString("N");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var tagged = await TryAsync(client, $"192.0.2.{attempt + 1}", $"{name}+{attempt}@gmail.com");
            Assert.Equal(HttpStatusCode.Accepted, tagged.StatusCode);
        }

        var refused = await TryAsync(client, "192.0.2.100", $"{name.ToUpperInvariant()}@Gmail.com");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("domain", (await refused.Content.ReadFromJsonAsync<JsonElement>(cancellation)).GetProperty("limit").GetString());
    }

    [Fact]
    public async Task Each_refused_try_adds_one_to_the_refusal_metric_with_the_sign_up_partition()
    {
        using var factory = await FactoryAsync();
        using var client = factory.CreateClient();
        var meterFactory = factory.Services.GetRequiredService<IMeterFactory>();
        var measurements = new List<(long Value, string? Partition)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (ReferenceEquals(instrument.Meter.Scope, meterFactory) && instrument.Name == AxisMetrics.RateLimitRefusals)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, tags, _) =>
        {
            var partition = tags.ToArray().Single(tag => tag.Key == AxisTelemetry.PartitionTag).Value as string;
            lock (measurements)
            {
                measurements.Add((measurement, partition));
            }
        });
        listener.Start();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await TryAsync(client, "203.0.113.1")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await TryAsync(client, "203.0.113.1")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await TryAsync(client, "203.0.113.1")).StatusCode);

        lock (measurements)
        {
            Assert.Equal([(1L, "sign_up"), (1L, "sign_up")], measurements);
        }
    }

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
            builder.UseSetting("ControlPlane:SignUp:ChallengeKey", Convert.ToBase64String(_challengeKey));
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Id", "approvals");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Titles:en", "Approvals");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Titles:vi", "Phê duyệt");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Descriptions:en", "Approve requests");
            builder.UseSetting("ControlPlane:SignUp:Applications:0:Descriptions:vi", "Duyệt yêu cầu");
            builder.UseSetting("ControlPlane:SignUp:PublicMailboxDomains:0", "gmail.com");

            // The test server has no client address, so the filter gives every request the one proxy the control plane trusts.
            // A test then picks the client address with X-Forwarded-For. A request without the header shares one address.
            builder.UseSetting("ForwardedHeaders:KnownProxies:0", Proxy);
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ProxyStartupFilter>());
        });
    }

    // One sign-up try from the client address that X-Forwarded-For names. The form is a valid one unless the test gives it, and it
    // carries a freshly solved challenge unless solve is false.
    private static async Task<HttpResponseMessage> TryAsync(HttpClient client, string clientAddress, string? email = null, Dictionary<string, object?>? form = null, bool solve = true)
    {
        form ??= Form(email ?? NewAddress(), NewSubdomain());
        if (solve)
        {
            form["altcha"] = await SolveAsync(client);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/sign-up") { Content = JsonContent.Create(form) };
        request.Headers.Add("X-Forwarded-For", clientAddress);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    // Gives every request the address of the one proxy that the control plane trusts, before the forwarded headers are read.
    private sealed class ProxyStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(Proxy);
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    // The form with a freshly fetched and solved challenge as altcha.
    private static async Task<Dictionary<string, object?>> SolvedFormAsync(HttpClient client, string email, string subdomain, string language = "en", string application = "")
    {
        var form = Form(email, subdomain, language, application);
        form["altcha"] = await SolveAsync(client);
        return form;
    }

    // Fetches a challenge and finds its number the way the page does, then builds the text the page sends as altcha.
    private static async Task<string> SolveAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/sign-up/challenge", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var challenge = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var salt = challenge.GetProperty("salt").GetString()!;
        var target = challenge.GetProperty("challenge").GetString()!;
        for (var number = 0; number <= challenge.GetProperty("maxnumber").GetInt32(); number++)
        {
            if (Hash(salt, number) == target)
            {
                return Payload(challenge.GetProperty("challenge").GetString()!, salt, challenge.GetProperty("signature").GetString()!, number);
            }
        }

        throw new InvalidOperationException("The challenge has no solution.");
    }

    // A solution signed with the key, whatever its number or expiry: the challenge hashes the real number and the payload claims another.
    private static string Forge(byte[] key, long expires, int number, int claimed)
    {
        var salt = $"{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}?expires={expires}&";
        var challenge = Hash(salt, number);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, System.Text.Encoding.UTF8.GetBytes(challenge)));
        return Payload(challenge, salt, signature, claimed);
    }

    private static string Hash(string salt, int number) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{salt}{number.ToString(CultureInfo.InvariantCulture)}")));

    private static string Payload(string challenge, string salt, string signature, int number) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { algorithm = "SHA-256", challenge, number, salt, signature }));

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
        var response = await client.PostAsJsonAsync("/api/sign-up", await SolvedFormAsync(client, address, subdomain), TestContext.Current.CancellationToken);
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
