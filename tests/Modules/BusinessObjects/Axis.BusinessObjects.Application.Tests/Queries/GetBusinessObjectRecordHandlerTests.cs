using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Queries.GetBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Identity.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Queries;

public sealed class GetBusinessObjectRecordHandlerTests
{
    [Fact]
    public async Task GetRecord_WhenOwnActionTargetsAnotherSubject_ReturnsNonDisclosingNotFound()
    {
        BusinessObjectDefinitionVersion definition = BusinessObjectRecordHandlerTestContext.PublishedDefinition();
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>> { ["display_name"] = ["Owner record"] });
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);
        BusinessObjectRecordHandlerTestContext.FakeCurrentSubject foreignSubject = new()
        {
            Subject = SubjectReference.Service(Guid.NewGuid()),
        };
        GetBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            foreignSubject,
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(ProductActionScope.Own),
            records,
            definitions);

        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new GetBusinessObjectRecordQuery(BusinessObjectRecordHandlerTestContext.RecordId),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        await definitions.DidNotReceiveWithAnyArgs().GetPublishedVersionByIdForWorkspaceAsync(
            default,
            default,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetRecord_WhenRecordIsWorkspaceScoped_ReturnsDefinitionContractAndValues()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(BusinessObjectFieldType.Text);
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["display_name"] = ["Ada Lovelace"],
            });
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);

        GetBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            new BusinessObjectRecordHandlerTestContext.FakeCurrentSubject(),
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(),
            records,
            definitions);
        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new GetBusinessObjectRecordQuery(BusinessObjectRecordHandlerTestContext.RecordId),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(record.Id.Value);
        result.Value.ObjectKey.Should().Be("business_record");
        result.Value.Values["display_name"].Should().Equal("Ada Lovelace");
        result.Value.Fields.Should().ContainSingle(field => field.FieldKey == "display_name");
    }
}
