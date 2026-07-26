using Axis.Rules.Contracts;
using FluentValidation;

namespace Axis.Rules.Application.Queries.SearchRuleExpressionGuide;

public sealed class SearchRuleExpressionGuideQueryValidator
    : AbstractValidator<SearchRuleExpressionGuideQuery>
{
    public SearchRuleExpressionGuideQueryValidator()
    {
        RuleFor(query => query.Request)
            .NotNull()
            .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

        When(query => query.Request is not null, () =>
        {
            RuleFor(query => query.Request.ExpressionLanguageVersion)
                .Equal(Domain.RuleExpressionLanguage.Version)
                .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

            RuleFor(query => query.Request.DefinitionKey)
                .MaximumLength(63)
                .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

            RuleFor(query => query.Request.ContextKey)
                .MaximumLength(120)
                .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

            RuleFor(query => query.Request.ContextSchemaVersion)
                .GreaterThan(0)
                .When(query => query.Request.ContextSchemaVersion.HasValue)
                .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

            RuleFor(query => query.Request.Parameters)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .Must(parameters => parameters.Count <= Domain.RuleEvaluationLimits.Default.MaxParameters)
                .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

            RuleFor(query => query.Request.Query)
                .MaximumLength(200)
                .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

            RuleFor(query => query.Request.Language)
                .NotEmpty()
                .MaximumLength(16)
                .WithErrorCode(RulesProblemCodes.DefinitionInvalid);
        });
    }
}
