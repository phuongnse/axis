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
    private const string UserId = "user-123";

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _sut = new DataRecordRepository(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        Dictionary<string, object?> data = new() { ["name"] = "Acme Corp", ["revenue"] = 1000000 };
        DataRecord record = DataRecord.Create(ModelId, OrgId, data, UserId);
        await _sut.AddAsync(record);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(record.Id, ModelId, OrgId);

        loaded.Should().NotBeNull();
        loaded!.ModelId.Should().Be(ModelId);
        loaded.OrganizationId.Should().Be(OrgId);
        loaded.Data.Should().ContainKey("name");
    }

    [Fact]
    public async Task GetAllAsync_WhenMultipleRecordsExist_ReturnsRecordsForModelExcludingDeleted()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        DataRecord r1 = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 1 }, UserId);
        DataRecord r2 = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 2 }, UserId);
        DataRecord deleted = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 3 }, UserId);
        deleted.Delete();
        DataRecord otherModel = DataRecord.Create(Guid.NewGuid(), orgId, new Dictionary<string, object?> { ["x"] = 4 }, UserId);

        await _sut.AddAsync(r1);
        await _sut.AddAsync(r2);
        await _sut.AddAsync(deleted);
        await _sut.AddAsync(otherModel);
        await _ctx.SaveChangesAsync();

        IReadOnlyList<DataRecord> result = await _sut.GetAllAsync(modelId, orgId);

        result.Should().HaveCount(2);
        result.Should().NotContain(r => r.DeletedAt.HasValue);
    }

    [Fact]
    public async Task AddAsync_WhenDataContainsVariousValueTypes_PersistsAllTypes()
    {
        Dictionary<string, object?> data = new()
        {
            ["text"] = "hello",
            ["number"] = 42,
            ["flag"] = true,
            ["nothing"] = null
        };
        DataRecord record = DataRecord.Create(ModelId, OrgId, data, UserId);
        await _sut.AddAsync(record);
        await _ctx.SaveChangesAsync();

        var loaded = await _sut.GetByIdAsync(record.Id, ModelId, OrgId);

        loaded!.Data.Should().ContainKey("text");
        loaded.Data.Should().ContainKey("number");
        loaded.Data.Should().ContainKey("flag");
        loaded.Data.Should().ContainKey("nothing");
    }

    [Fact]
    public async Task GetPagedAsync_WhenMultipleRecordsExist_ReturnsCorrectPageAndTotal()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        for (int i = 1; i <= 5; i++)
            await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["i"] = i }, UserId));
        await _ctx.SaveChangesAsync();

        (IReadOnlyList<DataRecord> page1, int total) = await _sut.GetPagedAsync(modelId, orgId, 1, 3, null);
        (IReadOnlyList<DataRecord> page2, int _) = await _sut.GetPagedAsync(modelId, orgId, 2, 3, null);

        total.Should().Be(5);
        page1.Should().HaveCount(3);
        page2.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_WhenSearchProvided_FiltersByJsonbText()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "Acme Corp" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "Beta LLC" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "acme subsidiary" }, UserId));
        await _ctx.SaveChangesAsync();

        (IReadOnlyList<DataRecord> results, int total) = await _sut.GetPagedAsync(modelId, orgId, 1, 25, "acme");

        total.Should().Be(2);
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Data["company"]!.ToString()!.Contains("acme", StringComparison.OrdinalIgnoreCase));
    }
}
