namespace Axis.Processes.Storage;

/// <summary>
/// A row of <c>axis.package_calls</c>: one package operation call that a process step staged. Its
/// id is the call's operation identity. Times use the database clock. No output is stored.
/// </summary>
public sealed class PackageCallRow
{
    /// <summary>The call's operation identity, a version 7 UUID.</summary>
    public required Guid Id { get; init; }

    public required Guid ProcessInstanceId { get; init; }

    /// <summary>The declared name of the package operation step.</summary>
    public required string Step { get; init; }

    /// <summary>The release the instance is pinned to.</summary>
    public required Guid ReleaseId { get; init; }

    /// <summary>The package id.</summary>
    public required string Package { get; init; }

    /// <summary>The package version the release pins.</summary>
    public required string Version { get; init; }

    /// <summary>The operation's declared name.</summary>
    public required string Operation { get; init; }

    /// <summary><c>pending</c>, <c>sending</c>, <c>unknown</c>, <c>succeeded</c> or <c>failed</c>.</summary>
    public required string State { get; init; }

    /// <summary>The attempts made so far, the first one included.</summary>
    public required int Attempts { get; init; }

    /// <summary>
    /// The evaluated input as JSON text, or <see langword="null"/> once the call settled. It is
    /// never copied to history, audit records or logs.
    /// </summary>
    public string? Input { get; init; }

    /// <summary>The error of the last attempt. It holds no value of the input and no exception message.</summary>
    public string? LastError { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the call became <c>succeeded</c> or <c>failed</c>, or <see langword="null"/> before.</summary>
    public DateTimeOffset? SettledAt { get; init; }
}
