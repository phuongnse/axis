using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Events;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Domain.Tests;

public class DataModelTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void Create_sets_name_description_icon_color_and_orgId()
    {
        var model = DataModel.Create("Invoice", "Invoicing model", "file-text", "#3B82F6", OrgId);

        model.Name.Should().Be("Invoice");
        model.Description.Should().Be("Invoicing model");
        model.Icon.Should().Be("file-text");
        model.Color.Should().Be("#3B82F6");
        model.OrganizationId.Should().Be(OrgId);
        model.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_generates_three_system_fields()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);

        model.Fields.Where(f => f.IsSystem).Should().HaveCount(3);
        model.Fields.Select(f => f.Name).Should().Contain(["id", "created_at", "updated_at"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]                                 // too short (< 2)
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // > 100
    public void Create_throws_when_name_invalid_length(string name)
    {
        var act = () => DataModel.Create(name, null, null, null, OrgId);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Invoice/v2")]
    [InlineData("Invoice\\v2")]
    [InlineData("Invoice<v2>")]
    [InlineData("Invoice\"v2")]
    [InlineData("Invoice;v2")]
    public void Create_throws_when_name_contains_special_chars(string name)
    {
        var act = () => DataModel.Create(name, null, null, null, OrgId);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_raises_ModelCreated_event()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.DomainEvents.Should().ContainSingle(e => e is ModelCreated);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_changes_metadata()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.Update("Tax Invoice", "Updated", "file", "#EF4444");

        model.Name.Should().Be("Tax Invoice");
        model.Description.Should().Be("Updated");
        model.Icon.Should().Be("file");
        model.Color.Should().Be("#EF4444");
    }

    [Fact]
    public void Update_throws_when_model_is_deleted()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.Delete();

        var act = () => model.Update("Tax Invoice", null, null, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*deleted*");
    }

    // ─── AddField ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddField_adds_text_field_with_correct_properties()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.AddField("amount", "Amount", FieldType.Text, required: true, new TextFieldConfig());

        var field = model.Fields.Single(f => f.Name == "amount");
        field.Label.Should().Be("Amount");
        field.Type.Should().Be(FieldType.Text);
        field.IsRequired.Should().BeTrue();
        field.IsSystem.Should().BeFalse();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("created_at")]
    [InlineData("updated_at")]
    public void AddField_throws_when_name_is_reserved(string name)
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var act = () => model.AddField(name, "Label", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    [Fact]
    public void AddField_throws_when_name_is_duplicate_case_insensitive()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.AddField("amount", "Amount", FieldType.Text, false, new TextFieldConfig());

        var act = () => model.AddField("Amount", "Amount 2", FieldType.Number, false, new NumberFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1starts_with_number")]
    [InlineData("has-hyphen")]
    [InlineData("has space")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65 chars
    public void AddField_throws_when_name_is_invalid_format(string name)
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var act = () => model.AddField(name, "Label", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddField_enum_requires_at_least_two_options()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var config = new EnumFieldConfig([new EnumOption("a", "A")], false);

        var act = () => model.AddField("status", "Status", FieldType.Enum, false, config);
        act.Should().Throw<ArgumentException>().WithMessage("*2 options*");
    }

    [Fact]
    public void AddField_enum_with_two_options_succeeds()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var config = new EnumFieldConfig(
            [new EnumOption("draft", "Draft"), new EnumOption("paid", "Paid")], false);

        model.AddField("status", "Status", FieldType.Enum, false, config);
        model.Fields.Should().Contain(f => f.Name == "status");
    }

    [Fact]
    public void AddField_throws_when_model_is_deleted()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.Delete();

        var act = () => model.AddField("amount", "Amount", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*deleted*");
    }

    // ─── RemoveField ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveField_removes_custom_field()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var field = model.AddField("amount", "Amount", FieldType.Text, false, new TextFieldConfig());

        model.RemoveField(field.Id);

        model.Fields.Should().NotContain(f => f.Name == "amount");
    }

    [Fact]
    public void RemoveField_throws_when_field_is_system()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var idField = model.Fields.Single(f => f.Name == "id");

        var act = () => model.RemoveField(idField.Id);
        act.Should().Throw<InvalidOperationException>().WithMessage("*system field*");
    }

    [Fact]
    public void RemoveField_throws_when_field_not_found()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var act = () => model.RemoveField(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    // ─── ReorderFields ────────────────────────────────────────────────────────

    [Fact]
    public void ReorderFields_changes_display_order_of_custom_fields()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        var f1 = model.AddField("amount", "Amount", FieldType.Number, false, new NumberFieldConfig());
        var f2 = model.AddField("due_date", "Due Date", FieldType.Date, false, new DateFieldConfig());
        var f3 = model.AddField("notes", "Notes", FieldType.Text, false, new TextFieldConfig());

        // Reverse the custom field order
        model.ReorderFields([f3.Id, f2.Id, f1.Id]);

        var customFields = model.Fields.Where(f => !f.IsSystem).OrderBy(f => f.DisplayOrder).ToList();
        customFields[0].Name.Should().Be("notes");
        customFields[1].Name.Should().Be("due_date");
        customFields[2].Name.Should().Be("amount");
    }

    [Fact]
    public void ReorderFields_throws_when_list_does_not_match_all_custom_fields()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.AddField("amount", "Amount", FieldType.Number, false, new NumberFieldConfig());
        model.AddField("due_date", "Due Date", FieldType.Date, false, new DateFieldConfig());

        var act = () => model.ReorderFields([Guid.NewGuid()]); // wrong ids
        act.Should().Throw<ArgumentException>();
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_sets_IsDeleted_and_raises_event()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.Delete();

        model.IsDeleted.Should().BeTrue();
        model.DomainEvents.Should().Contain(e => e is ModelDeleted);
    }

    [Fact]
    public void Delete_throws_when_already_deleted()
    {
        var model = DataModel.Create("Invoice", null, null, null, OrgId);
        model.Delete();

        var act = () => model.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
