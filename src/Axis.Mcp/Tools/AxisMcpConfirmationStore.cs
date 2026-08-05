using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Axis.Mcp.Tools;

public sealed record BusinessObjectPublishConfirmation(
    string Token,
    Guid BusinessObjectDefinitionId,
    int ExpectedRevision,
    DateTimeOffset ExpiresAt);

public sealed class AxisMcpConfirmationStore
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, ConfirmationRecord> _confirmations = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _lifetime;

    public AxisMcpConfirmationStore()
        : this(TimeProvider.System, DefaultLifetime)
    {
    }

    public AxisMcpConfirmationStore(TimeProvider timeProvider, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "The confirmation lifetime must be positive.");

        _timeProvider = timeProvider;
        _lifetime = lifetime;
    }

    public BusinessObjectPublishConfirmation Create(
        Guid businessObjectDefinitionId,
        int expectedRevision,
        string subject,
        string snapshotHash)
    {
        if (businessObjectDefinitionId == Guid.Empty)
            throw new ArgumentException("The business-object definition id is required.", nameof(businessObjectDefinitionId));
        if (expectedRevision < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotHash);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        PruneExpired(now);
        DateTimeOffset expiresAt = now.Add(_lifetime);
        string token = CreateToken();
        _confirmations[token] = new ConfirmationRecord(
            businessObjectDefinitionId,
            expectedRevision,
            Fingerprint(subject),
            snapshotHash,
            expiresAt);

        return new BusinessObjectPublishConfirmation(
            token,
            businessObjectDefinitionId,
            expectedRevision,
            expiresAt);
    }

    public bool TryConsume(
        string token,
        Guid businessObjectDefinitionId,
        int expectedRevision,
        string subject,
        string snapshotHash)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            businessObjectDefinitionId == Guid.Empty ||
            expectedRevision < 1 ||
            string.IsNullOrWhiteSpace(subject) ||
            string.IsNullOrWhiteSpace(snapshotHash))
            return false;

        if (!_confirmations.TryGetValue(token, out ConfirmationRecord? confirmation))
            return false;

        if (confirmation.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            _confirmations.TryRemove(token, out _);
            return false;
        }

        if (confirmation.BusinessObjectDefinitionId != businessObjectDefinitionId ||
            confirmation.ExpectedRevision != expectedRevision ||
            !CryptographicEquals(confirmation.SubjectFingerprint, Fingerprint(subject)) ||
            !CryptographicEquals(confirmation.SnapshotHash, snapshotHash))
            return false;

        // Removal is the commit point. A second concurrent consumer cannot
        // pass this point because only one TryRemove can succeed.
        return _confirmations.TryRemove(token, out _);
    }

    public static string ComputeSnapshotHash(string snapshotJson)
    {
        ArgumentNullException.ThrowIfNull(snapshotJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach ((string token, ConfirmationRecord confirmation) in _confirmations)
        {
            if (confirmation.ExpiresAt <= now)
                _confirmations.TryRemove(token, out _);
        }
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool CryptographicEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left),
            Encoding.UTF8.GetBytes(right));

    private sealed record ConfirmationRecord(
        Guid BusinessObjectDefinitionId,
        int ExpectedRevision,
        string SubjectFingerprint,
        string SnapshotHash,
        DateTimeOffset ExpiresAt);
}
