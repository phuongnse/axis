using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.FormBuilder.Application.Tests.Services;

public class FormSubmissionFieldValidatorTests
{
    private static readonly Guid TeamAccountId = Guid.NewGuid();

    [Fact]
    public void Validate_WhenRequiredFieldMissing_ReturnsError()
    {
        FormDefinition form = FormDefinition.Create("Test Form", null, TeamAccountId, "user");
        form.AddField("email", "Email", FormFieldType.Text, true, null);

        Dictionary<string, string[]> errors = FormSubmissionFieldValidator.Validate(
            new Dictionary<string, object?>(),
            form.Fields);

        errors.Should().ContainKey("email");
        errors["email"].Should().Contain(e => e.Contains("required"));
    }

    [Fact]
    public void Validate_WhenTextExceedsMaxLength_ReturnsError()
    {
        FormDefinition form = FormDefinition.Create("Test Form", null, TeamAccountId, "user");
        form.AddField("code", "Code", FormFieldType.Text, false, new TextFormFieldConfig(MaxLength: 3));

        Dictionary<string, string[]> errors = FormSubmissionFieldValidator.Validate(
            new Dictionary<string, object?> { ["code"] = "abcd" },
            form.Fields);

        errors.Should().ContainKey("code");
    }

    [Fact]
    public void Validate_WhenSectionField_SkipsValidation()
    {
        FormDefinition form = FormDefinition.Create("Test Form", null, TeamAccountId, "user");
        form.AddField("hdr", "Header", FormFieldType.Section, false, new SectionFieldConfig());

        Dictionary<string, string[]> errors = FormSubmissionFieldValidator.Validate(
            new Dictionary<string, object?>(),
            form.Fields);

        errors.Should().BeEmpty();
    }
}
