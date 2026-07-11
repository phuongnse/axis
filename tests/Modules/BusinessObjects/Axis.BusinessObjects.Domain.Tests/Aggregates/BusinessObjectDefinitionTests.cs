using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.BusinessObjects.Domain.Tests.Aggregates;

public sealed class BusinessObjectDefinitionTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime Now = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateUnpublished_WhenDefinitionIdentityIsValid_CreatesEditableUnpublishedDefinitionAtRevisionOne()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        definition.WorkspaceId.Should().Be(WorkspaceId);
        definition.Name.Should().Be("Customer");
        definition.Key.Value.Should().Be("customer");
        definition.Status.Should().Be(BusinessObjectDefinitionStatus.Unpublished);
        definition.Revision.Should().Be(1);
        definition.Fields.Should().BeEmpty();
    }

    [Fact]
    public void SaveUnpublished_WhenFieldDefinitionsAreValid_PreservesStableFieldsAndIncrementsRevision()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer Account",
            ValidFields(),
            expectedRevision: 1,
            Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        definition.Name.Should().Be("Customer Account");
        definition.Key.Value.Should().Be("customer");
        definition.Revision.Should().Be(2);
        definition.Fields.Select(field => field.Key.Value)
            .Should().Equal("name", "credit_limit", "opened_on");
    }

    [Fact]
    public void SaveUnpublished_WhenPersistedIdentitiesRoundTrip_PreservesFieldOptionAndRuleIdentities()
    {
        BusinessObjectDefinition definition = CreateUnpublished();
        definition.SaveUnpublished(
            "Customer",
            [
                Field(
                    "status",
                    "Status",
                    order: 0,
                    BusinessObjectFieldType.Choice,
                    [Rule(RuleDefinitionKeys.Required)],
                    Choice(BusinessObjectChoiceSelectionMode.Single, ("active", "Active"))),
            ],
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();

        BusinessObjectFieldDefinition existingField = definition.Fields.Single();
        BusinessObjectChoiceOption existingOption = existingField.ChoiceOptions.Single();
        BusinessObjectFieldRule existingRule = existingField.Rules.Single();

        Result result = definition.SaveUnpublished(
            "Customer",
            [
                Field(
                    "status",
                    "Customer status",
                    order: 0,
                    BusinessObjectFieldType.Choice,
                    [Rule(RuleDefinitionKeys.Required) with { Id = existingRule.Id }],
                    Choice(BusinessObjectChoiceSelectionMode.Single, ("active", "Enabled")) with
                    {
                        Options =
                        [
                            new BusinessObjectChoiceOptionSpec(
                                "active",
                                "Enabled",
                                0,
                                existingOption.Id),
                        ],
                    }) with
                {
                    Id = existingField.Id,
                },
            ],
            expectedRevision: 2,
            Now.AddMinutes(1));

        result.IsSuccess.Should().BeTrue();
        BusinessObjectFieldDefinition savedField = definition.Fields.Single();
        savedField.Id.Should().Be(existingField.Id);
        savedField.Key.Value.Should().Be("status");
        savedField.Label.Should().Be("Customer status");
        savedField.ChoiceOptions.Single().Id.Should().Be(existingOption.Id);
        savedField.ChoiceOptions.Single().Label.Should().Be("Enabled");
        savedField.Rules.Single().Id.Should().Be(existingRule.Id);
    }

    [Fact]
    public void SaveUnpublished_WhenPersistedFieldKeyChanges_ReturnsInvalidInputWithoutMutation()
    {
        BusinessObjectDefinition definition = CreateUnpublished();
        definition.SaveUnpublished(
            "Customer",
            [Field("name", "Name", order: 0)],
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();
        BusinessObjectFieldDefinition field = definition.Fields.Single();

        Result result = definition.SaveUnpublished(
            "Customer",
            [Field("display_name", "Name", order: 0) with { Id = field.Id }],
            expectedRevision: 2,
            Now.AddMinutes(1));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        definition.Revision.Should().Be(2);
        definition.Fields.Single().Key.Value.Should().Be("name");
    }

    [Fact]
    public void SaveUnpublished_WhenFieldRulesAreValid_PreservesAppliedRuleSnapshots()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [
                Field(
                    "name",
                    "Name",
                    order: 0,
                    BusinessObjectFieldType.Text,
                    [
                        Rule(RuleDefinitionKeys.Required),
                        Rule(RuleDefinitionKeys.TextLength, Params(("max", ["80"]))),
                    ]),
                Field(
                    "status",
                    "Status",
                    order: 1,
                    BusinessObjectFieldType.Choice,
                    choiceConfiguration: Choice(
                        BusinessObjectChoiceSelectionMode.Single,
                        ("draft", "Draft"),
                        ("submitted", "Submitted"),
                        ("approved", "Approved"))),
            ],
            expectedRevision: 1,
            Now);

        result.IsSuccess.Should().BeTrue();
        BusinessObjectFieldDefinition name = definition.Fields.Single(field => field.Key.Value == "name");
        name.Rules.Select(rule => rule.DefinitionKey)
            .Should().Equal(RuleDefinitionKeys.Required, RuleDefinitionKeys.TextLength);
        name.Rules.Single(rule => rule.DefinitionKey == RuleDefinitionKeys.TextLength)
            .Parameters["max"].Should().Equal("80");
        BusinessObjectFieldDefinition choice = definition.Fields.Single(field => field.Key.Value == "status");
        choice.FieldType.Should().Be(BusinessObjectFieldType.Choice);
        choice.ChoiceSelectionMode.Should().Be(BusinessObjectChoiceSelectionMode.Single);
        choice.ChoiceOptions.Select(option => (option.Key.Value, option.Label))
            .Should().Equal(("draft", "Draft"), ("submitted", "Submitted"), ("approved", "Approved"));
    }

    [Fact]
    public void SaveUnpublished_WhenFieldKeyAlreadyExists_ReturnsInvalidInputWithoutChangingUnpublishedDefinition()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [
                Field("name", "Name", order: 0),
                Field("name", "Display name", order: 1),
            ],
            expectedRevision: 1,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        definition.Revision.Should().Be(1);
        definition.Fields.Should().BeEmpty();
    }

    [Fact]
    public void SaveUnpublished_WhenExpectedRevisionIsStale_ReturnsConflictWithoutOverwrite()
    {
        BusinessObjectDefinition definition = CreateUnpublished();
        definition.SaveUnpublished(
            "Customer",
            [Field("name", "Name", order: 0)],
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();

        Result stale = definition.SaveUnpublished(
            "Customer stale",
            [Field("new_name", "New name", order: 0)],
            expectedRevision: 1,
            Now);

        stale.IsFailure.Should().BeTrue();
        stale.ErrorCode.Should().Be(ErrorCodes.Conflict);
        definition.Name.Should().Be("Customer");
        definition.Fields.Should().ContainSingle(field => field.Key.Value == "name");
    }

    [Fact]
    public void SaveUnpublished_WhenFieldLabelIsMissing_ReturnsInvalidInput()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [Field("name", " ", order: 0)],
            expectedRevision: 1,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
    }

    [Fact]
    public void SaveUnpublished_WhenFieldTypeIsUnsupported_ReturnsInvalidInput()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [Field("name", "Name", order: 0, (BusinessObjectFieldType)999)],
            expectedRevision: 1,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        result.Error.Should().Be("Field type is not supported.");
    }

    [Fact]
    public void SaveUnpublished_WhenAppliedRuleKeyIsDuplicated_ReturnsInvalidInput()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [
                Field(
                    "name",
                    "Name",
                    order: 0,
                    BusinessObjectFieldType.Text,
                    [
                        Rule(RuleDefinitionKeys.Required),
                        Rule(RuleDefinitionKeys.Required),
                    ]),
            ],
            expectedRevision: 1,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        result.Error.Should().Be("Field rules must be unique per field.");
    }

    [Fact]
    public void SaveUnpublished_WhenFieldRuleShapeIsInvalid_ReturnsInvalidInput()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result result = definition.SaveUnpublished(
            "Customer",
            [
                Field(
                    "name",
                    "Name",
                    order: 0,
                    BusinessObjectFieldType.Text,
                    [Rule("Field.Required")]),
            ],
            expectedRevision: 1,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
        result.Error.Should().Be("Field rule definition key format is invalid.");
    }

    [Fact]
    public void Publish_WhenUnpublishedDefinitionIsValid_CreatesImmutableVersionOneSnapshot()
    {
        BusinessObjectDefinition definition = CreateUnpublished();
        definition.SaveUnpublished(
            "Customer",
            ValidFields(),
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();

        Result<BusinessObjectDefinitionVersion> result = definition.Publish(
            expectedRevision: 2,
            UserId,
            Now.AddMinutes(2));

        result.IsSuccess.Should().BeTrue();
        BusinessObjectDefinitionVersion version = result.Value;
        version.VersionNumber.Should().Be(1);
        version.Name.Should().Be("Customer");
        version.Key.Value.Should().Be("customer");
        version.PublishedByUserId.Should().Be(UserId);
        version.PublishedAt.Should().Be(Now.AddMinutes(2));
        version.Fields.Select(field => field.Key.Value)
            .Should().Equal("name", "credit_limit", "opened_on");
        version.SourceDefinitionId.Should().Be(definition.Id);
        version.Fields.Should().OnlyContain(field =>
            definition.Fields.Any(source =>
                source.Id == field.SourceFieldDefinitionId &&
                source.Id.Value != field.Id.Value));
        definition.Status.Should().Be(BusinessObjectDefinitionStatus.Published);
        definition.LatestPublishedVersionNumber.Should().Be(1);
    }

    [Fact]
    public void Publish_WhenFieldRulesAreConfigured_PreservesImmutableRuleSnapshot()
    {
        BusinessObjectDefinition definition = CreateUnpublished();
        definition.SaveUnpublished(
            "Customer",
            [
                Field(
                    "status",
                    "Status",
                    order: 0,
                    BusinessObjectFieldType.Choice,
                    choiceConfiguration: Choice(
                        BusinessObjectChoiceSelectionMode.Single,
                        ("draft", "Draft"),
                        ("submitted", "Submitted"),
                        ("approved", "Approved"))),
            ],
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();

        Result<BusinessObjectDefinitionVersion> result = definition.Publish(
            expectedRevision: 2,
            UserId,
            Now.AddMinutes(2));

        result.IsSuccess.Should().BeTrue();
        BusinessObjectDefinitionVersionField field = result.Value.Fields.Single();
        field.FieldType.Should().Be(BusinessObjectFieldType.Choice);
        field.ChoiceSelectionMode.Should().Be(BusinessObjectChoiceSelectionMode.Single);
        field.ChoiceOptions.Select(option => (option.Key.Value, option.Label))
            .Should().Equal(("draft", "Draft"), ("submitted", "Submitted"), ("approved", "Approved"));
        BusinessObjectFieldDefinition sourceField = definition.Fields.Single();
        field.SourceFieldDefinitionId.Should().Be(sourceField.Id);
        field.ChoiceOptions.Select(option => option.SourceChoiceOptionId)
            .Should().Equal(sourceField.ChoiceOptions.Select(option => option.Id));
        field.ChoiceOptions.Should().OnlyContain(option =>
            option.Id.Value != option.SourceChoiceOptionId.Value);
    }

    [Fact]
    public void Publish_WhenUnpublishedDefinitionHasNoFields_ReturnsInvalidInput()
    {
        BusinessObjectDefinition definition = CreateUnpublished();

        Result<BusinessObjectDefinitionVersion> result = definition.Publish(
            expectedRevision: 1,
            UserId,
            Now);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.InvalidInput);
    }

    private static BusinessObjectDefinition CreateUnpublished()
    {
        Result<BusinessObjectDefinition> result = BusinessObjectDefinition.CreateUnpublished(
            WorkspaceId,
            "Customer",
            BusinessObjectDefinitionKey.Create("customer").Value,
            Now);

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static BusinessObjectFieldDefinitionSpec Field(
        string key,
        string label,
        int order,
        BusinessObjectFieldType fieldType = BusinessObjectFieldType.Text,
        IReadOnlyList<BusinessObjectFieldRuleSpec>? rules = null,
        BusinessObjectChoiceFieldConfigurationSpec? choiceConfiguration = null) =>
        new(key, label, order, fieldType, rules, choiceConfiguration);

    private static BusinessObjectFieldRuleSpec Rule(
        string definitionKey,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parameters = null) =>
        new(definitionKey, DefinitionVersion: 1, parameters);

    private static BusinessObjectChoiceFieldConfigurationSpec Choice(
        BusinessObjectChoiceSelectionMode selectionMode,
        params (string Key, string Label)[] options) =>
        new(
            selectionMode,
            options.Select((option, index) => new BusinessObjectChoiceOptionSpec(
                option.Key,
                option.Label,
                index)).ToArray());

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Params(
        params (string Key, string[] Values)[] parameters) =>
        parameters.ToDictionary(
            parameter => parameter.Key,
            parameter => (IReadOnlyList<string>)parameter.Values,
            StringComparer.Ordinal);

    private static IReadOnlyList<BusinessObjectFieldDefinitionSpec> ValidFields() =>
    [
        Field("name", "Name", order: 0),
        Field("credit_limit", "Credit limit", order: 1),
        Field("opened_on", "Opened on", order: 2),
    ];
}
