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
    // Retired: the removed form page link of table widgets. Never reported and never reused.
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
    public const string ChildEntityWithChildCollection = "AXC0041";

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
    public const string InvalidProcessSubject = "AXC0244";
    public const string DuplicateProcessParameter = "AXC0245";
    public const string StepNeedsSubject = "AXC0246";
    public const string TriggerProcessWithoutSubject = "AXC0247";
    public const string ScheduleJobProcessWithSubject = "AXC0249";
    public const string UnknownSubProcess = "AXC0250";
    public const string SubProcessCycle = "AXC0251";
    public const string InvalidSubProcessSubject = "AXC0252";
    public const string InvalidSubProcessParameters = "AXC0253";
    public const string DeleteTriggerWithOtherKinds = "AXC0254";
    public const string DeleteTriggerWithFields = "AXC0255";
    public const string DeleteTriggerProcessWithSubject = "AXC0256";
    public const string UnknownCreateRecordEntity = "AXC0260";
    public const string CreateRecordOverChildEntity = "AXC0261";
    public const string UnknownOperation = "AXC0078";
    public const string UnknownSetField = "AXC0079";
    public const string ReadOnlySetField = "AXC0080";
    public const string InvalidTaskAssignee = "AXC0081";
    public const string UnknownTaskRole = "AXC0089";
    public const string UnknownTaskForm = "AXC0082";
    public const string TaskFormOverOtherEntity = "AXC0083";
    public const string InvalidTaskDueIn = "AXC0084";
    public const string DuplicateOutcomeName = "AXC0085";

    public const string UnknownFormEntity = "AXC0074";
    public const string UnknownFormField = "AXC0075";
    public const string DuplicateFormField = "AXC0076";
    public const string UnknownWidgetForm = "AXC0077";
    public const string UnknownActionProcess = "AXC0087";
    public const string ActionProcessOverOtherEntity = "AXC0088";

    public const string UnknownPolicyRole = "AXC0090";
    public const string InvalidGrant = "AXC0091";
    public const string UnknownGrantTarget = "AXC0092";
    public const string GrantOnChildEntity = "AXC0093";
    public const string UnknownGrantField = "AXC0094";

    public const string DuplicateConnectorName = "AXC0096";
    public const string InvalidOperationPath = "AXC0097";
    public const string InvalidOperationLookup = "AXC0098";
    public const string InvalidConnectorTimeout = "AXC0099";
    public const string MissingDeploymentBinding = "AXC0100";
    public const string InsecureBaseUrl = "AXC0101";

    public const string UnknownConnector = "AXC0102";
    public const string UnknownConnectorOperation = "AXC0103";
    public const string UnknownConnectorInput = "AXC0104";
    public const string MissingConnectorInput = "AXC0105";
    public const string UnknownConnectorOutput = "AXC0106";
    public const string UnknownOutputField = "AXC0107";
    public const string InvalidOutputField = "AXC0108";
    public const string InvalidRetryBackoff = "AXC0109";

    public const string UnknownEventEntity = "AXC0110";
    public const string EventOverChildEntity = "AXC0111";
    public const string DuplicateEventFieldName = "AXC0112";
    public const string InvalidEventFieldType = "AXC0113";

    public const string UnknownEndpointEntity = "AXC0114";
    public const string EndpointOverChildEntity = "AXC0115";
    public const string UnknownEndpointProcess = "AXC0116";
    public const string EndpointProcessOverOtherEntity = "AXC0117";
    public const string DuplicateEndpointInputName = "AXC0118";
    public const string InvalidEndpointInputTarget = "AXC0119";
    public const string UnknownEndpointValueField = "AXC0120";
    public const string ReadOnlyEndpointValueField = "AXC0121";
    public const string InvalidEndpointCollectionValue = "AXC0122";
    public const string UnknownEndpointOutputField = "AXC0123";
    public const string InvalidEndpointWait = "AXC0124";

    public const string UnknownStepEvent = "AXC0125";
    public const string StepEventOverOtherEntity = "AXC0126";
    public const string UnknownEventPayloadField = "AXC0127";
    public const string MissingEventPayloadField = "AXC0128";

    public const string UnknownTriggerProcess = "AXC0129";
    public const string UnknownTriggerEvent = "AXC0130";
    public const string TriggerEventOverOtherEntity = "AXC0131";

    public const string PackageManifestInvalid = "AXC0132";
    public const string PackageFolderMismatch = "AXC0133";
    public const string PackageAssemblyMissing = "AXC0134";
    public const string PackageVersionMismatch = "AXC0135";
    public const string InvalidPackageOperationType = "AXC0136";
    public const string DuplicatePackageName = "AXC0137";
    public const string InvalidPackageTimeout = "AXC0138";

    public const string UnknownPackage = "AXC0139";
    public const string InvalidPackageRange = "AXC0140";
    public const string NoPackageVersionInRange = "AXC0141";
    public const string DuplicateApplicationPackage = "AXC0142";
    public const string UnknownTriggerEntity = "AXC0143";
    public const string TriggerEntityNotProcessSubject = "AXC0144";
    public const string InvalidTriggerField = "AXC0145";
    public const string PinnedPackageVersionMissing = "AXC0146";
    public const string UnknownStepPackage = "AXC0150";
    public const string UnknownPackageOperation = "AXC0151";

    public const string InvalidTriggerSchedule = "AXC0160";
    public const string UnknownTriggerTimeZone = "AXC0161";
    public const string UnknownTriggerDataSource = "AXC0162";
    public const string GroupedTriggerDataSource = "AXC0163";
    public const string TriggerDataSourceOverOtherEntity = "AXC0164";
    public const string UnknownTriggerParameter = "AXC0165";
    public const string InvalidTriggerParameterValue = "AXC0166";
    public const string MissingTriggerParameter = "AXC0167";

    public const string UnknownMigrationEntity = "AXC0170";
    public const string UnknownMigrationField = "AXC0171";
    public const string MigrationFieldAlreadyExists = "AXC0172";
    public const string MigrationFieldNotInEntity = "AXC0173";
    public const string ChangedMigrationHistory = "AXC0174";
    public const string FillLeftNulls = "AXC0176";
    public const string ConvertFailed = "AXC0177";
    public const string OldReleaseWorkOpen = "AXC0178";

    public const string UnknownScenarioRole = "AXC0180";
    public const string UnknownScenarioUser = "AXC0181";
    public const string UnknownScenarioEntity = "AXC0182";
    public const string UnknownScenarioField = "AXC0183";
    public const string UnknownScenarioProcess = "AXC0184";
    public const string UnknownScenarioStep = "AXC0185";
    public const string UnknownScenarioOutcome = "AXC0186";
    public const string UnsavedScenarioRecord = "AXC0187";
    public const string DuplicateScenarioRecord = "AXC0188";
    public const string InvalidScenarioValue = "AXC0189";
    public const string UnknownStubOperation = "AXC0190";
    public const string UnknownStubOutput = "AXC0191";
    public const string UnknownWidgetProcess = "AXC0192";
    public const string InvalidAdvanceTimeBy = "AXC0193";
    public const string AdvanceTimeWithoutNow = "AXC0194";

    public const string ModuleManifestInvalid = "AXC0200";
    public const string ModuleFolderMismatch = "AXC0201";
    public const string ModuleKindNotAllowed = "AXC0202";
    public const string UnknownModule = "AXC0203";
    public const string InvalidModuleRange = "AXC0204";
    public const string NoModuleVersionInRange = "AXC0205";
    public const string DuplicateApplicationModule = "AXC0206";
    public const string QualifiedModuleReference = "AXC0208";
    public const string ModuleTextKeyWithoutPrefix = "AXC0209";
    public const string MissingModuleLocale = "AXC0210";
    public const string ApplicationTextKeyWithModulePrefix = "AXC0211";
    public const string ReservedModulesFolder = "AXC0212";
    public const string NonAdditiveModuleUpgrade = "AXC0213";
    public const string MigrationOfModuleEntity = "AXC0214";
    public const string PublishClaimsExhausted = "AXC0215";
    public const string PreviewTenantMissing = "AXC0216";
    public const string ScenarioServerMissing = "AXC0217";
    public const string UnknownScenario = "AXC0218";
    public const string ScenarioTimeLimit = "AXC0219";
    public const string ScenarioStopped = "AXC0220";
    public const string TooDeepWidgetTree = "AXC0221";
    public const string InvalidColumnWidths = "AXC0222";
    public const string DuplicatePageTable = "AXC0223";
    public const string DuplicatePageRecordWidget = "AXC0224";
    public const string UnlistedFormConditionField = "AXC0225";
    public const string VisibleWhenOnRequiredField = "AXC0226";
    public const string DuplicateFormSectionName = "AXC0227";
    public const string UnknownStepSection = "AXC0228";
    public const string DuplicateStepSection = "AXC0229";
    public const string SectionInNoStep = "AXC0230";
    public const string InvalidPageParameterName = "AXC0231";
    public const string UnknownNavigatePage = "AXC0232";
    public const string UnknownNavigateParameter = "AXC0233";
    public const string MissingNavigateParameter = "AXC0234";
    public const string InvalidNavigateRecord = "AXC0235";
    public const string ParametersOnFormPage = "AXC0236";
    public const string KpiDataSourceNotTotal = "AXC0237";
    public const string UnknownKpiMeasure = "AXC0238";
    public const string InvalidDetailRecord = "AXC0239";
    public const string ChartDataSourceNotGrouped = "AXC0240";
    public const string InvalidChartMeasure = "AXC0241";
    public const string InvalidValueLabels = "AXC0242";
    public const string InvalidMoneyCurrency = "AXC0243";
    public const string UngroupedMoneyCurrency = "AXC0248";
    public const string InvalidFileMaxSize = "AXC0257";
    public const string InvalidFileAccept = "AXC0258";
    public const string FileFieldNotAllowed = "AXC0259";
    public const string ActionWithoutRequiredGrant = "AXC0265";
    public const string EmptyNotifyRecipients = "AXC0266";
    public const string InvalidNotifyRecipient = "AXC0267";
    public const string UnknownNotifyRole = "AXC0268";
    public const string EmptyNotifyChannels = "AXC0269";
    public const string RoleRecipientWithEmail = "AXC0270";
    public const string MissingPlaceholderValue = "AXC0271";
    public const string UnusedPlaceholderValue = "AXC0272";
    public const string InvalidPlaceholder = "AXC0273";
    public const string DuplicateIndex = "AXC0274";
    public const string UnknownIndexField = "AXC0275";
    public const string InvalidIndexField = "AXC0276";
    public const string UnknownApplicationTimeZone = "AXC0277";
}
