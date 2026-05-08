using Axis.DataModeling.Domain.Aggregates;
using FluentAssertions;

namespace Axis.DataModeling.Infrastructure.Tests.Repositories;

[Collection("DataModelingDb")]
public class DataRecordRepositoryTests(DataModelingDatabaseFixture db) : IAsyncLifetime
{
    private DataModelingDbContext _ctx = null!;
    private DataRecordRepository _sut = null!;

    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new DataRecordRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task AddAsync_and_GetByIdAsync_round_trip()
    {
        var data = new Dictionary<string, object?> { ["name"] = "Acme Corp", ["revenue"] = 1000000 };
        var record = DataRecord.Create(ModelId, OrgId, data);
        await _sut.AddAsync(record);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(record.Id, ModelId, OrgId);

        loaded.Should().NotBeNull();
        loaded!.ModelId.Should().Be(ModelId);
        loaded.OrganizationId.Should().Be(OrgId);
        loaded.Data.Should().ContainKey("name");
    }

    [Fact]
    public async Task GetAllAsync_returns_records_for_model_excluding_deleted()
    {
        var modelId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        var r1 = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 1 });
        var r2 = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 2 });
        var deleted = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 3 });
        deleted.Delete();
        var otherModel = DataRecord.Create(Guid.NewGuid(), orgId, new Dictionary<string, object?> { ["x"] = 4 });

        await _sut.AddAsync(r1);
        await _sut.AddAsync(r2);
        await _sut.AddAsync(deleted);
        await _sut.AddAsync(otherModel);
        await _ctx.SaveChangesAsync();

        var result = await _sut.GetAllAsync(modelId, orgId);

        result.Should().HaveCount(2);
        result.Should().NotContain(r => r.IsDeleted);
    }

    [Fact]
    public async Task Data_dictionary_persists_various_value_types()
    {
        var data = new Dictionary<string, object?>
        {
            ["text"] = "hello",
            ["number"] = 42,
            ["flag"] = true,
            ["nothing"] = null
        };
        var record = DataRecord.Create(ModelId, OrgId, data);
        await _sut.AddAsync(record);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(record.Id, ModelId, OrgId);

        loaded!.Data.Should().ContainKey("text");
        loaded.Data.Should().ContainKey("number");
        loaded.Data.Should().ContainKey("flag");
        loaded.Data.Should().ContainKey("nothing");
    }

    [Fact]
    public async Task GetPagedAsync_returns_correct_page_and_total()
    {
        var modelId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        for (var i = 1; i <= 5; i++)
            await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["i"] = i }));
        await _ctx.SaveChangesAsync();

        var (page1, total) = await _sut.GetPagedAsync(modelId, orgId, 1, 3, null);
        var (page2, _) = await _sut.GetPagedAsync(modelId, orgId, 2, 3, null);

        total.Should().Be(5);
        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_search_filters_by_jsonb_text()
    {
        var modelId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "Acme Corp" }));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "Beta LLC" }));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "acme subsidiary" }));
        await _ctx.SaveChangesAsync();

        var (results, total) = await _sut.GetPagedAsync(modelId, orgId, 1, 25, "acme");

        total.Should().Be(2);
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Data["company"]!.ToString()!.Contains("acme", StringComparison.OrdinalIgnoreCase));
    }
}
