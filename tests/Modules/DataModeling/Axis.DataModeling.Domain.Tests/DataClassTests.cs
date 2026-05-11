using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Events;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Domain.Tests;

public class DataClassTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    [Fact]
    public void Create_sets_name_description_and_orgId()
    {
        var dc = DataClass.Create("Address", "Postal address structure", OrgId, UserId);

        dc.Name.Should().Be("Address");
        dc.Description.Should().Be("Postal address structure");
        dc.OrganizationId.Should().Be(OrgId);
        dc.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_sets_CreatedBy_and_DateTimeOffset_timestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var dc = DataClass.Create("Address", null, OrgId, UserId);

        dc.CreatedBy.Should().Be(UserId);
        dc.CreatedAt.Should().BeOnOrAfter(before);
        dc.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Create_raises_DataClassCreated_event()
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);
        dc.DomainEvents.Should().ContainSingle(e => e is DataClassCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]                   // too short
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // > 100
    public void Create_throws_when_name_invalid_length(string name)
    {
        var act = () => DataClass.Create(name, null, OrgId, UserId);
        act.Should().Throw<ArgumentException>();
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public void Update_changes_name_and_bumps_UpdatedAt()
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);
        var before = dc.UpdatedAt;

        dc.Update("Home Address", "Residential address");

        dc.Name.Should().Be("Home Address");
        dc.Description.Should().Be("Residential address");
        dc.UpdatedAt.Should().BeOnOrAfter(before);
    }

    // ─── AddField ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddField_accepts_allowed_field_types()
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);

        dc.AddField("street", "Street", FieldType.Text, false, new TextFieldConfig());
        dc.AddField("zip_code", "Zip", FieldType.Number, false, new NumberFieldConfig());
        dc.AddField("is_primary", "Primary", FieldType.Boolean, false, new BooleanFieldConfig());
        dc.AddField("since", "Since", FieldType.Date, false, new DateFieldConfig());

        dc.Fields.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(FieldType.Relation)]
    [InlineData(FieldType.DataClass)]
    [InlineData(FieldType.File)]
    public void AddField_throws_for_disallowed_field_types(FieldType type)
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);
        var act = () => dc.AddField("field1", "Field", type, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*not allowed*");
    }

    [Fact]
    public void AddField_throws_when_name_is_duplicate()
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);
        dc.AddField("street", "Street", FieldType.Text, false, new TextFieldConfig());

        var act = () => dc.AddField("Street", "Street 2", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    // ─── RemoveField ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveField_removes_a_field()
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);
        var field = dc.AddField("street", "Street", FieldType.Text, false, new TextFieldConfig());

        dc.RemoveField(field.Id);
        dc.Fields.Should().BeEmpty();
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_sets_DeletedAt_and_raises_event()
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);
        var before = DateTimeOffset.UtcNow;
        dc.Delete();

        dc.DeletedAt.Should().NotBeNull();
        dc.DeletedAt!.Value.Should().BeOnOrAfter(before);
        dc.DomainEvents.Should().Contain(e => e is DataClassDeleted);
    }

    [Fact]
    public void Delete_throws_when_already_deleted()
    {
        var dc = DataClass.Create("Address", null, OrgId, UserId);
        dc.Delete();

        var act = () => dc.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
