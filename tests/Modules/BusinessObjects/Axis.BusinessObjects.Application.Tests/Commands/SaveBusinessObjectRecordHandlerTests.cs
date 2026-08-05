using Axis.BusinessObjects.Application.Commands.SaveBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class SaveBusinessObjectRecordHandlerTests
{
    [Fact]
    public async Task SaveRecord_WhenTypedValuesUseNonCanonicalLexemes_PersistsCanonicalValues()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(BusinessObjectFieldType.Integer);
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["quantity"] = ["1000"],
            });
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);

        SaveBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            records,
            definitions,
            unitOfWork);
        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new SaveBusinessObjectRecordCommand(
                BusinessObjectRecordHandlerTestContext.RecordId,
                1,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["quantity"] = ["0012"],
                }),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Values["quantity"].Should().Equal("12");
        record.Values["quantity"].Should().Equal("12");
        record.Revision.Should().Be(2);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveRecord_WhenFieldValueIsNull_ReturnsFieldValidation()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(BusinessObjectFieldType.Text);
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>());
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);

        SaveBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            records,
            definitions,
            unitOfWork);
        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new SaveBusinessObjectRecordCommand(
                BusinessObjectRecordHandlerTestContext.RecordId,
                1,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["display_name"] = null!,
                }),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.FieldValidation);
        result.FieldErrors.Should().ContainKey("display_name");
        record.Revision.Should().Be(1);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
