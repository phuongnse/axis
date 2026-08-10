using System.Security.Cryptography;

namespace Axis.Solutions.Domain;

public enum TrustedPublisherKeyStatus
{
    Active = 0,
    Revoked = 1,
}

public sealed class TrustedPublisherKey
{
    private TrustedPublisherKey()
    {
    }

    private TrustedPublisherKey(string publisherId, string keyId, string spkiSha256, string publicKeyPem, long revision)
    {
        Id = Guid.NewGuid();
        PublisherId = publisherId;
        KeyId = keyId;
        SpkiSha256 = spkiSha256;
        PublicKeyPem = publicKeyPem;
        Status = TrustedPublisherKeyStatus.Active;
        ConfigurationRevision = revision;
    }

    public Guid Id { get; private set; }
    public string PublisherId { get; private set; } = string.Empty;
    public string KeyId { get; private set; } = string.Empty;
    public string SpkiSha256 { get; private set; } = string.Empty;
    public string PublicKeyPem { get; private set; } = string.Empty;
    public TrustedPublisherKeyStatus Status { get; private set; }
    public long ConfigurationRevision { get; private set; }
    public bool IsTombstone { get; private set; }

    public static TrustedPublisherKey Create(string publisherId, string keyId, string publicKeyPem, long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        if (revision <= 0)
            throw new ArgumentOutOfRangeException(nameof(revision));

        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        ECParameters parameters = key.ExportParameters(false);
        if (parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            throw new ArgumentException("Publisher keys must use P-256.", nameof(publicKeyPem));
        string fingerprint = Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        return new TrustedPublisherKey(publisherId, keyId, fingerprint, publicKeyPem, revision);
    }

    public void Revoke(long revision)
    {
        if (revision <= ConfigurationRevision)
            throw new InvalidOperationException("Configuration revision must increase.");
        Status = TrustedPublisherKeyStatus.Revoked;
        IsTombstone = true;
        ConfigurationRevision = revision;
    }

    public void ReconcileActive(string publicKeyPem, long revision)
    {
        TrustedPublisherKey candidate = Create(PublisherId, KeyId, publicKeyPem, revision);
        if (IsTombstone || Status == TrustedPublisherKeyStatus.Revoked || candidate.SpkiSha256 != SpkiSha256)
            throw new InvalidOperationException("A publisher key cannot be resurrected or substituted.");
        if (revision <= ConfigurationRevision)
            throw new InvalidOperationException("Configuration revision must increase.");
        ConfigurationRevision = revision;
    }
}
