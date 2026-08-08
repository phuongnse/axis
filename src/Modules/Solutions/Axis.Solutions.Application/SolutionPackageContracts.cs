namespace Axis.Solutions.Application;

public sealed record VerifiedSolutionPackage(
    byte[] EnvelopeBytes,
    byte[] PayloadBytes,
    string PackageSha256,
    string SolutionKey,
    string SolutionVersion,
    string AxisOpenApiSha256,
    string PublisherId,
    string PublisherKeyId,
    SolutionProvenance Provenance,
    IReadOnlyList<VerifiedSolutionComponent> Components);

public sealed record SolutionProvenance(
    string SourceRevision,
    string BuildId,
    DateTimeOffset BuiltAt,
    Uri SourceUri);

public sealed record VerifiedSolutionComponent(
    string Type,
    string Key,
    string Sha256,
    byte[] Content,
    IReadOnlyList<SolutionComponentReference> DependsOn);

public sealed record SolutionComponentReference(string Type, string Key);

public sealed record TrustedPublisherSnapshot(
    string PublisherId,
    string KeyId,
    string PublicKeyPem,
    bool IsActive,
    bool IsTombstone,
    long ConfigurationRevision);

public interface ITrustedPublisherKeyReader
{
    Task<TrustedPublisherSnapshot?> FindAsync(
        string publisherId,
        string keyId,
        CancellationToken cancellationToken = default);
}

public sealed record TrustedPublisherConfigurationKey(string PublisherId, string KeyId, string PublicKeyPem, bool IsActive);
public sealed record TrustedPublisherIdentity(string PublisherId, string KeyId);

public interface ITrustedPublisherLedger
{
    Task<IReadOnlyList<TrustedPublisherIdentity>> ReconcileAsync(long configurationRevision, IReadOnlyList<TrustedPublisherConfigurationKey> candidate, CancellationToken cancellationToken = default);
}

public sealed class SolutionPackageException(string problemCode) : Exception(problemCode)
{
    public string ProblemCode { get; } = problemCode;
}
