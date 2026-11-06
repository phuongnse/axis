using System.Collections.Concurrent;

namespace Axis.ControlPlane.Dns;

/// <summary>The records in memory, for tests and Development. The settings refuse it in every other environment.</summary>
internal sealed class InMemoryDnsRecords : IDnsRecords
{
    private readonly ConcurrentDictionary<string, InMemoryCname> _records = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A copy of the <c>CNAME</c> records now held, by name.</summary>
    public IReadOnlyDictionary<string, InMemoryCname> Records => new Dictionary<string, InMemoryCname>(_records, StringComparer.OrdinalIgnoreCase);

    public Task SetCnameAsync(string name, string target, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        cancellationToken.ThrowIfCancellationRequested();
        _records[name.TrimEnd('.')] = new InMemoryCname(target.TrimEnd('.'), IDnsRecords.TimeToLive);
        return Task.CompletedTask;
    }

    public Task RemoveCnameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        cancellationToken.ThrowIfCancellationRequested();
        _records.TryRemove(name.TrimEnd('.'), out _);
        return Task.CompletedTask;
    }
}

/// <summary>A record of <see cref="InMemoryDnsRecords"/>: where the name points and its time to live in seconds.</summary>
internal sealed record InMemoryCname(string Target, int TimeToLive);
