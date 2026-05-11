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

    private static readonly Guid OrgId = Guid.NewGuid();
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
    public async Task AddAsync_and_GetByIdAsync_round_trip()
    {
        DataClass dc = DataClass.Create("Address", "Postal address", OrgId, UserId);
        await _sut.AddAsync(dc);
        await _ctx.SaveChangesAsync();

        DataClass? loaded = await _sut.GetByIdAsync(dc.Id, OrgId);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Address");
        loaded.Description.Should().Be("Postal address");
    }

    [Fact]
    public async Task Fields_are_persisted_and_reloaded()
    {
        DataClass dc = DataClass.Create("ContactInfo", null, OrgId, UserId);
        dc.AddField("email", "Email", FieldType.Text, true, new TextFieldConfig(MaxLength: 320));
        dc.AddField("phone", "Phone", FieldType.Text, false, new TextFieldConfig(MaxLength: 20));
        await _sut.AddAsync(dc);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(dc.Id, OrgId);

        loaded!.Fields.Should().HaveCount(2);
        loaded.Fields.Single(f => f.Name == "email").Config
            .Should().BeOfType<TextFieldConfig>().Which.MaxLength.Should().Be(320);
    }

    [Fact]
    public async Task IsReferencedByAnyModelAsync_returns_true_when_used_in_model()
    {
        Guid orgId = Guid.NewGuid();
        DataClass dc = DataClass.Create("Billing", null, orgId, UserId);
        await _sut.AddAsync(dc);

        DataModel model = DataModel.Create("Order", null, null, null, orgId, UserId);
        model.AddField("billing", "Billing", FieldType.DataClass, false, new DataClassFieldConfig(dc.Id));
        await _modelRepo.AddAsync(model);
        await _ctx.SaveChangesAsync();

        bool referenced = await _sut.IsReferencedByAnyModelAsync(dc.Id);

        referenced.Should().BeTrue();
    }

    [Fact]
    public async Task IsReferencedByAnyModelAsync_returns_false_when_not_used()
    {
        DataClass dc = DataClass.Create("Unused", null, OrgId, UserId);
        await _sut.AddAsync(dc);
        await _ctx.SaveChangesAsync();

        bool referenced = await _sut.IsReferencedByAnyModelAsync(dc.Id);

        referenced.Should().BeFalse();
    }

    [Fact]
    public async Task NameExistsAsync_is_case_insensitive()
    {
        Guid orgId = Guid.NewGuid();
        await _sut.AddAsync(DataClass.Create("Address", null, orgId, UserId));
        await _ctx.SaveChangesAsync();

        (await _sut.NameExistsAsync("address", orgId)).Should().BeTrue();
        (await _sut.NameExistsAsync("ADDRESS", orgId)).Should().BeTrue();
    }
}
