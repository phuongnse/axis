using Axis.Rules.Application.Repositories;
using Axis.Rules.Application.Services;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Application.Identity;
using FluentAssertions;
using NSubstitute;
using ContractOutcomeKind = Axis.Rules.Contracts.RuleOutcomeKind;
using ContractScope = Axis.Rules.Contracts.RuleScope;

namespace Axis.Rules.Application.Tests;

internal sealed class RuleDefinitionHandlerTestContext
{
    public static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    public static readonly Guid UserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    public RuleDefinitionHandlerTestContext()
    {
        CurrentUser.UserId.Returns(UserId);
        CurrentUser.workspaceId.Returns(WorkspaceId);
        SchemaProvider.FindSchemaAsync(
                WorkspaceId,
                Schema.ContextKey,
                Schema.Version,
                Arg.Any<CancellationToken>())
            .Returns(Schema);
        SchemaProvider.ListSchemasAsync(
                WorkspaceId,
                Arg.Any<ContractScope?>(),
                Arg.Any<CancellationToken>())
            .Returns([Schema]);
    }

    public ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();
    public IRuleDefinitionRepository Repository { get; } = Substitute.For<IRuleDefinitionRepository>();
    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();
    public IRuleContextSchemaProvider SchemaProvider { get; } = Substitute.For<IRuleContextSchemaProvider>();
    public RuleContextSchemaRegistry ContextRegistry => new([SchemaProvider]);

    public static RuleContextSchemaDto Schema { get; } = new(
        "business_objects.field.decimal",
        Version: 1,
        ContractScope.Field,
        "Decimal field value",
        [new RuleContextFieldDto("field.value", "Field value", Axis.Rules.Contracts.RuleValueType.Decimal, false)],
        TargetTypeKey: "Decimal");

    public static RuleDefinition DraftDefinition(bool configured = false)
    {
        RuleDefinition definition = RuleDefinition.CreateDraft(
            WorkspaceId,
            RuleDefinitionKey.Create("credit_threshold").Value,
            "Credit threshold",
            "Flags high credit values.",
            Axis.Rules.Domain.RuleScope.Field,
            RuleContextKey.Create(Schema.ContextKey).Value,
            Schema.Version,
            Axis.Rules.Domain.RuleOutcomeKind.Validation,
            UserId,
            DateTime.UtcNow).Value;

        if (configured)
            Configure(definition);

        return definition;
    }

    public static RuleDefinition PublishedDefinition()
    {
        RuleDefinition definition = DraftDefinition(configured: true);
        definition.Publish(definition.Revision, UserId, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        return definition;
    }

    public static void Configure(RuleDefinition definition)
    {
        RuleParameterDefinition parameter = RuleParameterDefinition.Create(
            "threshold",
            Axis.Rules.Domain.RuleValueType.Decimal,
            isRequired: true).Value;
        RulePredicateCondition condition = RulePredicateCondition.Create(
            "threshold_check",
            Axis.Rules.Domain.RulePredicateOperator.GreaterThan,
            RuleOperand.Context("field.value").Value,
            RuleOperand.Parameter("threshold").Value).Value;
        RuleValidationOutcome outcome = RuleValidationOutcome.Create(
            "credit.threshold.exceeded",
            Axis.Rules.Domain.RuleSeverity.Error,
            "Credit value exceeds the configured threshold.").Value;

        definition.SaveDraft(
                definition.Revision,
                definition.Name,
                definition.Description,
                definition.Scope,
                definition.ContextKey,
                definition.ContextSchemaVersion,
                definition.OutcomeKind,
                [parameter],
                condition,
                outcome,
                UserId,
                DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
    }

    public static RuleConditionNodeDto ConditionDto() => new(
        "threshold_check",
        LogicalOperator: null,
        Axis.Rules.Contracts.RulePredicateOperator.GreaterThan,
        new RuleOperandDto(Axis.Rules.Contracts.RuleOperandKind.Context, "field.value", Literal: null),
        new RuleOperandDto(Axis.Rules.Contracts.RuleOperandKind.Parameter, "threshold", Literal: null),
        []);

    public static RuleOutcomeDto OutcomeDto() => new(
        ContractOutcomeKind.Validation,
        "credit.threshold.exceeded",
        Axis.Rules.Contracts.RuleSeverity.Error,
        "Credit value exceeds the configured threshold.",
        Decision: null);
}
