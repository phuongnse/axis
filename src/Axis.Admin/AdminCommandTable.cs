using Microsoft.Extensions.Configuration;

namespace Axis.Admin;

/// <summary>
/// What a command writes to <c>platform.audit_records</c>. <see cref="Unmarked"/> is the default, so a new command that is
/// not marked fails the command table test.
/// </summary>
internal enum AuditMark
{
    Unmarked,
    None,
    OnePerTenant,

    /// <summary>One record for the whole run, with no tenant.</summary>
    OnePerRun,
}

/// <summary>One row of the command table. <paramref name="Name"/> is the leading arguments that pick the command, joined by spaces.</summary>
internal sealed record AdminCommand(string Name, AuditMark Audit, Func<AdminRun, Task<int>> RunAsync);

/// <summary>What a command needs to run: its arguments, the settings and the writers.</summary>
internal sealed record AdminRun(
    AdminCommand Command,
    string[] Args,
    IConfiguration Configuration,
    TextWriter Output,
    TextWriter Error,
    TimeSpan PublisherLease,
    TimeSpan PublisherTestLimit,
    CancellationToken CancellationToken);
