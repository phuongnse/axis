using Axis.FormBuilder.Domain.Aggregates;
using Axis.FormBuilder.Domain.Enums;
using Axis.FormBuilder.Domain.ReadModels;
using Axis.FormBuilder.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.FormBuilder.Infrastructure.Tests.Repositories;

[Collection("FormBuilderDb")]
public class FormRepositoryTests(FormBuilderDatabaseFixture db) : IAsyncLifetime
{
    private FormBuilderDbContext _ctx = null!;
    private FormRepository _sut = null!;

    private static readonly Guid TenantId = Guid.NewGuid();
    private const string UserId = "user-123";

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new FormRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static FormDefinition MakeForm(string name, Guid? tenantId = null)
        => FormDefinition.Create(name, null, tenantId ?? TenantId, UserId);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        FormDefinition form = MakeForm("Contact Form");
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();

        FormDefinition? loaded = await _sut.GetByIdAsync(form.Id, TenantId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Contact Form");
        loaded.tenantId.Should().Be(TenantId);
        loaded.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_WhenMultipleFormsExist_ExcludesDeletedAndOthertenants()
    {
        Guid TenantId = Guid.NewGuid();
        FormDefinition active = MakeForm("Active Form", TenantId);
        FormDefinition deleted = MakeForm("Deleted Form", TenantId);
        deleted.Delete();
        FormDefinition other = MakeForm("Other Tenant Form", Guid.NewGuid());

        await _sut.AddAsync(active);
        await _sut.AddAsync(deleted);
        await _sut.AddAsync(other);
        await _ctx.SaveChangesAsync();
        IReadOnlyList<FormDefinition> result = await _sut.GetAllAsync(TenantId);

        result.Should().ContainSingle().Which.Name.Should().Be("Active Form");
    }

    [Fact]
    public async Task GetByIdAsync_WhenFormIsDeleted_ReturnsNull()
    {
        Guid TenantId = Guid.NewGuid();
        FormDefinition form = MakeForm("To Delete", TenantId);
        form.Delete();
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();
        FormDefinition? loaded = await _sut.GetByIdAsync(form.Id, TenantId);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task NameExistsAsync_WhenNameExists_IsCaseInsensitive()
    {
        Guid TenantId = Guid.NewGuid();
        await _sut.AddAsync(MakeForm("Feedback Form", TenantId));
        await _ctx.SaveChangesAsync();

        (await _sut.NameExistsAsync("feedback form", TenantId)).Should().BeTrue();
        (await _sut.NameExistsAsync("FEEDBACK FORM", TenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_WhenExcludeIdProvided_ExcludesThatFormFromCheck()
    {
        Guid TenantId = Guid.NewGuid();
        FormDefinition form = MakeForm("Survey Form", TenantId);
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();
        bool exists = await _sut.NameExistsAsync("Survey Form", TenantId, excludeId: form.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_WhenFormHasFieldsWithVariousConfigTypes_PersistsAndReloadsAllFields()
    {
        FormDefinition form = MakeForm("Multi-type Form");
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
        FormDefinition? loaded = await _sut.GetByIdAsync(form.Id, TenantId);

        loaded!.Fields.Should().HaveCount(5);
        loaded.Fields.Single(f => f.Key == "title").Config
            .Should().BeOfType<TextFormFieldConfig>().Which.MaxLength.Should().Be(200);
        loaded.Fields.Single(f => f.Key == "age").Config
            .Should().BeOfType<NumberFormFieldConfig>().Which.Max.Should().Be(120);
        loaded.Fields.Single(f => f.Key == "accepted").Config.Should().BeNull();
        DropdownFieldConfig dropdownConfig = loaded.Fields.Single(f => f.Key == "status").Config
                    .Should().BeOfType<DropdownFieldConfig>().Subject;
        dropdownConfig.Options.Should().HaveCount(2);
    }

    [Fact]
    public async Task IsReferencedByWorkflowAsync_WhenFormNotReferenced_ReturnsFalse()
    {
        FormDefinition form = MakeForm("Unreferenced Form");
        await _sut.AddAsync(form);
        await _ctx.SaveChangesAsync();
        bool referenced = await _sut.IsReferencedByWorkflowAsync(form.Id);

        referenced.Should().BeFalse();
    }

    [Fact]
    public async Task IsReferencedByWorkflowAsync_WhenFormUsedInWorkflowStep_ReturnsTrue()
    {
        FormDefinition form = MakeForm("Referenced Form");
        await _sut.AddAsync(form);
        _ctx.FormWorkflowReferences.Add(
            FormWorkflowReference.Create(Guid.NewGuid(), form.Id, form.tenantId));
        await _ctx.SaveChangesAsync();
        bool referenced = await _sut.IsReferencedByWorkflowAsync(form.Id);

        referenced.Should().BeTrue();
    }
}
