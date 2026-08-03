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
    public async Task ListRecords_WhenPageIsRequested_ReturnsRowsAndPagingMetadata()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(BusinessObjectFieldType.Text);
        BusinessObjectRecord first = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["applicant_name"] = ["Ada Lovelace"],
            },
            idempotencyKey: "record-1");
        BusinessObjectRecord second = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["applicant_name"] = ["Grace Hopper"],
            },
            idempotencyKey: "record-2");
        BusinessObjectDefinitionKey objectKey = BusinessObjectDefinitionKey.Create("loan_application").Value;
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

        ListBusinessObjectRecordsHandler sut = new(new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(), records);
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
