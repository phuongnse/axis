using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;

namespace Axis.BusinessObjects.Application.Tests.Commands;

public sealed class SubmitBusinessObjectRecordHandlerTests
{
    [Fact]
    public async Task SubmitRecord_WhenOwnActionTargetsAnotherSubject_SkipsRulesAndMutation()
    {
        BusinessObjectDefinitionVersion definition = BusinessObjectRecordHandlerTestContext.PublishedDefinition(includeRule: true);
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>> { ["display_name"] = ["Owner record"] });
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IRuleBindingEvaluator bindingEvaluator = Substitute.For<IRuleBindingEvaluator>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);
        BusinessObjectRecordHandlerTestContext.FakeCurrentSubject foreignSubject = new()
        {
            Subject = SubjectReference.Service(Guid.NewGuid()),
        };
        SubmitBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            foreignSubject,
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(ProductActionScope.Own),
            records,
            definitions,
            new BusinessObjectRecordRuleEvaluator(bindingEvaluator),
            unitOfWork);

        Result<BusinessObjectRecordSubmitResultDto> result = await sut.Handle(
            new SubmitBusinessObjectRecordCommand(BusinessObjectRecordHandlerTestContext.RecordId, 1),
            TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
        record.Status.Should().Be(BusinessObjectRecordStatus.Draft);
        await bindingEvaluator.DidNotReceiveWithAnyArgs().EvaluateBindingAsync(
            Arg.Any<RuleBindingEvaluationRequest>(),
            Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SubmitRecord_WhenExpectedRevisionIsStale_SkipsRuleEvaluation()
    {
        BusinessObjectDefinitionVersion definition =
            BusinessObjectRecordHandlerTestContext.PublishedDefinition(includeRule: true);
        BusinessObjectRecord record = BusinessObjectRecordHandlerTestContext.DraftRecord(
            definition,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["display_name"] = ["Ada Lovelace"],
            });
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IRuleBindingEvaluator bindingEvaluator = Substitute.For<IRuleBindingEvaluator>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        BusinessObjectRecordHandlerTestContext.ConfigureRecord(records, definitions, definition, record);

        SubmitBusinessObjectRecordHandler sut = new(
            new BusinessObjectRecordHandlerTestContext.FakeCurrentUser(),
            new BusinessObjectRecordHandlerTestContext.FakeCurrentSubject(),
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(),
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
