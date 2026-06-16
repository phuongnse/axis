using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Entities;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Infrastructure.Tests.Repositories;

[Collection("DataModelingDb")]
public class DataModelRepositoryTests(DataModelingDatabaseFixture db) : IAsyncLifetime
{
    private DataModelingDbContext _ctx = null!;
    private DataModelRepository _sut = null!;

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private const string UserId = "user-123";

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new DataModelRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    private static DataModel MakeModel(string name) =>
        DataModel.Create(name, null, null, null, TeamAccountId, UserId);

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        DataModel model = MakeModel("Customer");
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        DataModel? loaded = await _sut.GetByIdAsync(model.Id, TeamAccountId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Customer");
        loaded.TeamAccountId.Should().Be(TeamAccountId);
    }

    [Fact]
    public async Task GetAllAsync_WhenMultipleModelsExist_ReturnsOnlyTeamAccountModelsExcludingDeleted()
    {
        Guid teamAccountId = Guid.NewGuid();
        DataModel active = DataModel.Create("Active", null, null, null, teamAccountId, UserId);
        DataModel deleted = DataModel.Create("Deleted", null, null, null, teamAccountId, UserId);
        deleted.Delete();
        DataModel otherTeamAccount = DataModel.Create("Other", null, null, null, Guid.NewGuid(), UserId);

        await _sut.AddAsync(active);
        await _sut.AddAsync(deleted);
        await _sut.AddAsync(otherTeamAccount);
        await _ctx.SaveChangesAsync();
        IReadOnlyList<DataModel> result = await _sut.GetAllAsync(teamAccountId);

        result.Should().ContainSingle().Which.Name.Should().Be("Active");
    }

    [Fact]
    public async Task NameExistsAsync_WhenNameExistsInTeamAccount_ReturnsTrueCaseInsensitive()
    {
        Guid teamAccountId = Guid.NewGuid();
        await _sut.AddAsync(DataModel.Create("Invoice", null, null, null, teamAccountId, UserId));
        await _ctx.SaveChangesAsync();

        bool exists = await _sut.NameExistsAsync("invoice", teamAccountId); // case-insensitive

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_WhenExcludeIdProvided_ExcludesThatModelFromCheck()
    {
        Guid teamAccountId = Guid.NewGuid();
        DataModel model = DataModel.Create("Project", null, null, null, teamAccountId, UserId);
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        bool exists = await _sut.NameExistsAsync("Project", teamAccountId, excludeId: model.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_WhenModelCreated_PersistsAndReloadsSystemFields()
    {
        DataModel model = MakeModel("Order");
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();
        DataModel? loaded = await _sut.GetByIdAsync(model.Id, TeamAccountId);

        loaded!.Fields.Should().HaveCount(3); // id, created_at, updated_at
        loaded.Fields.Should().Contain(f => f.Name == "id" && f.IsSystem);
        loaded.Fields.Should().Contain(f => f.Name == "created_at" && f.IsSystem);
        loaded.Fields.Should().Contain(f => f.Name == "updated_at" && f.IsSystem);
    }

    [Fact]
    public async Task AddAsync_WhenModelHasCustomFields_PersistsAndReloadsAllFieldTypes()
    {
        DataModel model = MakeModel("Product");
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
        DataModel? loaded = await _sut.GetByIdAsync(model.Id, TeamAccountId);
        List<FieldDefinition> customFields = loaded!.Fields.Where(f => !f.IsSystem).ToList();

        customFields.Should().HaveCount(4);
        FieldDefinition textField = customFields.Single(f => f.Name == "title");
        textField.Config.Should().BeOfType<TextFieldConfig>()
            .Which.MaxLength.Should().Be(200);
        FieldDefinition enumField = customFields.Single(f => f.Name == "status");
        enumField.Config.Should().BeOfType<EnumFieldConfig>()
            .Which.Options.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetByIdAsync_WhenModelIsDeleted_ReturnsNull()
    {
        DataModel model = MakeModel("ToDelete");
        model.Delete();
        await _sut.AddAsync(model);
        await _ctx.SaveChangesAsync();

        DataModel? loaded = await _sut.GetByIdAsync(model.Id, TeamAccountId);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task GetPagedAsync_WhenModelsExist_ReturnsPagedResult()
    {
        Guid teamAccountId = Guid.NewGuid();
        DataModel m1 = DataModel.Create($"Paged-Model-A-{Guid.NewGuid():N}", null, null, null, teamAccountId, UserId);
        DataModel m2 = DataModel.Create($"Paged-Model-B-{Guid.NewGuid():N}", null, null, null, teamAccountId, UserId);
        await _sut.AddAsync(m1);
        await _sut.AddAsync(m2);
        await _ctx.SaveChangesAsync();

        (IReadOnlyList<DataModel> items, int total) = await _sut.GetPagedAsync(teamAccountId, 1, 20);

        items.Should().HaveCount(2);
        total.Should().Be(2);
    }
}
