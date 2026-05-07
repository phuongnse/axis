using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.Events;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Domain.Tests;

public class DataClassTests
{
    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public void Create_sets_name_description_and_orgId()
    {
        var dc = DataClass.Create("Address", "Postal address structure", OrgId);

        dc.Name.Should().Be("Address");
        dc.Description.Should().Be("Postal address structure");
        dc.OrganizationId.Should().Be(OrgId);
        dc.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Create_raises_DataClassCreated_event()
    {
        var dc = DataClass.Create("Address", null, OrgId);
        dc.DomainEvents.Should().ContainSingle(e => e is DataClassCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]                   // too short
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // > 100
    public void Create_throws_when_name_invalid_length(string name)
    {
        var act = () => DataClass.Create(name, null, OrgId);
        act.Should().Throw<ArgumentException>();
    }

    // ─── AddField ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddField_accepts_allowed_field_types()
    {
        var dc = DataClass.Create("Address", null, OrgId);

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
        var dc = DataClass.Create("Address", null, OrgId);
        // Use TextFieldConfig as placeholder — the type check happens before config validation
        var act = () => dc.AddField("field1", "Field", type, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*not allowed*");
    }

    [Fact]
    public void AddField_throws_when_name_is_duplicate()
    {
        var dc = DataClass.Create("Address", null, OrgId);
        dc.AddField("street", "Street", FieldType.Text, false, new TextFieldConfig());

        var act = () => dc.AddField("Street", "Street 2", FieldType.Text, false, new TextFieldConfig());
        act.Should().Throw<InvalidOperationException>().WithMessage("*already exists*");
    }

    // ─── RemoveField ──────────────────────────────────────────────────────────

    [Fact]
    public void RemoveField_removes_a_field()
    {
        var dc = DataClass.Create("Address", null, OrgId);
        var field = dc.AddField("street", "Street", FieldType.Text, false, new TextFieldConfig());

        dc.RemoveField(field.Id);
        dc.Fields.Should().BeEmpty();
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_sets_IsDeleted()
    {
        var dc = DataClass.Create("Address", null, OrgId);
        dc.Delete();

        dc.IsDeleted.Should().BeTrue();
        dc.DomainEvents.Should().Contain(e => e is DataClassDeleted);
    }

    [Fact]
    public void Delete_throws_when_already_deleted()
    {
        var dc = DataClass.Create("Address", null, OrgId);
        dc.Delete();

        var act = () => dc.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
