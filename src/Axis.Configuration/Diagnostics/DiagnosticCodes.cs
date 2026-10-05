namespace Axis.Configuration.Diagnostics;

/// <summary>Stable diagnostic codes. A code never changes meaning once published.</summary>
public static class DiagnosticCodes
{
    public const string InvalidJson = "AXC0001";
    public const string MissingKind = "AXC0002";
    public const string UnknownKind = "AXC0003";
    public const string SchemaViolation = "AXC0004";
    public const string DuplicateId = "AXC0005";
    public const string DuplicateName = "AXC0006";
    public const string ManifestMissing = "AXC0007";
    public const string MultipleManifests = "AXC0008";
}
