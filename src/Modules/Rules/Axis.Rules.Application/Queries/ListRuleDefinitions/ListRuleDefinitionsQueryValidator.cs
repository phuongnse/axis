using FluentValidation;

namespace Axis.Rules.Application.Queries.ListRuleDefinitions;

public sealed class ListRuleDefinitionsQueryValidator : AbstractValidator<ListRuleDefinitionsQuery>
{
    public ListRuleDefinitionsQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThan(0)
            .WithErrorCode(RulesProblemCodes.DefinitionInvalid);

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode(RulesProblemCodes.DefinitionInvalid);
    }
}
