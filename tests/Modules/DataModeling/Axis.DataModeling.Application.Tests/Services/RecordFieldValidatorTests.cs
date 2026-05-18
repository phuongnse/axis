using Axis.DataModeling.Application.Services;
using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Application.Tests.Services;

public class RecordFieldValidatorTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-1";

    private static DataModel ModelWith(Action<DataModel> configure)
    {
        DataModel model = DataModel.Create("Test", null, null, null, OrgId, UserId);
        configure(model);
        return model;
    }

    // ─── Required ──────────────────────────────────────────────────────────

    [Fact]
    public void Validate_WhenRequiredFieldMissing_ReturnsError()
    {
        DataModel model = ModelWith(m => m.AddField("title", "Title", FieldType.Text, required: true, new TextFieldConfig()));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(new Dictionary<string, object?>(), model.Fields);
        errors.Should().ContainKey("title");
    }

    [Fact]
    public void Validate_WhenRequiredFieldNull_ReturnsError()
    {
        DataModel model = ModelWith(m => m.AddField("title", "Title", FieldType.Text, required: true, new TextFieldConfig()));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["title"] = null }, model.Fields);
        errors.Should().ContainKey("title");
    }

    [Fact]
    public void Validate_WhenRequiredFieldPresent_NoError()
    {
        DataModel model = ModelWith(m => m.AddField("title", "Title", FieldType.Text, required: true, new TextFieldConfig()));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["title"] = "Hello" }, model.Fields);
        errors.Should().NotContainKey("title");
    }

    [Fact]
    public void Validate_SystemFieldsAreSkipped()
    {
        // id, created_at, updated_at are system fields — never validated against required rule
        DataModel model = DataModel.Create("Test", null, null, null, OrgId, UserId);
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(new Dictionary<string, object?>(), model.Fields);
        errors.Should().BeEmpty();
    }

    // ─── Text constraints ──────────────────────────────────────────────────

    [Fact]
    public void Validate_WhenTextTooShort_ReturnsError()
    {
        DataModel model = ModelWith(m => m.AddField("code", "Code", FieldType.Text, required: false, new TextFieldConfig(MinLength: 3)));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["code"] = "ab" }, model.Fields);
        errors.Should().ContainKey("code");
    }

    [Fact]
    public void Validate_WhenTextTooLong_ReturnsError()
    {
        DataModel model = ModelWith(m => m.AddField("code", "Code", FieldType.Text, required: false, new TextFieldConfig(MaxLength: 5)));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["code"] = "toolongvalue" }, model.Fields);
        errors.Should().ContainKey("code");
    }

    [Fact]
    public void Validate_WhenTextWithinBounds_NoError()
    {
        DataModel model = ModelWith(m => m.AddField("code", "Code", FieldType.Text, required: false, new TextFieldConfig(MinLength: 2, MaxLength: 10)));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["code"] = "abc" }, model.Fields);
        errors.Should().NotContainKey("code");
    }

    // ─── Number constraints ────────────────────────────────────────────────

    [Fact]
    public void Validate_WhenNumberBelowMin_ReturnsError()
    {
        DataModel model = ModelWith(m => m.AddField("qty", "Qty", FieldType.Number, required: false, new NumberFieldConfig(Min: 1)));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["qty"] = 0.5 }, model.Fields);
        errors.Should().ContainKey("qty");
    }

    [Fact]
    public void Validate_WhenNumberAboveMax_ReturnsError()
    {
        DataModel model = ModelWith(m => m.AddField("qty", "Qty", FieldType.Number, required: false, new NumberFieldConfig(Max: 100)));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["qty"] = 200.0 }, model.Fields);
        errors.Should().ContainKey("qty");
    }

    [Fact]
    public void Validate_WhenNumberWithinBounds_NoError()
    {
        DataModel model = ModelWith(m => m.AddField("qty", "Qty", FieldType.Number, required: false, new NumberFieldConfig(Min: 1, Max: 100)));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["qty"] = 50.0 }, model.Fields);
        errors.Should().NotContainKey("qty");
    }

    // ─── Enum constraints ──────────────────────────────────────────────────

    [Fact]
    public void Validate_WhenEnumValueNotInOptions_ReturnsError()
    {
        EnumFieldConfig config = new(new[] { new EnumOption("active", "Active"), new EnumOption("inactive", "Inactive") });
        DataModel model = ModelWith(m => m.AddField("status", "Status", FieldType.Enum, required: false, config));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["status"] = "unknown" }, model.Fields);
        errors.Should().ContainKey("status");
    }

    [Fact]
    public void Validate_WhenEnumValueInOptions_NoError()
    {
        EnumFieldConfig config = new(new[] { new EnumOption("active", "Active"), new EnumOption("inactive", "Inactive") });
        DataModel model = ModelWith(m => m.AddField("status", "Status", FieldType.Enum, required: false, config));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["status"] = "active" }, model.Fields);
        errors.Should().NotContainKey("status");
    }

    [Fact]
    public void Validate_WhenNumberFieldReceivesNonNumericString_ReturnsError()
    {
        DataModel model = ModelWith(m => m.AddField("qty", "Qty", FieldType.Number, required: false, new NumberFieldConfig()));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(
            new Dictionary<string, object?> { ["qty"] = "abc" }, model.Fields);
        errors.Should().ContainKey("qty");
        errors["qty"].Should().Contain(e => e.Contains("valid number"));
    }

    // ─── Optional field absent — no error ──────────────────────────────────

    [Fact]
    public void Validate_WhenOptionalFieldAbsent_NoError()
    {
        DataModel model = ModelWith(m => m.AddField("notes", "Notes", FieldType.Text, required: false, new TextFieldConfig()));
        Dictionary<string, string[]> errors = RecordFieldValidator.Validate(new Dictionary<string, object?>(), model.Fields);
        errors.Should().NotContainKey("notes");
    }
}
