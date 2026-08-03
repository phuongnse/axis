using Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class SubmitBusinessObjectRecordHandlerTests
{
    [Fact]
    public async Task SubmitRecord_WhenExpectedRevisionIsStale_SkipsRuleEvaluation()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(includeRule: true);
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["applicant_name"] = ["Ada Lovelace"],
            });
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IRuleBindingEvaluator bindingEvaluator = Substitute.For<IRuleBindingEvaluator>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);

        SubmitBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            records,
            definitions,
            new BusinessObjectRecordRuleEvaluator(bindingEvaluator),
            unitOfWork);
        Result<BusinessObjectRecordSubmitResultDto> result = await sut.Handle(
            new SubmitBusinessObjectRecordCommand(
                BusinessObjectRecordHandlerTestContext.RecordId,
                ExpectedRevision: 0),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.Conflict);
        record.Status.Should().Be(BusinessObjectRecordStatus.Draft);
        await bindingEvaluator.DidNotReceiveWithAnyArgs().EvaluateBindingAsync(
            Arg.Any<RuleBindingEvaluationRequest>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
