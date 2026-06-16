using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Enums;
using Axis.DataModeling.Domain.ValueObjects;
using FluentAssertions;

namespace Axis.DataModeling.Infrastructure.Tests.Repositories;

[Collection("DataModelingDb")]
public class DataClassRepositoryTests(DataModelingDatabaseFixture db) : IAsyncLifetime
{
    private DataModelingDbContext _ctx = null!;
    private DataClassRepository _sut = null!;
    private DataModelRepository _modelRepo = null!;

    private static readonly Guid TeamAccountId = Guid.NewGuid();
    private const string UserId = "user-123";

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new DataClassRepository(_ctx);
        _modelRepo = new DataModelRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        DataClass dc = DataClass.Create("Address", "Postal address", TeamAccountId, UserId);
        await _sut.AddAsync(dc);
        await _ctx.SaveChangesAsync();

        DataClass? loaded = await _sut.GetByIdAsync(dc.Id, TeamAccountId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Address");
        loaded.Description.Should().Be("Postal address");
    }

    [Fact]
    public async Task AddAsync_WhenDataClassHasFields_PersistsAndReloadsFields()
    {
        DataClass dc = DataClass.Create("ContactInfo", null, TeamAccountId, UserId);
        dc.AddField("email", "Email", FieldType.Text, true, new TextFieldConfig(MaxLength: 320));
        dc.AddField("phone", "Phone", FieldType.Text, false, new TextFieldConfig(MaxLength: 20));
        await _sut.AddAsync(dc);
        await _ctx.SaveChangesAsync();
        DataClass? loaded = await _sut.GetByIdAsync(dc.Id, TeamAccountId);

        loaded!.Fields.Should().HaveCount(2);
        loaded.Fields.Single(f => f.Name == "email").Config
            .Should().BeOfType<TextFieldConfig>().Which.MaxLength.Should().Be(320);
    }

    [Fact]
    public async Task IsReferencedByAnyModelAsync_WhenUsedInModel_ReturnsTrue()
    {
        Guid teamAccountId = Guid.NewGuid();
        DataClass dc = DataClass.Create("Billing", null, teamAccountId, UserId);
        await _sut.AddAsync(dc);

        DataModel model = DataModel.Create("Order", null, null, null, teamAccountId, UserId);
        model.AddField("billing", "Billing", FieldType.DataClass, false, new DataClassFieldConfig(dc.Id));
        await _modelRepo.AddAsync(model);
        await _ctx.SaveChangesAsync();

        bool referenced = await _sut.IsReferencedByAnyModelAsync(dc.Id);

        referenced.Should().BeTrue();
    }

    [Fact]
    public async Task IsReferencedByAnyModelAsync_WhenNotUsed_ReturnsFalse()
    {
        DataClass dc = DataClass.Create("Unused", null, TeamAccountId, UserId);
        await _sut.AddAsync(dc);
        await _ctx.SaveChangesAsync();

        bool referenced = await _sut.IsReferencedByAnyModelAsync(dc.Id);

        referenced.Should().BeFalse();
    }

    [Fact]
    public async Task NameExistsAsync_WhenNameExists_IsCaseInsensitive()
    {
        Guid teamAccountId = Guid.NewGuid();
        await _sut.AddAsync(DataClass.Create("Address", null, teamAccountId, UserId));
        await _ctx.SaveChangesAsync();

        (await _sut.NameExistsAsync("address", teamAccountId)).Should().BeTrue();
        (await _sut.NameExistsAsync("ADDRESS", teamAccountId)).Should().BeTrue();
    }

    [Fact]
    public async Task GetPagedAsync_WhenClassesExist_ReturnsPagedResult()
    {
        Guid teamAccountId = Guid.NewGuid();
        DataClass c1 = DataClass.Create($"Paged-Class-A-{Guid.NewGuid():N}", null, teamAccountId, UserId);
        DataClass c2 = DataClass.Create($"Paged-Class-B-{Guid.NewGuid():N}", null, teamAccountId, UserId);
        await _sut.AddAsync(c1);
        await _sut.AddAsync(c2);
        await _ctx.SaveChangesAsync();

        (IReadOnlyList<DataClass> items, int total) = await _sut.GetPagedAsync(teamAccountId, 1, 20);

        items.Should().HaveCount(2);
        total.Should().Be(2);
    }
}
