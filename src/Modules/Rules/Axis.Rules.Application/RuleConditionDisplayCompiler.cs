using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using ContractReferenceKind = Axis.Rules.Contracts.RuleExpressionReferenceKind;
using DomainExpressionFunction = Axis.Rules.Domain.RuleExpressionFunction;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

internal sealed class RuleConditionDisplayCompiler
{
    private static readonly IReadOnlyList<IRuleConditionDisplayRewrite> Rewrites =
    [
        new OptionalBoundDisplayRewrite(),
    ];

    public RuleExpressionDisplayNodeDto Compile(
        RuleConditionNode root,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language) =>
        ToContract(CompileNode(root, inputs, language), inputs, language);

    private static RuleDisplayNode CompileNode(
        RuleConditionNode node,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        RuleDisplayNode structural = node switch
        {
            RuleConditionGroup group => new RuleDisplayGroup(
                group.NodeId,
                group.Operator,
                group.Children.Select(child => CompileNode(child, inputs, language)).ToArray()),
            RulePredicateCondition predicate => CompilePredicate(predicate, inputs, language),
            _ => throw new InvalidOperationException("Rule condition node type is not supported."),
        };

        return Rewrites
            .Select(rewrite => rewrite.TryRewrite(structural))
            .FirstOrDefault(rewrite => rewrite is not null)
            ?? structural;
    }

    private static RuleDisplayPredicate CompilePredicate(
        RulePredicateCondition predicate,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        List<RuleExpressionDisplayTokenDto> tokens = [];
        AppendPredicate(tokens, predicate, inputs, language);
        return new(predicate.NodeId, predicate, tokens);
    }

    private static RuleExpressionDisplayNodeDto ToContract(
        RuleDisplayNode node,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language) =>
        node switch
        {
            RuleDisplayGroup group => new(
                group.NodeId,
                [
                    new(
                        GroupLabel(group.Operator, language),
                        ContractReferenceKind.LogicalOperator,
                        group.Operator.ToString()),
                ],
                group.Children.Select(child => ToContract(child, inputs, language)).ToArray()),
            RuleDisplayPredicate predicate => new(predicate.NodeId, predicate.Tokens, []),
            RuleDisplayConditional conditional => new(
                conditional.NodeId,
                ConditionalTokens(conditional, inputs, language),
                []),
            _ => throw new InvalidOperationException("Rule display node type is not supported."),
        };

    private static IReadOnlyList<RuleExpressionDisplayTokenDto> ConditionalTokens(
        RuleDisplayConditional conditional,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        List<RuleExpressionDisplayTokenDto> tokens = [];
        AppendInline(tokens, conditional.Consequent, inputs, language);
        tokens.Add(new(language == "vi" ? "khi" : "when"));
        AppendSpecifiedGuards(tokens, conditional.Guards, inputs, language);
        return tokens;
    }

    private static void AppendSpecifiedGuards(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        IReadOnlyList<RuleOperand> guards,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        for (int index = 0; index < guards.Count; index += 1)
        {
            if (index > 0)
            {
                AddReference(
                    tokens,
                    language == "vi" ? "và" : "and",
                    ContractReferenceKind.LogicalOperator,
                    DomainLogicalOperator.All.ToString());
            }

            AppendOperand(tokens, guards[index], inputs, language);
        }

        AddReference(
            tokens,
            guards.Count == 1 || language == "vi"
                ? language == "vi" ? "được chỉ định" : "is specified"
                : "are specified",
            ContractReferenceKind.PredicateOperator,
            DomainPredicateOperator.IsNotNull.ToString());
    }

    private static void AppendInline(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RuleDisplayNode node,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        switch (node)
        {
            case RuleDisplayPredicate predicate:
                foreach (RuleExpressionDisplayTokenDto token in predicate.Tokens)
                    tokens.Add(token);
                return;
            case RuleDisplayConditional conditional:
                tokens.Add(new("("));
                foreach (RuleExpressionDisplayTokenDto token in ConditionalTokens(conditional, inputs, language))
                    tokens.Add(token);
                tokens.Add(new(")"));
                return;
            case RuleDisplayGroup group when group.Children.Count == 1 &&
                group.Operator != DomainLogicalOperator.Not:
                AppendInline(tokens, group.Children[0], inputs, language);
                return;
            case RuleDisplayGroup group:
                tokens.Add(new("("));
                AppendInlineGroup(tokens, group, inputs, language);
                tokens.Add(new(")"));
                return;
            default:
                throw new InvalidOperationException("Rule display node type is not supported.");
        }
    }

    private static void AppendInlineGroup(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RuleDisplayGroup group,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        if (group.Operator == DomainLogicalOperator.Not)
        {
            AddReference(
                tokens,
                language == "vi" ? "không" : "not",
                ContractReferenceKind.LogicalOperator,
                DomainLogicalOperator.Not.ToString());
            AppendInline(tokens, group.Children[0], inputs, language);
            return;
        }

        string connector = group.Operator == DomainLogicalOperator.All
            ? language == "vi" ? "và" : "and"
            : language == "vi" ? "hoặc" : "or";
        for (int index = 0; index < group.Children.Count; index += 1)
        {
            if (index > 0)
            {
                AddReference(tokens, connector, ContractReferenceKind.LogicalOperator, group.Operator.ToString());
            }

            AppendInline(tokens, group.Children[index], inputs, language);
        }
    }

    private static string GroupLabel(DomainLogicalOperator @operator, string language) =>
        (@operator, language) switch
        {
            (DomainLogicalOperator.All, "vi") => "Tất cả điều kiện đều đúng",
            (DomainLogicalOperator.Any, "vi") => "Ít nhất một điều kiện đúng",
            (DomainLogicalOperator.Not, "vi") => "Điều này không đúng",
            (DomainLogicalOperator.All, _) => "All conditions must match",
            (DomainLogicalOperator.Any, _) => "Any condition may match",
            (DomainLogicalOperator.Not, _) => "This must not match",
            _ => throw new InvalidOperationException("Rule logical operator has no label."),
        };

    private static void AppendPredicate(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RulePredicateCondition predicate,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        if (!AppendBooleanFunctionPredicate(tokens, predicate, inputs, language) &&
            !AppendPresencePredicate(tokens, predicate, inputs, language))
        {
            AppendOperand(tokens, predicate.Left, inputs, language);
            AddReference(
                tokens,
                OperatorPhrase(predicate.Operator, language),
                ContractReferenceKind.PredicateOperator,
                predicate.Operator.ToString());
            if (predicate.Right is not null)
                AppendOperand(tokens, predicate.Right, inputs, language);
        }
    }

    private static bool AppendBooleanFunctionPredicate(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RulePredicateCondition predicate,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        if (predicate.Operator is not (DomainPredicateOperator.Equal or DomainPredicateOperator.NotEqual) ||
            predicate.Left.Kind != DomainOperandKind.Function ||
            predicate.Right?.Literal is not { Type: DomainValueType.Boolean, Values.Count: 1 } literal ||
            !bool.TryParse(literal.Values[0], out bool expected))
        {
            return false;
        }

        if (predicate.Operator == DomainPredicateOperator.NotEqual)
            expected = !expected;

        IReadOnlyList<RuleOperand> arguments = predicate.Left.Arguments;
        switch (predicate.Left.FunctionKind)
        {
            case DomainExpressionFunction.IsBlank when arguments.Count == 1:
                AppendOperand(tokens, arguments[0], inputs, language);
                AddFunctionReference(
                    tokens,
                    predicate.Left.FunctionKind.Value,
                    expected
                        ? language == "vi" ? "đang trống" : "is blank"
                        : language == "vi" ? "không trống" : "is not blank");
                return true;
            case DomainExpressionFunction.MatchesPattern when arguments.Count == 2:
                AppendOperand(tokens, arguments[0], inputs, language);
                AddFunctionReference(
                    tokens,
                    predicate.Left.FunctionKind.Value,
                    expected
                        ? language == "vi" ? "khớp" : "matches"
                        : language == "vi" ? "không khớp" : "does not match");
                AppendOperand(tokens, arguments[1], inputs, language);
                return true;
            case DomainExpressionFunction.HasFormat when arguments.Count == 2:
                AppendOperand(tokens, arguments[0], inputs, language);
                AddFunctionReference(
                    tokens,
                    predicate.Left.FunctionKind.Value,
                    expected
                        ? language == "vi" ? "đúng định dạng" : "has format"
                        : language == "vi" ? "không đúng định dạng" : "does not have format");
                AppendOperand(tokens, arguments[1], inputs, language);
                return true;
            default:
                return false;
        }
    }

    private static bool AppendPresencePredicate(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RulePredicateCondition predicate,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        if (predicate.Operator is not (DomainPredicateOperator.IsNull or DomainPredicateOperator.IsNotNull))
            return false;

        AppendOperand(tokens, predicate.Left, inputs, language);
        bool present = predicate.Operator == DomainPredicateOperator.IsNotNull;
        string phrase = predicate.Left.Kind == DomainOperandKind.Input
            ? present
                ? language == "vi" ? "được cung cấp" : "is provided"
                : language == "vi" ? "không được cung cấp" : "is not provided"
            : present
                ? language == "vi" ? "có giá trị" : "has a value"
                : language == "vi" ? "không có giá trị" : "has no value";
        AddReference(tokens, phrase, ContractReferenceKind.PredicateOperator, predicate.Operator.ToString());
        return true;
    }

    private static void AppendOperand(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RuleOperand operand,
        IReadOnlyList<RuleInputDefinition> inputs,
        string language)
    {
        if (operand.Kind == DomainOperandKind.Function)
        {
            DomainExpressionFunction function = operand.FunctionKind!.Value;
            if (operand.Arguments.Count == 1 &&
                function is DomainExpressionFunction.Length
                    or DomainExpressionFunction.Precision
                    or DomainExpressionFunction.Scale
                    or DomainExpressionFunction.Count)
            {
                AddFunctionReference(tokens, function, FunctionPrefix(function, language));
                AppendOperand(tokens, operand.Arguments[0], inputs, language);
                return;
            }
            if (operand.Arguments.Count == 1 && function == DomainExpressionFunction.ToDecimal)
            {
                AppendOperand(tokens, operand.Arguments[0], inputs, language);
                return;
            }

            RuleExpressionFunctionDefinition definition = RuleExpressionLanguage.Find(function)!;
            AddReference(
                tokens,
                Content(definition.Documentation, language).DisplayName,
                ContractReferenceKind.Function,
                function.ToString());
            tokens.Add(new("("));
            for (int index = 0; index < operand.Arguments.Count; index += 1)
            {
                if (index > 0)
                    tokens.Add(new(","));
                AppendOperand(tokens, operand.Arguments[index], inputs, language);
            }
            tokens.Add(new(")"));
            return;
        }

        if (operand.Kind == DomainOperandKind.Input)
        {
            RuleInputDefinition? input = inputs.SingleOrDefault(candidate =>
                candidate.Key.Equals(operand.Reference, StringComparison.Ordinal));
            tokens.Add(new(
                input?.Label ?? operand.Reference!,
                ContractReferenceKind.Input,
                operand.Reference));
            return;
        }

        string value = string.Join(", ", operand.Literal!.Values);
        tokens.Add(new(
            value,
            ContractReferenceKind.Literal,
            operand.Literal.Type.ToString(),
            IsCode: true));
    }

    private static string FunctionPrefix(DomainExpressionFunction function, string language) =>
        (function, language) switch
        {
            (DomainExpressionFunction.Length, "vi") => "Độ dài của",
            (DomainExpressionFunction.Precision, "vi") => "Tổng số chữ số của",
            (DomainExpressionFunction.Scale, "vi") => "Số chữ số thập phân của",
            (DomainExpressionFunction.Count, "vi") => "Số lượng giá trị trong",
            (DomainExpressionFunction.Length, _) => "Length of",
            (DomainExpressionFunction.Precision, _) => "Total digits in",
            (DomainExpressionFunction.Scale, _) => "Decimal places in",
            (DomainExpressionFunction.Count, _) => "Number of values in",
            _ => throw new InvalidOperationException("Rule calculation has no display prefix."),
        };

    private static void AddFunctionReference(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        DomainExpressionFunction function,
        string text) =>
        AddReference(tokens, text, ContractReferenceKind.Function, function.ToString());

    private static void AddReference(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        string text,
        ContractReferenceKind kind,
        string key) =>
        tokens.Add(new(text, kind, key));

    private static string LowerInitial(string value) =>
        string.IsNullOrEmpty(value)
            ? value
            : char.ToLowerInvariant(value[0]) + value[1..];

    private static string OperatorPhrase(DomainPredicateOperator @operator, string language)
    {
        if (language == "vi")
        {
            RulePredicateOperatorDefinition definition = RuleExpressionLanguage.Find(@operator)!;
            return LowerInitial(Content(definition.Documentation, language).DisplayName);
        }

        return @operator switch
        {
            DomainPredicateOperator.Equal => "equals",
            DomainPredicateOperator.NotEqual => "does not equal",
            DomainPredicateOperator.GreaterThan => "is greater than",
            DomainPredicateOperator.GreaterThanOrEqual => "is greater than or equal to",
            DomainPredicateOperator.LessThan => "is less than",
            DomainPredicateOperator.LessThanOrEqual => "is less than or equal to",
            DomainPredicateOperator.Contains => "contains",
            DomainPredicateOperator.StartsWith => "starts with",
            DomainPredicateOperator.EndsWith => "ends with",
            _ => throw new InvalidOperationException("Rule predicate operator has no display phrase."),
        };
    }

    private static RuleReferenceContent Content(
        RuleReferenceDocumentation documentation,
        string language) =>
        documentation.Locales.TryGetValue(language, out RuleReferenceContent? content)
            ? content
            : documentation.Locales["en"];
}

internal interface IRuleConditionDisplayRewrite
{
    RuleDisplayNode? TryRewrite(RuleDisplayNode node);
}

internal abstract record RuleDisplayNode(string NodeId);

internal sealed record RuleDisplayGroup(
    string NodeId,
    DomainLogicalOperator Operator,
    IReadOnlyList<RuleDisplayNode> Children) : RuleDisplayNode(NodeId);

internal sealed record RuleDisplayPredicate(
    string NodeId,
    RulePredicateCondition Predicate,
    IReadOnlyList<RuleExpressionDisplayTokenDto> Tokens) : RuleDisplayNode(NodeId);

internal sealed record RuleDisplayConditional(
    string NodeId,
    IReadOnlyList<RuleOperand> Guards,
    RuleDisplayNode Consequent) : RuleDisplayNode(NodeId);
