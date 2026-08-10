using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.SaveBusinessObjectRecord;
using Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;
using Axis.BusinessObjects.Application.Repositories;
using Axis.BusinessObjects.Application.Services;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Domain.ValueObjects;
using Axis.Identity.Contracts;
using Axis.Rules.Contracts;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using NSubstitute;
using DomainSubjectReference = Axis.BusinessObjects.Domain.ValueObjects.SubjectReference;
using IdentitySubjectReference = Axis.Identity.Contracts.SubjectReference;

namespace Axis.BusinessObjects.Application.Tests;

public sealed class BusinessObjectRecordApplicationTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid RecordId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid BindingId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly DateTime Now = new(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Save_WhenPublishedFieldValueHasWrongType_ReturnsFieldValidationWithoutCommit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        BusinessObjectDefinitionVersion definition = PublishedDefinition(BusinessObjectFieldType.Integer);
        BusinessObjectRecord record = DraftRecord(definition, ["quantity"]);
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        FakeCurrentUser currentUser = new();
        records.GetByIdForWorkspaceAsync(
                Arg.Any<BusinessObjectRecordId>(),
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(record);
        definitions.GetPublishedVersionByIdForWorkspaceAsync(
                definition.Id,
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);

        SaveBusinessObjectRecordHandler sut = new(
            currentUser,
            new FakeCurrentSubject(),
            BusinessObjectRecordHandlerTestContext.AllowedAuthorization(),
            records,
            definitions,
            unitOfWork);
        Result<BusinessObjectRecordDetailDto> result = await sut.Handle(
            new SaveBusinessObjectRecordCommand(
                RecordId,
                ExpectedRevision: 1,
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["quantity"] = ["not-a-number"],
                }),
            cancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.FieldValidation);
        result.FieldErrors.Should().ContainKey("quantity");
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
        record.Revision.Should().Be(1);
    }

    [Fact]
    public async Task Submit_WhenRuleDoesNotMatch_ReturnsDiagnosticsAndKeepsDraftRecoverable()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        BusinessObjectDefinitionVersion definition = PublishedDefinition(BusinessObjectFieldType.Text, includeRule: true);
        BusinessObjectRecord record = DraftRecord(definition, ["display_name"]);
        IRuleBindingEvaluator bindingEvaluator = Substitute.For<IRuleBindingEvaluator>();
        bindingEvaluator.EvaluateBindingAsync(
                Arg.Any<RuleBindingEvaluationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new RuleEvaluationResult(
                true,
                [new RuleEvaluationItemDto(
                    "field.required",
                    1,
                    false,
                    [new RuleNodeDiagnosticDto("required-check", false)])],
                "correlation",
                null,
                null));

        (SubmitBusinessObjectRecordHandler sut, IUnitOfWork unitOfWork) = SubmitHandler(
            definition,
            record,
            bindingEvaluator);
        Result<BusinessObjectRecordSubmitResultDto> result = await sut.Handle(
            new SubmitBusinessObjectRecordCommand(RecordId, ExpectedRevision: 1),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSubmitted.Should().BeFalse();
        result.Value.RuleEvaluations.Should().ContainSingle(evaluation => !evaluation.IsMatch);
        record.Status.Should().Be(BusinessObjectRecordStatus.Draft);
        record.Revision.Should().Be(1);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Submit_WhenRuleMatches_UsesExactBindingRevisionAndTypedContext()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        BusinessObjectDefinitionVersion definition = PublishedDefinition(BusinessObjectFieldType.Text, includeRule: true, bindingRevision: 3);
        BusinessObjectRecord record = DraftRecord(definition, ["display_name"]);
        IRuleBindingEvaluator bindingEvaluator = Substitute.For<IRuleBindingEvaluator>();
        bindingEvaluator.EvaluateBindingAsync(
                Arg.Any<RuleBindingEvaluationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new RuleEvaluationResult(
                true,
                [new RuleEvaluationItemDto("field.required", 1, true, [])],
                "correlation",
                null,
                null));

        (SubmitBusinessObjectRecordHandler sut, IUnitOfWork unitOfWork) = SubmitHandler(
            definition,
            record,
            bindingEvaluator);
        Result<BusinessObjectRecordSubmitResultDto> result = await sut.Handle(
            new SubmitBusinessObjectRecordCommand(RecordId, ExpectedRevision: 1),
            cancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsSubmitted.Should().BeTrue();
        record.Status.Should().Be(BusinessObjectRecordStatus.Submitted);
        record.RuleEvaluations.Single().BindingRevision.Should().Be(3);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bindingEvaluator.Received(1).EvaluateBindingAsync(
            Arg.Is<RuleBindingEvaluationRequest>(request =>
                request.BindingRevision == 3 &&
                request.Context.Values.ContainsKey("record.value") &&
                request.Context.Values["record.value"].Type == RuleValueType.Text &&
                request.Context.Values["record.value"].Values.SequenceEqual(new[] { "Ada Lovelace" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Submit_WhenRuleEvaluationFails_ReturnsBusinessRuleFailureWithoutCommit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        BusinessObjectDefinitionVersion definition = PublishedDefinition(BusinessObjectFieldType.Text, includeRule: true);
        BusinessObjectRecord record = DraftRecord(definition, ["display_name"]);
        IRuleBindingEvaluator bindingEvaluator = Substitute.For<IRuleBindingEvaluator>();
        bindingEvaluator.EvaluateBindingAsync(
                Arg.Any<RuleBindingEvaluationRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new RuleEvaluationResult(false, [], "correlation", "binding_disabled", "Rule binding is disabled."));

        (SubmitBusinessObjectRecordHandler sut, IUnitOfWork unitOfWork) = SubmitHandler(
            definition,
            record,
            bindingEvaluator);
        Result<BusinessObjectRecordSubmitResultDto> result = await sut.Handle(
            new SubmitBusinessObjectRecordCommand(RecordId, ExpectedRevision: 1),
            cancellationToken);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.BusinessRule);
        result.ProblemCode.Should().Be(BusinessObjectsProblemCodes.BusinessObjectRecordRuleExecutionFailed);
        record.Status.Should().Be(BusinessObjectRecordStatus.Draft);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static (SubmitBusinessObjectRecordHandler Handler, IUnitOfWork UnitOfWork) SubmitHandler(
        BusinessObjectDefinitionVersion definition,
        BusinessObjectRecord record,
        IRuleBindingEvaluator bindingEvaluator)
    {
        IBusinessObjectRecordRepository records = Substitute.For<IBusinessObjectRecordRepository>();
        IBusinessObjectDefinitionRepository definitions = Substitute.For<IBusinessObjectDefinitionRepository>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();
        records.GetByIdForWorkspaceAsync(
                Arg.Any<BusinessObjectRecordId>(),
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(record);
        definitions.GetPublishedVersionByIdForWorkspaceAsync(
                definition.Id,
                WorkspaceId,
                Arg.Any<CancellationToken>())
            .Returns(definition);

        return (
            new SubmitBusinessObjectRecordHandler(
                new FakeCurrentUser(),
                new FakeCurrentSubject(),
                BusinessObjectRecordHandlerTestContext.AllowedAuthorization(),
                records,
                definitions,
                new BusinessObjectRecordRuleEvaluator(bindingEvaluator),
                unitOfWork),
            unitOfWork);
    }

    private static BusinessObjectRecord DraftRecord(
        BusinessObjectDefinitionVersion definition,
        IReadOnlyList<string> keys)
    {
        Dictionary<string, IReadOnlyList<string>> values = keys.ToDictionary(
            key => key,
            key => (IReadOnlyList<string>)(key == "display_name" ? ["Ada Lovelace"] : ["1000"]),
            StringComparer.Ordinal);
        Result<BusinessObjectRecord> result = BusinessObjectRecord.CreateDraft(
            WorkspaceId,
            definition.Id,
            definition.VersionNumber,
            definition.Key,
            "record-1",
            "hash-1",
            values,
            DomainSubjectReference.Human(UserId),
            Now);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private static BusinessObjectDefinitionVersion PublishedDefinition(
        BusinessObjectFieldType fieldType,
        bool includeRule = false,
        int bindingRevision = 1)
    {
        BusinessObjectDefinition definition = BusinessObjectDefinitionHandlerTestContext.CreateUnpublished(
            "Business record",
            "business_record");
        BusinessObjectFieldRuleSpec[] rules = includeRule
            ? [new(BindingId, BindingRevision: bindingRevision)]
            : [];
        definition.SaveUnpublished(
            "Business record",
            [new BusinessObjectFieldDefinitionSpec(
                fieldType == BusinessObjectFieldType.Integer ? "quantity" : "display_name",
                fieldType == BusinessObjectFieldType.Integer ? "Quantity" : "Display name",
                0,
                fieldType,
                rules)],
            expectedRevision: 1,
            Now).IsSuccess.Should().BeTrue();
        definition.Publish(2, DomainSubjectReference.Human(UserId), Now).IsSuccess.Should().BeTrue();
        return definition.Versions.Single();
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid? UserId => BusinessObjectRecordApplicationTests.UserId;
        public Guid? workspaceId => BusinessObjectRecordApplicationTests.WorkspaceId;
    }

    private sealed class FakeCurrentSubject : ICurrentSubject
    {
        public IdentitySubjectReference Subject => IdentitySubjectReference.Human(UserId);
    }
}
