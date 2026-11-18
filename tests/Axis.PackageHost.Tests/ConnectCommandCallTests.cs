using System.Net;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;
using Axis.Integration.Tests;
using Microsoft.Extensions.Logging.Abstractions;

namespace Axis.PackageHost.Tests;

/// <summary>
/// The <c>connect</c> command against a fake tenant, with the real C# runner of the test package: how each answer of a runner is posted,
/// what happens when a lease ends, and how the token and a <c>401</c> are handled, see
/// docs/reference/extension-packages.md#customer-hosted-runners. Each wait ends on a state, under a deadline that only bounds a hang.
/// </summary>
public sealed class ConnectCommandCallTests
{
    private static readonly TimeSpan _deadline = TimeSpan.FromSeconds(90);

    [Theory]
    [InlineData("sum", "succeeded")]
    [InlineData("fail", "failed")]
    [InlineData("flaky", "retryable")]
    public async Task A_call_posts_the_outcome_of_the_operation(string operation, string outcome)
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant();
        var lease = Guid.NewGuid();
        tenant.EnqueueCall(lease, operation, operation switch
        {
            "sum" => new JsonObject { ["a"] = "2", ["b"] = "3" },
            "fail" => new JsonObject { ["a"] = "1" },
            _ => new JsonObject { ["failures"] = "1" },
        }, TimeSpan.FromSeconds(60));

        var (exitCode, error) = await RunUntilAsync(extensions, tenant, () => tenant.Results.Count == 1);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error);
        var posted = Assert.Single(tenant.Results);
        Assert.Equal(lease, posted.Lease);
        Assert.Equal(outcome, (string?)posted.Body["outcome"]);
        switch (outcome)
        {
            case "succeeded":
                Assert.Equal("5", (string?)posted.Body["output"]!["total"]);
                break;
            case "failed":
                Assert.Equal(typeof(FormatException).FullName, (string?)posted.Body["error"]);
                break;
            default:
                Assert.Null(posted.Body["error"]);
                break;
        }
    }

    [Fact]
    public async Task A_call_that_asks_for_an_undeclared_binding_posts_the_error_text_of_the_worker()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant();
        tenant.EnqueueCall(Guid.NewGuid(), "probe", new JsonObject { ["mode"] = "undeclaredSecret" }, TimeSpan.FromSeconds(60));

        await RunUntilAsync(extensions, tenant, () => tenant.Results.Count == 1);

        var posted = Assert.Single(tenant.Results);
        Assert.Equal("failed", (string?)posted.Body["outcome"]);
        Assert.Equal(PackageOperationContext.BindingRefusedMessage("probe", "test-package", "OtherToken"), (string?)posted.Body["error"]);
    }

    [Fact]
    public async Task A_runner_still_running_when_its_lease_ends_is_killed_and_no_result_is_posted()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant();
        var slow = Guid.NewGuid();
        var sum = Guid.NewGuid();
        tenant.EnqueueCall(slow, "slow", new JsonObject { ["a"] = "1" }, TimeSpan.FromSeconds(1));
        tenant.EnqueueCall(sum, "sum", new JsonObject { ["a"] = "2", ["b"] = "3" }, TimeSpan.FromSeconds(60));

        await RunUntilAsync(extensions, tenant, () => tenant.Results.Count == 1);

        var posted = Assert.Single(tenant.Results);
        Assert.Equal(sum, posted.Lease);
        Assert.Equal("succeeded", (string?)posted.Body["outcome"]);
        Assert.DoesNotContain(tenant.Results, result => result.Lease == slow);
    }

    [Fact]
    public async Task A_network_error_and_a_result_that_answers_conflict_do_not_stop_the_command()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant { ResultStatus = HttpStatusCode.Conflict };
        tenant.EnqueueTake(() => throw new HttpRequestException("The tenant is down."));
        tenant.EnqueueTake(() => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        tenant.EnqueueCall(Guid.NewGuid(), "sum", new JsonObject { ["a"] = "2", ["b"] = "3" }, TimeSpan.FromSeconds(60));
        tenant.EnqueueCall(Guid.NewGuid(), "sum", new JsonObject { ["a"] = "4", ["b"] = "5" }, TimeSpan.FromSeconds(60));

        var (exitCode, _) = await RunUntilAsync(extensions, tenant, () => tenant.Results.Count == 2);

        Assert.Equal(0, exitCode);
        Assert.Equal(["5", "9"], tenant.Results.Select(result => (string?)result.Body["output"]!["total"]).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task It_registers_the_manifest_of_every_installed_version_and_takes_with_every_version()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0", "2.0.0");
        var tenant = new FakeTenant();

        await RunUntilAsync(extensions, tenant, () => tenant.TakeRequests >= 1);

        var registered = tenant.Registered!["packages"]!.AsArray();
        Assert.Equal(["1.0.0", "2.0.0"], registered.Select(package => (string?)package!["manifest"]!["version"]));
        Assert.Equal(1, tenant.Registrations);
    }

    [Fact]
    public async Task A_conflicting_registration_exits_with_code_1_and_prints_the_reason()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        const string title = "The manifest of package 'test-package' version 1.0.0 differs from the one a release in use pins.";
        var tenant = new FakeTenant
        {
            RegistrationAnswer = () => FakeTenant.Json(HttpStatusCode.Conflict, new JsonObject { ["title"] = title }),
        };

        var (exitCode, error) = await RunUntilAsync(extensions, tenant, () => false);

        Assert.Equal(1, exitCode);
        Assert.Contains(title, error, StringComparison.Ordinal);
        Assert.Equal(0, tenant.TakeRequests);
    }

    [Fact]
    public async Task A_token_endpoint_that_refuses_the_credential_exits_with_code_1_and_names_its_setting()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant { TokenStatus = HttpStatusCode.BadRequest };

        var (exitCode, error) = await RunUntilAsync(extensions, tenant, () => false);

        Assert.Equal(1, exitCode);
        Assert.Contains("Connect:TokenUrl", error, StringComparison.Ordinal);
        Assert.DoesNotContain("runner-secret", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_unauthorized_answer_gets_one_fresh_token_and_one_retry_that_succeeds()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant();
        tenant.EnqueueTake(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        tenant.EnqueueCall(Guid.NewGuid(), "sum", new JsonObject { ["a"] = "2", ["b"] = "3" }, TimeSpan.FromSeconds(60));

        var (exitCode, _) = await RunUntilAsync(extensions, tenant, () => tenant.Results.Count == 1);

        Assert.Equal(0, exitCode);
        Assert.Equal(2, tenant.TokenRequests);
        Assert.Equal(["token-1", "token-2"], tenant.TakeTokens.Take(2));
        Assert.Equal("token-2", Assert.Single(tenant.Results).Token);
    }

    [Fact]
    public async Task Two_unauthorized_answers_in_a_row_exit_with_code_1()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant();
        tenant.EnqueueTake(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        tenant.EnqueueTake(() => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var (exitCode, error) = await RunUntilAsync(extensions, tenant, () => false);

        Assert.Equal(1, exitCode);
        Assert.Contains("Connect:Url", error, StringComparison.Ordinal);
        Assert.Equal(2, tenant.TokenRequests);
        Assert.Empty(tenant.Results);
    }

    [Fact]
    public async Task The_token_is_kept_between_requests()
    {
        using var extensions = new TemporaryFolder().Install("1.0.0");
        var tenant = new FakeTenant();

        await RunUntilAsync(extensions, tenant, () => tenant.TakeRequests >= 3);

        Assert.Equal(1, tenant.TokenRequests);
        Assert.All(tenant.TakeTokens, token => Assert.Equal("token-1", token));
    }

    /// <summary>Runs the command until <paramref name="done"/> or its end, then cancels it and returns its exit code and error output.</summary>
    private static async Task<(int ExitCode, string Error)> RunUntilAsync(TemporaryFolder extensions, FakeTenant tenant, Func<bool> done)
    {
        var error = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        var command = ConnectCommand.RunAsync(
            ConnectTestSettings.Create(extensions.Path),
            development: false,
            TestRunnerCommand.Create(),
            error,
            NullLogger.Instance,
            cancellation.Token,
            tenant,
            tenant,
            TimeSpan.FromMilliseconds(50));
        try
        {
            await Polling.WaitUntilAsync(() => done() || command.IsCompleted, "The command did not reach the expected state.", _deadline);
        }
        finally
        {
            await cancellation.CancelAsync();
        }

        return (await command, error.ToString());
    }
}
