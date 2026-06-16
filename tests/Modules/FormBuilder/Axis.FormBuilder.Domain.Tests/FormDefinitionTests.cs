using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Entities;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.Events;
using Axis.FormBuilder.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.FormBuilder.Domain.Tests;

public class FormDefinitionTests
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private const string UserId = "user-123";

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void FormDefinition_WhenCreated_SetsNameDescriptionAndWorkspaceId()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", "New hire form", WorkspaceId, UserId);

        form.Name.Should().Be("Employee Intake");
        form.Description.Should().Be("New hire form");
        form.workspaceId.Should().Be(WorkspaceId);
        form.DeletedAt.Should().BeNull();
        form.Fields.Should().BeEmpty();
    }

    [Fact]
    public void FormDefinition_WhenCreated_SetsCreatedByAndTimestamps()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);

        form.CreatedBy.Should().Be(UserId);
        form.CreatedAt.Should().BeOnOrAfter(before);
        form.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void FormDefinition_WhenCreated_RaisesFormCreatedEvent()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        form.DomainEvents.Should().ContainSingle(e => e is FormCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]  // too short
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // > 200
    public void FormDefinition_WhenNameLengthIsInvalid_ThrowsArgumentException(string name)
    {
        Func<FormDefinition> act = () => FormDefinition.Create(name, null, WorkspaceId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    // ─── AddField ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddField_WhenFieldIsTextField_AddsFieldSuccessfully()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        FormField field = form.AddField("first_name", "First Name", FormFieldType.Text, required: true, null);

        form.Fields.Should().ContainSingle();
        field.Key.Should().Be("first_name");
        field.Label.Should().Be("First Name");
        field.IsRequired.Should().BeTrue();
    }

    [Fact]
    public void AddField_WhenKeyIsDuplicate_Throws()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        form.AddField("first_name", "First Name", FormFieldType.Text, false, null);

        Func<Entities.FormField> act = () => form.AddField("first_name", "First Name 2", FormFieldType.Text, false, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1starts_with_number")]
    [InlineData("has-hyphen")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65 chars
    public void AddField_WhenKeyFormatIsInvalid_Throws(string key)
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        Func<Entities.FormField> act = () => form.AddField(key, "Label", FormFieldType.Text, false, null);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddField_WhenDropdownHasFewerThanTwoOptions_Throws()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        DropdownFieldConfig config = new DropdownFieldConfig([new DropdownOption("a", "A")]);

        Func<Entities.FormField> act = () => form.AddField("dept", "Department", FormFieldType.Dropdown, false, config);
        act.Should().Throw<ArgumentException>().WithMessage("*2 options*");
    }

    [Fact]
    public void AddField_WhenDropdownHasTwoOptions_AddsFieldSuccessfully()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        DropdownFieldConfig config = new DropdownFieldConfig(
                    [new DropdownOption("eng", "Engineering"), new DropdownOption("hr", "HR")]);

        form.AddField("dept", "Department", FormFieldType.Dropdown, false, config);
        form.Fields.Should().ContainSingle(f => f.Key == "dept");
    }

    // ─── RemoveField ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveField_WhenFieldExists_RemovesField()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        FormField field = form.AddField("first_name", "First Name", FormFieldType.Text, false, null);

        form.RemoveField(field.Id);

        form.Fields.Should().BeEmpty();
    }

    [Fact]
    public void RemoveField_WhenFieldNotFound_Throws()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        Action act = () => form.RemoveField(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    // ─── ReorderFields ────────────────────────────────────────────────────────

    [Fact]
    public void ReorderFields_WhenValidOrderProvided_UpdatesDisplayOrder()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        FormField f1 = form.AddField("first_name", "First Name", FormFieldType.Text, false, null);
        FormField f2 = form.AddField("last_name", "Last Name", FormFieldType.Text, false, null);
        FormField f3 = form.AddField("email", "Email", FormFieldType.Text, false, null);

        form.ReorderFields([f3.Id, f1.Id, f2.Id]);
        List<FormField> ordered = form.Fields.OrderBy(f => f.DisplayOrder).ToList();
        ordered[0].Key.Should().Be("email");
        ordered[1].Key.Should().Be("first_name");
        ordered[2].Key.Should().Be("last_name");
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_WhenCalled_SetsDeletedAtAndRaisesEvent()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        DateTimeOffset before = DateTimeOffset.UtcNow;
        form.Delete();

        form.DeletedAt.Should().NotBeNull();
        form.DeletedAt!.Value.Should().BeOnOrAfter(before);
        form.DomainEvents.Should().Contain(e => e is FormDeleted);
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_Throws()
    {
        FormDefinition form = FormDefinition.Create("Employee Intake", null, WorkspaceId, UserId);
        form.Delete();

        Action act = () => form.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
