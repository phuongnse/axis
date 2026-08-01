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

            RuleFor(query => query.Request.Inputs)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .Must(inputs => inputs.Count <= Domain.RuleEvaluationLimits.Default.MaxInputs)
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
