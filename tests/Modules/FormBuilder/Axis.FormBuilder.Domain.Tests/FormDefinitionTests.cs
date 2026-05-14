using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.Events;
using Axis.FormBuilder.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.FormBuilder.Domain.Tests;

public class FormDefinitionTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void FormDefinition_WhenCreated_SetsNameDescriptionAndOrgId()
    {
        var form = FormDefinition.Create("Employee Intake", "New hire form", OrgId, UserId);

        form.Name.Should().Be("Employee Intake");
        form.Description.Should().Be("New hire form");
        form.OrganizationId.Should().Be(OrgId);
        form.DeletedAt.Should().BeNull();
        form.Fields.Should().BeEmpty();
    }

    [Fact]
    public void FormDefinition_WhenCreated_SetsCreatedByAndTimestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);

        form.CreatedBy.Should().Be(UserId);
        form.CreatedAt.Should().BeOnOrAfter(before);
        form.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void FormDefinition_WhenCreated_RaisesFormCreatedEvent()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        form.DomainEvents.Should().ContainSingle(e => e is FormCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]  // too short
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // > 200
    public void FormDefinition_WhenNameLengthIsInvalid_ThrowsArgumentException(string name)
    {
        var act = () => FormDefinition.Create(name, null, OrgId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    // ─── AddField ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddField_WhenFieldIsTextField_AddsFieldSuccessfully()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var field = form.AddField("first_name", "First Name", FormFieldType.Text, required: true, null);

        form.Fields.Should().ContainSingle();
        field.Key.Should().Be("first_name");
        field.Label.Should().Be("First Name");
        field.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void AddField_WhenKeyIsDuplicate_Throws()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        form.AddField("first_name", "First Name", FormFieldType.Text, false, null);

        var act = () => form.AddField("first_name", "First Name 2", FormFieldType.Text, false, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1starts_with_number")]
    [InlineData("has-hyphen")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65 chars
    public void AddField_WhenKeyFormatIsInvalid_Throws(string key)
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var act = () => form.AddField(key, "Label", FormFieldType.Text, false, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddField_WhenDropdownHasFewerThanTwoOptions_Throws()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var config = new DropdownFieldConfig([new DropdownOption("a", "A")]);

        var act = () => form.AddField("dept", "Department", FormFieldType.Dropdown, false, config);
        act.Should().Throw<ArgumentException>().WithMessage("*2 options*");
    }

    [Fact]
    public void AddField_WhenDropdownHasTwoOptions_AddsFieldSuccessfully()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var config = new DropdownFieldConfig(
            [new DropdownOption("eng", "Engineering"), new DropdownOption("hr", "HR")]);

        form.AddField("dept", "Department", FormFieldType.Dropdown, false, config);
        form.Fields.Should().ContainSingle(f => f.Key == "dept");
    }

    // ─── RemoveField ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveField_WhenFieldExists_RemovesField()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var field = form.AddField("first_name", "First Name", FormFieldType.Text, false, null);

        form.RemoveField(field.Id);

        form.Fields.Should().BeEmpty();
    }

    [Fact]
    public void RemoveField_WhenFieldNotFound_Throws()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var act = () => form.RemoveField(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    // ─── ReorderFields ────────────────────────────────────────────────────────

    [Fact]
    public void ReorderFields_WhenValidOrderProvided_UpdatesDisplayOrder()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var f1 = form.AddField("first_name", "First Name", FormFieldType.Text, false, null);
        var f2 = form.AddField("last_name", "Last Name", FormFieldType.Text, false, null);
        var f3 = form.AddField("email", "Email", FormFieldType.Text, false, null);

        form.ReorderFields([f3.Id, f1.Id, f2.Id]);

        var ordered = form.Fields.OrderBy(f => f.DisplayOrder).ToList();
        ordered[0].Key.Should().Be("email");
        ordered[1].Key.Should().Be("first_name");
        ordered[2].Key.Should().Be("last_name");
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_WhenCalled_SetsDeletedAtAndRaisesEvent()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        var before = DateTimeOffset.UtcNow;
        form.Delete();

        form.DeletedAt.Should().NotBeNull();
        form.DeletedAt!.Value.Should().BeOnOrAfter(before);
        form.DomainEvents.Should().Contain(e => e is FormDeleted);
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_Throws()
    {
        var form = FormDefinition.Create("Employee Intake", null, OrgId, UserId);
        form.Delete();

        var act = () => form.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
