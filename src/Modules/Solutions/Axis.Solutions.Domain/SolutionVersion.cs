namespace Axis.Solutions.Domain;

public sealed class SolutionVersion
{
    private SolutionVersion()
    {
    }

    private SolutionVersion(
        Guid id,
        string solutionKey,
        string version,
        string packageSha256,
        byte[] envelope,
        string axisOpenApiSha256,
        string publisherId,
        string publisherKeyId,
        string sourceRevision,
        string buildId,
        DateTimeOffset builtAt,
        string sourceUri,
        DateTimeOffset publishedAt)
    {
        Id = id;
        SolutionKey = solutionKey;
        Version = version;
        PackageSha256 = packageSha256;
        Envelope = envelope;
        AxisOpenApiSha256 = axisOpenApiSha256;
        PublisherId = publisherId;
        PublisherKeyId = publisherKeyId;
        SourceRevision = sourceRevision;
        BuildId = buildId;
        BuiltAt = builtAt;
        SourceUri = sourceUri;
        PublishedAt = publishedAt;
    }

    public Guid Id { get; private set; }
    public string SolutionKey { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string PackageSha256 { get; private set; } = string.Empty;
    public byte[] Envelope { get; private set; } = [];
    public string AxisOpenApiSha256 { get; private set; } = string.Empty;
    public string PublisherId { get; private set; } = string.Empty;
    public string PublisherKeyId { get; private set; } = string.Empty;
    public string SourceRevision { get; private set; } = string.Empty;
    public string BuildId { get; private set; } = string.Empty;
    public DateTimeOffset BuiltAt { get; private set; }
    public string SourceUri { get; private set; } = string.Empty;
    public DateTimeOffset PublishedAt { get; private set; }

    public static SolutionVersion Create(
        string solutionKey,
        string version,
        string packageSha256,
        ReadOnlySpan<byte> envelope,
        string axisOpenApiSha256,
        string publisherId,
        string publisherKeyId,
        string sourceRevision,
        string buildId,
        DateTimeOffset builtAt,
        Uri sourceUri,
        DateTimeOffset publishedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(axisOpenApiSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);
        ArgumentNullException.ThrowIfNull(sourceUri);
        if (envelope.IsEmpty || builtAt == default || publishedAt == default)
            throw new ArgumentException("Solution version data is incomplete.");

        return new SolutionVersion(
            Guid.NewGuid(),
            solutionKey,
            version,
            packageSha256,
            envelope.ToArray(),
            axisOpenApiSha256,
            publisherId,
            publisherKeyId,
            sourceRevision,
            buildId,
            builtAt,
            sourceUri.AbsoluteUri,
            publishedAt);
    }
}
