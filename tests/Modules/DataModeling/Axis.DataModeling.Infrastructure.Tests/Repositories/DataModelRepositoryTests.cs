using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Infrastructure.Tests.Repositories;

[Collection("DataModelingDb")]
public class DataModelRepositoryTests(DataModelingDatabaseFixture db) : IAsyncLifetime
{
    private DataModelingDbContext _ctx = null!;
    private DataModelRepository _sut = null!;

    private static readonly Guid OrgId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new DataModelRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static DataModel MakeModel(string name) =>
        DataModel.Create(name, null, null, null, OrgId);

    [Fact]
    public async Task AddAsync_and_GetByIdAsync_round_trip()
    {
        var model = MakeModel("Customer");
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(model.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Customer");
        loaded.OrganizationId.Should().Be(OrgId);
    }

    [Fact]
    public async Task GetAllAsync_returns_only_org_models_excluding_deleted()
    {
        var orgId = Guid.NewGuid();
        var active = DataModel.Create("Active", null, null, null, orgId);
        var deleted = DataModel.Create("Deleted", null, null, null, orgId);
        deleted.Delete();
        var otherOrg = DataModel.Create("Other", null, null, null, Guid.NewGuid());

        await _sut.AddAsync(active);
        await _sut.AddAsync(deleted);
        await _sut.AddAsync(otherOrg);
        await _ctx.SaveChangesAsync();

        var result = await _sut.GetAllAsync(orgId);

        result.Should().ContainSingle().Which.Name.Should().Be("Active");
    }

    [Fact]
    public async Task NameExistsAsync_returns_true_for_existing_name()
    {
        var orgId = Guid.NewGuid();
        await _sut.AddAsync(DataModel.Create("Invoice", null, null, null, orgId));
        await _ctx.SaveChangesAsync();

        var exists = await _sut.NameExistsAsync("invoice", orgId); // case-insensitive

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_excludes_self_on_update()
    {
        var orgId = Guid.NewGuid();
        var model = DataModel.Create("Project", null, null, null, orgId);
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        var exists = await _sut.NameExistsAsync("Project", orgId, excludeId: model.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task System_fields_are_persisted_and_reloaded()
    {
        var model = MakeModel("Order");
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(model.Id, OrgId);

        loaded!.Fields.Should().HaveCount(3); // id, created_at, updated_at
        loaded.Fields.Should().Contain(f => f.Name == "id" && f.IsSystem);
        loaded.Fields.Should().Contain(f => f.Name == "created_at" && f.IsSystem);
        loaded.Fields.Should().Contain(f => f.Name == "updated_at" && f.IsSystem);
    }

    [Fact]
    public async Task Custom_fields_with_various_types_round_trip()
    {
        var model = MakeModel("Product");
        model.AddField("title", "Title", FieldType.Text, true,
            new TextFieldConfig(MaxLength: 200));
        model.AddField("price", "Price", FieldType.Number, true,
            new NumberFieldConfig(Min: 0, DecimalPlaces: 2));
        model.AddField("in_stock", "In Stock", FieldType.Boolean, false,
            new BooleanFieldConfig(DefaultValue: true));
        model.AddField("status", "Status", FieldType.Enum, true,
            new EnumFieldConfig(
                Options: [new EnumOption("active", "Active"), new EnumOption("archived", "Archived")]));

        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(model.Id, OrgId);
        var customFields = loaded!.Fields.Where(f => !f.IsSystem).ToList();

        customFields.Should().HaveCount(4);
        var textField = customFields.Single(f => f.Name == "title");
        textField.Config.Should().BeOfType<TextFieldConfig>()
            .Which.MaxLength.Should().Be(200);
        var enumField = customFields.Single(f => f.Name == "status");
        enumField.Config.Should().BeOfType<EnumFieldConfig>()
            .Which.Options.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_deleted_model()
    {
        var model = MakeModel("ToDelete");
        model.Delete();
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(model.Id, OrgId);

        loaded.Should().BeNull();
    }
}
