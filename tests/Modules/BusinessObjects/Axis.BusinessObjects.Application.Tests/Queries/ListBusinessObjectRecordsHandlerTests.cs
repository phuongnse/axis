using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Queries.ListBusinessObjectRecords;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Queries;

public sealed class ListBusinessObjectRecordsHandlerTests
{
    [Fact]
    public async Task ListRecords_WhenDecisionIsOwn_FiltersByOwnerBeforeMaterialization()
    {
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        SubjectReference owner = SubjectReference.Human(BusinessObjectRecordHandlerTestContext.UserId);
        records.CountOwnedForWorkspaceAsync(
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                owner,
                null,
                Arg.Any<CancellationToken>())
            .Returns(0);
        records.ListOwnedForWorkspaceAsync(
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                owner,
                null,
                1,
                20,
                Arg.Any<CancellationToken>())
            .Returns([]);
        ListBusinessObjectRecordsHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            new BusinessObjectRecordHandlerTestContext.FakeCurrentSubject(),
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(ProductActionScope.Own),
            records);

        Result<PagedResult<BusinessObjectRecordListItemDto>> result = await sut.Handle(
            new ListBusinessObjectRecordsQuery(1, 20, CorrelationId: "list-own"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await records.Received(1).ListOwnedForWorkspaceAsync(
            BusinessObjectRecordHandlerTestContext.WorkspaceId,
            owner,
            null,
            1,
            20,
            Arg.Any<CancellationToken>());
        await records.DidNotReceiveWithAnyArgs().ListForWorkspaceAsync(
            default,
            default,
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ListRecords_WhenPageIsRequested_ReturnsRowsAndPagingMetadata()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(BusinessObjectFieldType.Text);
        BusinessObjectRecord first = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["display_name"] = ["Ada Lovelace"],
            },
            idempotencyKey: "record-1");
        BusinessObjectRecord second = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["display_name"] = ["Grace Hopper"],
            },
            idempotencyKey: "record-2");
        BusinessObjectDefinitionKey objectKey = BusinessObjectDefinitionKey.Create("business_record").Value;
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        records.CountForWorkspaceAsync(
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                objectKey,
                Arg.Any<CancellationToken>())
            .Returns(3);
        records.ListForWorkspaceAsync(
                BusinessObjectRecordHandlerTestContext.WorkspaceId,
                objectKey,
                2,
                2,
                Arg.Any<CancellationToken>())
            .Returns([first, second]);

        ListBusinessObjectRecordsHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            new BusinessObjectRecordHandlerTestContext.FakeCurrentSubject(),
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(ProductActionScope.All),
            records);
        Result<PagedResult<BusinessObjectRecordListItemDto>> result = await sut.Handle(
            new ListBusinessObjectRecordsQuery(2, 2, objectKey.Value),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.TotalCount.Should().Be(3);
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(2);
        await records.Received(1).ListForWorkspaceAsync(
            BusinessObjectRecordHandlerTestContext.WorkspaceId,
            objectKey,
            2,
            2,
            Arg.Any<CancellationToken>());
    }
}
