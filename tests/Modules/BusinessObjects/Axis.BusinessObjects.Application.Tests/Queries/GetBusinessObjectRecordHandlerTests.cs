using Axis.BusinessObjects.Application.Queries.GetBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Queries;

public sealed class GetBusinessObjectRecordHandlerTests
{
    [Fact]
    public async Task GetRecord_WhenRecordIsWorkspaceScoped_ReturnsDefinitionContractAndValues()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(BusinessObjectFieldType.Text);
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["applicant_name"] = ["Ada Lovelace"],
            });
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);

        GetBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            records,
            definitions);
        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new GetBusinessObjectRecordQuery(BusinessObjectRecordHandlerTestContext.RecordId),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(record.Id.Value);
        result.Value.ObjectKey.Should().Be("loan_application");
        result.Value.Values["applicant_name"].Should().Equal("Ada Lovelace");
        result.Value.Fields.Should().ContainSingle(field => field.FieldKey == "applicant_name");
    }
}
