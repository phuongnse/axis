using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.BusinessObjects.Infrastructure.Repositories;
using Axis.BusinessObjects.Infrastructure.Tests.Fixtures;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.BusinessObjects.Infrastructure.Tests.Repositories;

[Collection("BusinessObjectsDb")]
public sealed class BusinessObjectRecordRepositoryTests(BusinessObjectsDatabaseFixture db) : IAsyncLifetime
{
    private BusinessObjectsDbContext _context = null!;
    private IBusinessObjectRecordRepository _repository = null!;
    private IUnitOfWork _unitOfWork = null!;

    public ValueTask InitializeAsync()
    {
        _context = db.CreateContext();
        _repository = new BusinessObjectRecordRepository(_context);
        _unitOfWork = new BusinessObjectsUnitOfWork(_context);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task ListOwnedForWorkspaceAsync_WhenOwnersDiffer_FiltersBeforeMaterialization()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid humanId = Guid.NewGuid();
        Guid serviceId = Guid.NewGuid();
        BusinessObjectDefinitionKey objectKey = BusinessObjectDefinitionKey.Create(UniqueKey("owned_record")).Value;
        BusinessObjectRecord humanRecord = CreateRecord(
            workspaceId,
            SubjectReference.Human(humanId),
            objectKey,
            "human-record");
        BusinessObjectRecord serviceRecord = CreateRecord(
            workspaceId,
            SubjectReference.Service(serviceId),
            objectKey,
            "service-record");
        await _repository.AddAsync(humanRecord, TestContext.Current.CancellationToken);
        await _repository.AddAsync(serviceRecord, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        IReadOnlyList<BusinessObjectRecord> humanRecords = await _repository.ListOwnedForWorkspaceAsync(
            workspaceId,
            SubjectReference.Human(humanId),
            objectKey,
            1,
            10,
            TestContext.Current.CancellationToken);
        IReadOnlyList<BusinessObjectRecord> serviceRecords = await _repository.ListOwnedForWorkspaceAsync(
            workspaceId,
            SubjectReference.Service(serviceId),
            objectKey,
            1,
            10,
            TestContext.Current.CancellationToken);

        humanRecords.Should().ContainSingle(record => record.Id == humanRecord.Id);
        serviceRecords.Should().ContainSingle(record => record.Id == serviceRecord.Id);
        (await _repository.CountOwnedForWorkspaceAsync(
            workspaceId,
            SubjectReference.Human(humanId),
            objectKey,
            TestContext.Current.CancellationToken)).Should().Be(1);
    }

    [Fact]
    public async Task AddAsync_WhenRecordContainsValuesAndEvidence_RoundTripsAndSupportsWorkspaceQueries()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        BusinessObjectDefinitionKey objectKey = BusinessObjectDefinitionKey.Create(
            UniqueKey("business_record")).Value;
        BusinessObjectRecord record = CreateRecord(
            workspaceId,
            SubjectReference.Human(userId),
            objectKey,
            "record-1");
        record.Submit(
            expectedRevision: 1,
            values: record.Values,
            [new(
                "display_name",
                Guid.NewGuid(),
                2,
                "field.required",
                1,
                true,
                [new("required-check", true)])],
            SubjectReference.Human(userId),
            DateTime.UtcNow).IsSuccess.Should().BeTrue();

        await _repository.AddAsync(record, TestContext.Current.CancellationToken);
        await _unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using BusinessObjectsDbContext reloadContext = db.CreateContext();
        IBusinessObjectRecordRepository reloadRepository = new BusinessObjectRecordRepository(reloadContext);
        BusinessObjectRecord? loaded = await reloadRepository.GetByIdForWorkspaceAsync(
            record.Id,
            workspaceId,
            TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.Status.Should().Be(BusinessObjectRecordStatus.Submitted);
        loaded.Values["display_name"].Should().Equal("Ada Lovelace");
        loaded.RuleEvaluations.Should().ContainSingle(evaluation => evaluation.BindingRevision == 2);
        (await reloadRepository.FindByIdempotencyKeyAsync(
            workspaceId,
            objectKey,
            "record-1",
            TestContext.Current.CancellationToken))!.Id.Should().Be(record.Id);
        (await reloadRepository.CountForWorkspaceAsync(
            workspaceId,
            objectKey,
            TestContext.Current.CancellationToken)).Should().Be(1);
        (await reloadRepository.ListForWorkspaceAsync(
            workspaceId,
            objectKey,
            1,
            10,
            TestContext.Current.CancellationToken)).Should().ContainSingle(item => item.Id == record.Id);
    }

    private static BusinessObjectRecord CreateRecord(
        Guid workspaceId,
        SubjectReference owner,
        BusinessObjectDefinitionKey objectKey,
        string idempotencyKey)
    {
        Result<BusinessObjectRecord> result = BusinessObjectRecord.CreateDraft(
            workspaceId,
            BusinessObjectDefinitionVersionId.New(),
            1,
            objectKey,
            idempotencyKey,
            "hash-1",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["display_name"] = ["Ada Lovelace"],
            },
            owner,
            DateTime.UtcNow);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static string UniqueKey(string prefix)
    {
        string key = $"{prefix}_{Guid.NewGuid():N}";
        return key.Length <= 63 ? key : key[..63];
    }
}
