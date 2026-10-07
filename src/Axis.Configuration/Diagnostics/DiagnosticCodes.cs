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
    public const string MisplacedManifest = "AXC0009";
    public const string UnreadableFile = "AXC0010";
    public const string DuplicateFieldName = "AXC0011";
    public const string UnknownReferenceTarget = "AXC0012";
    public const string InvalidConstraint = "AXC0013";
    public const string MissingTypeProperty = "AXC0014";
    public const string RemovedField = "AXC0015";
    public const string IncompatibleFieldChange = "AXC0016";
    public const string RemovedEntity = "AXC0017";
    public const string EntityOwnedByOtherApplication = "AXC0018";
    public const string UnlistableFolder = "AXC0019";
    public const string ApplicationNameActiveForOtherApplication = "AXC0020";

    public const string UnknownWidgetEntity = "AXC0021";
    public const string InvalidFormPage = "AXC0022";
    public const string UnknownNavigationPage = "AXC0023";
    public const string InvalidSitePath = "AXC0024";
    public const string InvalidSiteLocale = "AXC0025";
    public const string DuplicateLocale = "AXC0026";
    public const string LocaleDrift = "AXC0027";
    public const string MissingTextKey = "AXC0028";
    public const string InvalidDisplayField = "AXC0029";
    public const string ReferenceTargetWithoutDisplayField = "AXC0030";
    public const string SitePathActiveForOtherApplication = "AXC0031";
    public const string UnknownSeedEntity = "AXC0032";
    public const string InvalidSeedValue = "AXC0033";
    public const string DuplicateSeedRecordId = "AXC0034";
}
