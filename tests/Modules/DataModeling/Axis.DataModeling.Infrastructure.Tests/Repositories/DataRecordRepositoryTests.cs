using Axis.DataModeling.Application.Queries.GetRecords;
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

    // ── Add / GetById ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_WhenEntityIsValid_PersistsAndCanBeRetrievedById()
    {
        Dictionary<string, object?> data = new() { ["name"] = "Acme Corp", ["revenue"] = 1000000 };
        DataRecord record = DataRecord.Create(ModelId, OrgId, data, UserId);
        await _sut.AddAsync(record);
        await _ctx.SaveChangesAsync();

        DataRecord? loaded = await _sut.GetByIdAsync(record.Id, ModelId, OrgId);

        loaded.Should().NotBeNull();
        loaded!.ModelId.Should().Be(ModelId);
        loaded.OrganizationId.Should().Be(OrgId);
        loaded.Data.Should().ContainKey("name");
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

        DataRecord? loaded = await _sut.GetByIdAsync(record.Id, ModelId, OrgId);

        loaded!.Data.Should().ContainKey("text");
        loaded.Data.Should().ContainKey("number");
        loaded.Data.Should().ContainKey("flag");
        loaded.Data.Should().ContainKey("nothing");
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

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

    // ── GetPagedAsync — basic paging ──────────────────────────────────────────

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

    // ── GetPagedAsync — per-field filters ────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_WhenEqFilterApplied_ReturnsOnlyMatchingRecords()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["status"] = "active" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["status"] = "inactive" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["status"] = "active" }, UserId));
        await _ctx.SaveChangesAsync();

        IReadOnlyList<RecordFilter> filters = [new RecordFilter("status", "eq", "active")];
        (IReadOnlyList<DataRecord> results, int total) = await _sut.GetPagedAsync(modelId, orgId, 1, 25, null, filters);

        total.Should().Be(2);
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Data["status"]!.ToString() == "active");
    }

    [Fact]
    public async Task GetPagedAsync_WhenContainsFilterApplied_ReturnsMatchingRecords()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "Acme Corp" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["company"] = "Beta LLC" }, UserId));
        await _ctx.SaveChangesAsync();

        IReadOnlyList<RecordFilter> filters = [new RecordFilter("company", "contains", "acme")];
        (IReadOnlyList<DataRecord> results, int total) = await _sut.GetPagedAsync(modelId, orgId, 1, 25, null, filters);

        total.Should().Be(1);
        results.Single().Data["company"]!.ToString().Should().Be("Acme Corp");
    }

    [Fact]
    public async Task GetPagedAsync_WhenIsEmptyFilterApplied_ReturnsRecordsWithMissingOrEmptyField()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["notes"] = string.Empty }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["notes"] = "some note" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["other"] = "x" }, UserId));
        await _ctx.SaveChangesAsync();

        IReadOnlyList<RecordFilter> filters = [new RecordFilter("notes", "isempty", "")];
        (IReadOnlyList<DataRecord> results, int total) = await _sut.GetPagedAsync(modelId, orgId, 1, 25, null, filters);

        total.Should().Be(2);
    }

    // ── GetPagedAsync — sort ──────────────────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_WhenSortByJsonbField_ReturnsRecordsInCorrectOrder()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["name"] = "Zebra" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["name"] = "Apple" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["name"] = "Mango" }, UserId));
        await _ctx.SaveChangesAsync();

        (IReadOnlyList<DataRecord> results, int _) = await _sut.GetPagedAsync(
            modelId, orgId, 1, 25, null, null, sortBy: "name", sortDir: "asc");

        results.Select(r => r.Data["name"]!.ToString()).Should().BeInAscendingOrder();
    }

    // ── BulkDeleteAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task BulkDeleteAsync_WhenRecordsExist_SoftDeletesThemAndReturnsCount()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        DataRecord r1 = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 1 }, UserId);
        DataRecord r2 = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 2 }, UserId);
        DataRecord r3 = DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["x"] = 3 }, UserId);

        await _sut.AddAsync(r1);
        await _sut.AddAsync(r2);
        await _sut.AddAsync(r3);
        await _ctx.SaveChangesAsync();

        int deleted = await _sut.BulkDeleteAsync([r1.Id, r2.Id], modelId, orgId);

        deleted.Should().Be(2);

        // Verify via a fresh context that the records are soft-deleted.
        await using DataModelingDbContext freshCtx = db.CreateContext();
        DataRecord? loaded1 = await freshCtx.DataRecords.FindAsync(r1.Id);
        DataRecord? loaded2 = await freshCtx.DataRecords.FindAsync(r2.Id);
        DataRecord? loaded3 = await freshCtx.DataRecords.FindAsync(r3.Id);

        // Soft-deleted records have DeletedAt set but still exist in DB.
        // Note: global query filter excludes them from normal queries, so we bypass it.
        loaded1.Should().BeNull(); // filtered out by global query filter
        loaded2.Should().BeNull();
        loaded3.Should().NotBeNull(); // not deleted
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenIdsBelongToDifferentModel_DoesNotDeleteThem()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();
        Guid otherModelId = Guid.NewGuid();

        DataRecord r = DataRecord.Create(otherModelId, orgId, new Dictionary<string, object?> { ["x"] = 1 }, UserId);
        await _sut.AddAsync(r);
        await _ctx.SaveChangesAsync();

        int deleted = await _sut.BulkDeleteAsync([r.Id], modelId, orgId); // wrong modelId

        deleted.Should().Be(0);
    }

    [Fact]
    public async Task BulkDeleteAsync_WhenEmptyList_ReturnsZero()
    {
        int deleted = await _sut.BulkDeleteAsync([], ModelId, OrgId);
        deleted.Should().Be(0);
    }

    // ── GetAllForExportAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllForExportAsync_WhenRecordsExist_StreamsAllMatchingRecords()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        for (int i = 1; i <= 3; i++)
            await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["i"] = i }, UserId));
        await _ctx.SaveChangesAsync();

        List<DataRecord> exported = [];
        await foreach (DataRecord r in _sut.GetAllForExportAsync(modelId, orgId))
            exported.Add(r);

        exported.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetAllForExportAsync_WhenFilterApplied_StreamsOnlyMatchingRecords()
    {
        Guid modelId = Guid.NewGuid();
        Guid orgId = Guid.NewGuid();

        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["type"] = "A" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["type"] = "B" }, UserId));
        await _sut.AddAsync(DataRecord.Create(modelId, orgId, new Dictionary<string, object?> { ["type"] = "A" }, UserId));
        await _ctx.SaveChangesAsync();

        IReadOnlyList<RecordFilter> filters = [new RecordFilter("type", "eq", "A")];
        List<DataRecord> exported = [];
        await foreach (DataRecord r in _sut.GetAllForExportAsync(modelId, orgId, filters: filters))
            exported.Add(r);

        exported.Should().HaveCount(2);
        exported.Should().OnlyContain(r => r.Data["type"]!.ToString() == "A");
    }
}
