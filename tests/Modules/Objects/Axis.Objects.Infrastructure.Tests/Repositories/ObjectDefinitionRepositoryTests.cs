using Axis.Objects.Application.Repositories;
using Axis.Objects.Application.Services;
using Axis.Objects.Domain.Aggregates;
using Axis.Objects.Domain.ValueObjects;
using Axis.Objects.Infrastructure.Persistence;
using Axis.Objects.Infrastructure.Repositories;
using Axis.Objects.Infrastructure.Tests.Fixtures;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Objects.Infrastructure.Tests.Repositories;

[Collection("ObjectsDb")]
public sealed class ObjectDefinitionRepositoryTests(ObjectsDatabaseFixture db) : IAsyncLifetime
{
    private ObjectsDbContext _ctx = null!;
    private IObjectDefinitionRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;

    public Task InitializeAsync()
    {
        _ctx = db.CreateContext();
        _repository = new ObjectDefinitionRepository(_ctx);
        _unitOfWork = new ObjectsUnitOfWork(_ctx);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

    [Fact]
    public async Task AddAsync_WhenDefinitionGraphIsPublished_PersistsDraftAndVersionSnapshot()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("customer");
        ObjectDefinition definition = CreateDraft(workspaceId, "Customer", objectKey);
        definition.SaveDraft(
            "Customer",
            [Field("name", "Name", 0), Field("status", "Status", 1)],
            expectedDraftVersion: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        definition.Publish(2, Guid.NewGuid(), DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        await using ObjectsDbContext reloadContext = db.CreateContext();
        IObjectDefinitionRepository reloadRepository = new ObjectDefinitionRepository(reloadContext);
        ObjectDefinition? loaded = await reloadRepository.GetByIdForWorkspaceAsync(
            definition.Id,
            workspaceId);

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(ObjectDefinitionStatus.Published);
        loaded.Fields.Should().HaveCount(2);
        loaded.Fields.Single(field => field.Key.Value == "status").Label.Should().Be("Status");
        loaded.Versions.Should().ContainSingle(version => version.VersionNumber == 1);
        loaded.Versions[0].Fields.Should().HaveCount(2);
        loaded.Versions[0].Fields.Single(field => field.Key.Value == "status").Label.Should().Be("Status");
    }

    [Fact]
    public async Task SaveDraft_WhenExistingFieldKeyRemains_ReusesStableId()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("account");
        ObjectDefinition definition = CreateDraft(workspaceId, "Account", objectKey);
        definition.SaveDraft(
            "Account",
            [Field("status", "Status", 0)],
            expectedDraftVersion: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        ObjectFieldDefinitionId originalFieldId = definition.Fields.Single().Id;

        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        ObjectDefinition loaded = (await _repository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        loaded.SaveDraft(
            "Account",
            [
                Field("status", "Lifecycle status", 0),
            ],
            expectedDraftVersion: 2,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await _unitOfWork.SaveChangesAsync();

        await using ObjectsDbContext reloadContext = db.CreateContext();
        IObjectDefinitionRepository reloadRepository = new ObjectDefinitionRepository(reloadContext);
        ObjectDefinition reloaded = (await reloadRepository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        ObjectFieldDefinition field = reloaded.Fields.Single();

        field.Id.Should().Be(originalFieldId);
        field.Label.Should().Be("Lifecycle status");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenWorkspaceObjectKeyConflicts_ThrowsUniqueConstraintException()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("invoice");

        await _repository.AddAsync(CreateDraft(workspaceId, "Invoice", objectKey));
        await _repository.AddAsync(CreateDraft(workspaceId, "Duplicate invoice", objectKey));

        Func<Task> act = () => _unitOfWork.SaveChangesAsync();

        await act.Should().ThrowAsync<UniqueConstraintException>();
    }

    [Fact]
    public async Task ObjectKeyExistsAsync_WhenExceptIdMatchesExistingDefinition_ReturnsFalse()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("order");
        ObjectDefinition definition = CreateDraft(workspaceId, "Order", objectKey);
        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        bool existsWithoutExcept = await _repository.ObjectKeyExistsAsync(
            workspaceId,
            ObjectDefinitionKey.Create(objectKey).Value);
        bool existsWithExcept = await _repository.ObjectKeyExistsAsync(
            workspaceId,
            ObjectDefinitionKey.Create(objectKey).Value,
            definition.Id);

        existsWithoutExcept.Should().BeTrue();
        existsWithExcept.Should().BeFalse();
    }

    [Fact]
    public async Task ListForWorkspaceAsync_WhenMultipleWorkspacesExist_ReturnsScopedPagedRows()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid otherWorkspaceId = Guid.NewGuid();
        ObjectDefinition first = CreateDraft(workspaceId, "Customer", UniqueKey("customer"));
        first.SaveDraft(
            first.Name,
            [Field("name", "Name", 0)],
            expectedDraftVersion: 1,
            DateTime.UtcNow.AddMinutes(-2)).IsSuccess.Should().BeTrue();
        ObjectDefinition second = CreateDraft(workspaceId, "Invoice", UniqueKey("invoice"));
        second.SaveDraft(
            second.Name,
            [Field("number", "Number", 0)],
            expectedDraftVersion: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(first);
        await _repository.AddAsync(second);
        await _repository.AddAsync(CreateDraft(otherWorkspaceId, "Other", UniqueKey("other")));
        await _unitOfWork.SaveChangesAsync();

        int total = await _repository.CountForWorkspaceAsync(workspaceId);
        IReadOnlyList<ObjectDefinition> page = await _repository.ListForWorkspaceAsync(
            workspaceId,
            page: 1,
            pageSize: 1);

        total.Should().Be(2);
        page.Should().ContainSingle();
        page[0].Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenConcurrentDraftUpdateWins_ThrowsConcurrencyException()
    {
        Guid workspaceId = Guid.NewGuid();
        string objectKey = UniqueKey("lead");
        ObjectDefinition definition = CreateDraft(workspaceId, "Lead", objectKey);
        await _repository.AddAsync(definition);
        await _unitOfWork.SaveChangesAsync();

        await using ObjectsDbContext firstContext = db.CreateContext();
        await using ObjectsDbContext secondContext = db.CreateContext();
        IObjectDefinitionRepository firstRepository = new ObjectDefinitionRepository(firstContext);
        IObjectDefinitionRepository secondRepository = new ObjectDefinitionRepository(secondContext);
        IUnitOfWork firstUnitOfWork = new ObjectsUnitOfWork(firstContext);
        IUnitOfWork secondUnitOfWork = new ObjectsUnitOfWork(secondContext);

        ObjectDefinition first = (await firstRepository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        ObjectDefinition second = (await secondRepository.GetByIdForWorkspaceAsync(definition.Id, workspaceId))!;
        first.SaveDraft(
            "Lead",
            [Field("name", "Name", 0)],
            expectedDraftVersion: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();
        second.SaveDraft(
            "Lead",
            [Field("company", "Company", 0)],
            expectedDraftVersion: 1,
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await firstUnitOfWork.SaveChangesAsync();
        Func<Task> act = () => secondUnitOfWork.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    private static ObjectDefinition CreateDraft(Guid workspaceId, string name, string key)
    {
        Result<ObjectDefinition> result = ObjectDefinition.CreateDraft(
            workspaceId,
            name,
            ObjectDefinitionKey.Create(key).Value,
            DateTime.UtcNow);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static ObjectFieldDefinitionSpec Field(string key, string label, int order) =>
        new(key, label, order);

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 9)];
}
