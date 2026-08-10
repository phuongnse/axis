using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.BusinessObjects.Application.Tests;

public sealed class BusinessObjectRecordValueValidatorTests
{
    [Fact]
    public void ValidateValues_WhenAllSupportedTypesUseValidLexemes_ReturnsCanonicalValues()
    {
        BusinessObjectDefinitionVersion definition = DefinitionWithAllTypes();
        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> result =
            BusinessObjectRecordValueValidator.ValidateAndCanonicalize(
                definition,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["description"] = ["Approved"],
                    ["quantity"] = ["0012"],
                    ["amount"] = ["0012.5000"],
                    ["due_date"] = ["2026-08-02"],
                    ["due_at"] = ["2026-08-02T10:00:00+07:00"],
                    ["active"] = ["TRUE"],
                    ["status"] = ["approved"],
                    ["labels"] = ["approved", "review"],
                });

        result.IsSuccess.Should().BeTrue();
        result.Value["description"].Should().Equal("Approved");
        result.Value["quantity"].Should().Equal("12");
        result.Value["amount"].Should().Equal("12.5");
        result.Value["due_date"].Should().Equal("2026-08-02");
        result.Value["due_at"].Should().Equal("2026-08-02T03:00:00.0000000Z");
        result.Value["active"].Should().Equal("true");
        result.Value["status"].Should().Equal("approved");
        result.Value["labels"].Should().Equal("approved", "review");
    }

    [Fact]
    public void ValidateValues_WhenChoiceValuesContainDuplicates_ReturnsFieldValidation()
    {
        BusinessObjectDefinitionVersion definition = DefinitionWithAllTypes();

        Result<IReadOnlyDictionary<string, IReadOnlyList<string>>> result =
            BusinessObjectRecordValueValidator.ValidateAndCanonicalize(
                definition,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["labels"] = ["approved", "approved"],
                });

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.FieldValidation);
        result.FieldErrors.Should().ContainKey("labels");
    }

    private static BusinessObjectDefinitionVersion DefinitionWithAllTypes()
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished(
            "All value types",
            "all_value_types");
        BusinessObjectChoiceFieldConfigurationSpec singleChoice = new(
            BusinessObjectChoiceSelectionMode.Single,
            [
                new("approved", "Approved", 0),
                new("rejected", "Rejected", 1),
            ]);
        BusinessObjectChoiceFieldConfigurationSpec multipleChoice = new(
            BusinessObjectChoiceSelectionMode.Multiple,
            [
                new("approved", "Approved", 0),
                new("review", "Review", 1),
            ]);
        Result saved = definition.SaveUnpublished(
            "All value types",
            [
                new("description", "Description", 0, BusinessObjectFieldType.Text),
                new("quantity", "Quantity", 1, BusinessObjectFieldType.Integer),
                new("amount", "Amount", 2, BusinessObjectFieldType.Decimal),
                new("due_date", "Due date", 3, BusinessObjectFieldType.Date),
                new("due_at", "Due at", 4, BusinessObjectFieldType.DateTime),
                new("active", "Active", 5, BusinessObjectFieldType.Boolean),
                new("status", "Status", 6, BusinessObjectFieldType.Choice, ChoiceConfiguration: singleChoice),
                new("labels", "Labels", 7, BusinessObjectFieldType.Choice, ChoiceConfiguration: multipleChoice),
            ],
            expectedRevision: 1,
            BusinessObjectRecordHandlerTestContext.Now);
        saved.IsSuccess.Should().BeTrue();
        definition.Publish(
            expectedRevision: 2,
            SubjectReference.Human(BusinessObjectRecordHandlerTestContext.UserId),
            BusinessObjectRecordHandlerTestContext.Now).IsSuccess.Should().BeTrue();
        return definition.Versions.Single();
    }
}
