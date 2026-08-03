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
    public async Task AddAsync_WhenRecordContainsValuesAndEvidence_RoundTripsAndSupportsWorkspaceQueries()
    {
        Guid workspaceId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        BusinessObjectDefinitionKey objectKey = BusinessObjectDefinitionKey.Create(
            UniqueKey("loan_application")).Value;
        BusinessObjectRecord record = CreateRecord(workspaceId, userId, objectKey, "record-1");
        record.Submit(
            expectedRevision: 1,
            values: record.Values,
            [new(
                "applicant_name",
                Guid.NewGuid(),
                2,
                "field.required",
                1,
                true,
                [new("required-check", true)])],
            userId,
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
        loaded.Values["applicant_name"].Should().Equal("Ada Lovelace");
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
        Guid userId,
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
                ["applicant_name"] = ["Ada Lovelace"],
            },
            userId,
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
