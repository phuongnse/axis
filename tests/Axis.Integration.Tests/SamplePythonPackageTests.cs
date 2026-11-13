using Axis.Configuration.Packages;
using Axis.Processes.Instances;
using Axis.Processes.Work;

namespace Axis.Integration.Tests;

/// <summary>
/// What the sample Python package <c>phone-numbers</c> does when a process step calls it, see
/// docs/reference/extension-packages.md#sample-and-test-packages. The calls run through the worker, a package host in this
/// process and a real Python runner process that imports the <c>phonenumbers</c> library from the version folder.
/// The tests need <c>python3</c> on the path, so they are skipped on Windows. Each wait ends on a state, never on elapsed time.
/// </summary>
public sealed class SamplePythonPackageTests(PackageCallFixture fixture) : IClassFixture<PackageCallFixture>
{
    private const string PythonOnWindows = "The Python runner tests need python3 on the path.";

    private const string PackageId = "phone-numbers";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_phone_number_is_normalized_to_e164_on_the_record()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (subjectId, instanceId) = await fixture.StageAsync("SamplePhoneNumber", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Completed, await fixture.RunCallOnAsync(Process(host.Address), CancellationToken));

        var request = await fixture.RequestAsync(subjectId);
        Assert.Equal("+16502530000", request.Values["phone"]!.GetValue<string>());
        Assert.Equal(PackageCalls.Succeeded, (string)(await fixture.Tenant.ScalarAsync("SELECT state FROM axis.package_calls WHERE id = @id", ("id", callId)))!);
        Assert.Single(host.RunnerIds(PackageId, "1.0.0"));
    }

    [Fact]
    public async Task A_text_that_is_no_number_fails_the_call_with_the_type_name_of_the_library_error_and_no_message()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), PythonOnWindows);
        const string error = "phonenumbers.phonenumberutil.NumberParseException";
        await using var host = await TestPackageHost.StartAsync(fixture.PackagesPath);
        var (_, instanceId) = await fixture.StageAsync("SamplePhoneNumberUnparsed", CancellationToken);
        var callId = await CallIdAsync(instanceId);

        Assert.Equal(WorkItemOutcome.Failed, await fixture.RunCallOnAsync(Process(host.Address), CancellationToken));

        Assert.Equal(
            (PackageCalls.Failed, error),
            ((string)(await fixture.Tenant.ScalarAsync("SELECT state FROM axis.package_calls WHERE id = @id", ("id", callId)))!,
                (string?)await fixture.Tenant.ScalarAsync("SELECT last_error FROM axis.package_calls WHERE id = @id", ("id", callId))));
        Assert.Equal(
            ("failed", error, 1L),
            ((string)(await fixture.Tenant.ScalarAsync("SELECT outcome FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)))!,
                (string?)await fixture.Tenant.ScalarAsync("SELECT error FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId)),
                await fixture.Tenant.CountAsync("SELECT count(*) FROM axis.process_call_attempts WHERE call_id = @id AND call_kind = 'package'", ("id", callId))));
        Assert.Equal(ProcessStarts.Failed, (await fixture.Tenant.InstanceAsync(instanceId)).State);
    }

    private static Dictionary<string, string?> Process(string python) =>
        new()
        {
            [PackageIsolation.IsolationSetting] = "process",
            [PackageIsolation.DotnetRunnerSetting] = TestPackageHost.ClosedAddress(),
            [PackageIsolation.PythonRunnerSetting] = python,
            [PackageIsolation.ProxySetting] = "0",
        };

    private async Task<Guid> CallIdAsync(Guid instanceId) =>
        (Guid)(await fixture.Tenant.ScalarAsync("SELECT id FROM axis.package_calls WHERE process_instance_id = @id", ("id", instanceId)))!;
}
