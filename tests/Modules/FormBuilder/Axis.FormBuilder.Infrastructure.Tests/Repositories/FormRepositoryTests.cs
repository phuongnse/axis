using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ValueObjects;
using FluentAssertions;
using Npgsql;

namespace Axis.FormBuilder.Infrastructure.Tests.Repositories;

[Collection("FormBuilderDb")]
public class FormRepositoryTests(FormBuilderDatabaseFixture db) : IAsyncLifetime
{
    private FormBuilderDbContext _ctx = null!;
    private FormRepository _sut = null!;

    private static readonly Guid OrgId = Guid.NewGuid();
    private const string UserId = "user-123";

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new FormRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static FormDefinition MakeForm(string name, Guid? orgId = null)
        => FormDefinition.Create(name, null, orgId ?? OrgId, UserId);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        FormDefinition form = MakeForm("Contact Form");
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();

        FormDefinition? loaded = await _sut.GetByIdAsync(form.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Contact Form");
        loaded.OrganizationId.Should().Be(OrgId);
        loaded.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WhenMultipleFormsExist_ExcludesDeletedAndOtherOrgs()
    {
        var orgId = Guid.NewGuid();
        var active = MakeForm("Active Form", orgId);
        var deleted = MakeForm("Deleted Form", orgId);
        deleted.Delete();
        var other = MakeForm("Other Org Form", Guid.NewGuid());

        await _sut.AddAsync(active);
        await _sut.AddAsync(deleted);
        await _sut.AddAsync(other);
        await _ctx.SaveChangesAsync();

        var result = await _sut.GetAllAsync(orgId);

        result.Should().ContainSingle().Which.Name.Should().Be("Active Form");
    }

    [Fact]
    public async Task GetByIdAsync_WhenFormIsDeleted_ReturnsNull()
    {
        var orgId = Guid.NewGuid();
        var form = MakeForm("To Delete", orgId);
        form.Delete();
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(form.Id, orgId);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task NameExistsAsync_WhenNameExists_IsCaseInsensitive()
    {
        var orgId = Guid.NewGuid();
        await _sut.AddAsync(MakeForm("Feedback Form", orgId));
        await _ctx.SaveChangesAsync();

        (await _sut.NameExistsAsync("feedback form", orgId)).Should().BeTrue();
        (await _sut.NameExistsAsync("FEEDBACK FORM", orgId)).Should().BeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_WhenExcludeIdProvided_ExcludesThatFormFromCheck()
    {
        var orgId = Guid.NewGuid();
        var form = MakeForm("Survey Form", orgId);
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();

        var exists = await _sut.NameExistsAsync("Survey Form", orgId, excludeId: form.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_WhenFormHasFieldsWithVariousConfigTypes_PersistsAndReloadsAllFields()
    {
        var form = MakeForm("Multi-type Form");
        form.AddField("title", "Title", FormFieldType.Text, true,
            new TextFormFieldConfig(MaxLength: 200, Placeholder: "Enter title"));
        form.AddField("age", "Age", FormFieldType.Number, false,
            new NumberFormFieldConfig(Min: 0, Max: 120, DecimalPlaces: 0));
        form.AddField("accepted", "Accepted", FormFieldType.Boolean, true, null);
        form.AddField("status", "Status", FormFieldType.Dropdown, true,
            new DropdownFieldConfig([new DropdownOption("active", "Active"), new DropdownOption("inactive", "Inactive")]));
        form.AddField("birth_date", "Birth Date", FormFieldType.Date, false,
            new DateFormFieldConfig(IncludeTime: false));

        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(form.Id, OrgId);

        loaded!.Fields.Should().HaveCount(5);
        loaded.Fields.Single(f => f.Key == "title").Config
            .Should().BeOfType<TextFormFieldConfig>().Which.MaxLength.Should().Be(200);
        loaded.Fields.Single(f => f.Key == "age").Config
            .Should().BeOfType<NumberFormFieldConfig>().Which.Max.Should().Be(120);
        loaded.Fields.Single(f => f.Key == "accepted").Config.Should().BeNull();
        var dropdownConfig = loaded.Fields.Single(f => f.Key == "status").Config
            .Should().BeOfType<DropdownFieldConfig>().Subject;
        dropdownConfig.Options.Should().HaveCount(2);
    }

    [Fact]
    public async Task IsReferencedByWorkflowAsync_WhenFormNotReferenced_ReturnsFalse()
    {
        var form = MakeForm("Unreferenced Form");
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();

        var referenced = await _sut.IsReferencedByWorkflowAsync(form.Id);

        referenced.Should().BeFalse();
    }

    [Fact]
    public async Task IsReferencedByWorkflowAsync_WhenFormUsedInWorkflowStep_ReturnsTrue()
    {
        var form = MakeForm("Referenced Form");
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();

        // Insert a workflow_definitions row that references this form in its steps JSONB
        await using var conn = new NpgsqlConnection(db.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"INSERT INTO \"test_form_builder\".workflow_definitions (id, steps) " +
            $"VALUES (gen_random_uuid(), '[{{\"type\":\"Form\",\"config\":{{\"formId\":\"{form.Id:D}\"}}}}]'::jsonb)";
        await cmd.ExecuteNonQueryAsync();

        var referenced = await _sut.IsReferencedByWorkflowAsync(form.Id);

        referenced.Should().BeTrue();
    }
}
