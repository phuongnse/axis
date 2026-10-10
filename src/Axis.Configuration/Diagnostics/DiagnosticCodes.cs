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
    public const string UnknownWidgetDataSource = "AXC0059";
    public const string InvalidWidgetBinding = "AXC0060";
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
    public const string ChildEntityOwnedTwice = "AXC0039";
    public const string ReferenceToChildEntity = "AXC0040";
    public const string ChildEntityWithReference = "AXC0041";

    public const string UnknownDataSourceEntity = "AXC0042";
    public const string InvalidDataSourceFieldPath = "AXC0043";
    public const string DuplicateDataSourceFieldName = "AXC0044";
    public const string InvalidDataSourceSort = "AXC0045";
    public const string InvalidDataSourceParameterName = "AXC0054";
    public const string UnknownDataSourceGroupField = "AXC0061";
    public const string DuplicateDataSourceMeasureName = "AXC0062";
    public const string InvalidDataSourceMeasure = "AXC0063";
    public const string DataSourceOverChildEntity = "AXC0064";

    public const string UnknownValidationField = "AXC0052";

    public const string RuleCallCycle = "AXC0055";
    public const string RuleCallTooDeep = "AXC0065";
    public const string DuplicateRuleParameter = "AXC0056";
    public const string RuleNameIsFunction = "AXC0057";

    public const string UnknownSequence = "AXC0066";

    public const string UnknownProcessEntity = "AXC0067";
    public const string ProcessOverChildEntity = "AXC0068";
    public const string DuplicateStepName = "AXC0069";
    public const string UnknownStep = "AXC0070";
    public const string UnreachableStep = "AXC0071";
    public const string StepWithoutEnd = "AXC0072";
    public const string ProcessStepCycle = "AXC0073";

    public const string UnknownFormEntity = "AXC0074";
    public const string UnknownFormField = "AXC0075";
    public const string DuplicateFormField = "AXC0076";
    public const string UnknownWidgetForm = "AXC0077";
}
