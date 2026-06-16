using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Events;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Domain.Tests;

public class DataModelTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    // ─── Create ───────────────────────────────────────────────────────────────

    [Fact]
    public void DataModel_WhenCreated_SetsNameDescriptionIconColorAndTenantId()
    {
        DataModel model = DataModel.Create("Invoice", "Invoicing model", "file-text", "#3B82F6", TenantId, UserId);

        model.Name.Should().Be("Invoice");
        model.Description.Should().Be("Invoicing model");
        model.Icon.Should().Be("file-text");
        model.Color.Should().Be("#3B82F6");
        model.tenantId.Should().Be(TenantId);
        model.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void DataModel_WhenCreated_SetsCreatedByAndTimestamps()
    {
        DateTimeOffset before = DateTimeOffset.UtcNow;
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);

        model.CreatedBy.Should().Be(UserId);
        model.CreatedAt.Should().BeOnOrAfter(before);
        model.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void DataModel_WhenCreated_GeneratesThreeSystemFields()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);

        model.Fields.Where(f => f.IsSystem).Should().HaveCount(3);
        model.Fields.Select(f => f.Name).Should().Contain(["id", "created_at", "updated_at"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]                                 // too short (< 2)
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // > 100
    public void DataModel_WhenNameLengthIsInvalid_ThrowsArgumentException(string name)
    {
        Func<DataModel> act = () => DataModel.Create(name, null, null, null, TenantId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Invoice/v2")]
    [InlineData("Invoice\\v2")]
    [InlineData("Invoice<v2>")]
    [InlineData("Invoice\"v2")]
    [InlineData("Invoice;v2")]
    public void DataModel_WhenNameContainsSpecialChars_ThrowsArgumentException(string name)
    {
        Func<DataModel> act = () => DataModel.Create(name, null, null, null, TenantId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DataModel_WhenCreated_RaisesModelCreatedEvent()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.DomainEvents.Should().ContainSingle(e => e is ModelCreated);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void DataModel_WhenUpdated_ChangesMetadata()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.Update("Tax Invoice", "Updated", "file", "#EF4444");

        model.Name.Should().Be("Tax Invoice");
        model.Description.Should().Be("Updated");
        model.Icon.Should().Be("file");
        model.Color.Should().Be("#EF4444");
    }

    [Fact]
    public void Update_WhenModelIsDeleted_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.Delete();

        Action act = () => model.Update("Tax Invoice", null, null, null);
        act.Should().Throw<InvalidOperationException>().WithMessage("*deleted*");
    }

    // ─── AddField ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddField_WhenCustomFieldAdded_RaisesFieldAddedEvent()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.ClearDomainEvents();
        FieldDefinition field = model.AddField("amount", "Amount", FieldType.Text, required: true, new TextFieldConfig());

        model.DomainEvents.Should().ContainSingle(e => e is FieldAdded);
        FieldAdded added = model.DomainEvents.OfType<FieldAdded>().Single();
        added.FieldId.Should().Be(field.Id);
        added.FieldName.Should().Be("amount");
    }

    [Fact]
    public void AddField_WhenFieldIsTextField_AddsWithCorrectProperties()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.AddField("amount", "Amount", FieldType.Text, required: true, new TextFieldConfig());
        FieldDefinition field = model.Fields.Single(f => f.Name == "amount");
        field.Label.Should().Be("Amount");
        field.Type.Should().Be(FieldType.Text);
        field.IsRequired.Should().BeTrue();
        field.IsSystem.Should().BeFalse();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("created_at")]
    [InlineData("updated_at")]
    public void AddField_WhenNameIsReserved_Throws(string name)
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        Func<FieldDefinition> act = () => model.AddField(name, "Label", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*reserved*");
    }

    [Fact]
    public void AddField_WhenNameIsDuplicateCaseInsensitive_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.AddField("amount", "Amount", FieldType.Text, false, new TextFieldConfig());

        Func<FieldDefinition> act = () => model.AddField("Amount", "Amount 2", FieldType.Number, false, new NumberFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1starts_with_number")]
    [InlineData("has-hyphen")]
    [InlineData("has space")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // 65 chars
    public void AddField_WhenNameFormatIsInvalid_Throws(string name)
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        Func<FieldDefinition> act = () => model.AddField(name, "Label", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddField_WhenEnumHasFewerThanTwoOptions_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        EnumFieldConfig config = new EnumFieldConfig([new EnumOption("a", "A")], false);

        Func<FieldDefinition> act = () => model.AddField("status", "Status", FieldType.Enum, false, config);
        act.Should().Throw<ArgumentException>().WithMessage("*2 options*");
    }

    [Fact]
    public void AddField_WhenEnumHasTwoOptions_AddsFieldSuccessfully()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        EnumFieldConfig config = new EnumFieldConfig(
                    [new EnumOption("draft", "Draft"), new EnumOption("paid", "Paid")], false);

        model.AddField("status", "Status", FieldType.Enum, false, config);
        model.Fields.Should().Contain(f => f.Name == "status");
    }

    [Fact]
    public void AddField_WhenModelIsDeleted_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.Delete();

        Func<FieldDefinition> act = () => model.AddField("amount", "Amount", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*deleted*");
    }

    // ─── UpdateField ──────────────────────────────────────────────────────────

    [Fact]
    public void UpdateField_WhenFieldUpdated_RaisesFieldUpdatedEvent()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        FieldDefinition field = model.AddField("amount", "Amount", FieldType.Text, false, new TextFieldConfig());
        model.ClearDomainEvents();

        model.UpdateField(field.Id, "Total Amount", null, true, new TextFieldConfig(MaxLength: 50));

        model.DomainEvents.Should().ContainSingle(e => e is FieldUpdated);
    }

    // ─── RemoveField ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveField_WhenFieldIsCustom_RemovesFieldAndRaisesFieldRemovedEvent()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        FieldDefinition field = model.AddField("amount", "Amount", FieldType.Text, false, new TextFieldConfig());
        model.ClearDomainEvents();

        model.RemoveField(field.Id);

        model.Fields.Should().NotContain(f => f.Name == "amount");
        model.DomainEvents.Should().ContainSingle(e => e is FieldRemoved);
    }

    [Fact]
    public void RemoveField_WhenFieldIsSystem_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        FieldDefinition idField = model.Fields.Single(f => f.Name == "id");

        Action act = () => model.RemoveField(idField.Id);
        act.Should().Throw<InvalidOperationException>().WithMessage("*system field*");
    }

    [Fact]
    public void RemoveField_WhenFieldNotFound_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        Action act = () => model.RemoveField(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    // ─── ReorderFields ────────────────────────────────────────────────────────

    [Fact]
    public void ReorderFields_WhenValidOrderProvided_ChangesDisplayOrderOfCustomFields()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        FieldDefinition f1 = model.AddField("amount", "Amount", FieldType.Number, false, new NumberFieldConfig());
        FieldDefinition f2 = model.AddField("due_date", "Due Date", FieldType.Date, false, new DateFieldConfig());
        FieldDefinition f3 = model.AddField("notes", "Notes", FieldType.Text, false, new TextFieldConfig());

        // Reverse the custom field order
        model.ReorderFields([f3.Id, f2.Id, f1.Id]);
        List<FieldDefinition> customFields = model.Fields.Where(f => !f.IsSystem).OrderBy(f => f.DisplayOrder).ToList();
        customFields[0].Name.Should().Be("notes");
        customFields[1].Name.Should().Be("due_date");
        customFields[2].Name.Should().Be("amount");
    }

    [Fact]
    public void ReorderFields_WhenListDoesNotMatchAllCustomFields_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.AddField("amount", "Amount", FieldType.Number, false, new NumberFieldConfig());
        model.AddField("due_date", "Due Date", FieldType.Date, false, new DateFieldConfig());

        Action act = () => model.ReorderFields([Guid.NewGuid()]); // wrong ids
        act.Should().Throw<ArgumentException>();
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_WhenCalled_SetsDeletedAtAndRaisesEvent()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        DateTimeOffset before = DateTimeOffset.UtcNow;
        model.Delete();

        model.DeletedAt.Should().NotBeNull();
        model.DeletedAt!.Value.Should().BeOnOrAfter(before);
        model.DomainEvents.Should().Contain(e => e is ModelDeleted);
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_Throws()
    {
        DataModel model = DataModel.Create("Invoice", null, null, null, TenantId, UserId);
        model.Delete();

        Action act = () => model.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
