using System.Text.Json;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractReferenceKind = Axis.Rules.Contracts.RuleExpressionReferenceKind;
using DomainDecision = Axis.Rules.Domain.RuleDecision;
using DomainExpressionFunction = Axis.Rules.Domain.RuleExpressionFunction;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainOutcomeKind = Axis.Rules.Domain.RuleOutcomeKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

public sealed class RuleExpressionAuthoringService(RuleContextSchemaRegistry contextSchemas)
{
    public async Task<Result<RuleExpressionAuthoringDto>> AssistAsync(
        Guid workspaceId,
        AssistRuleExpressionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExpressionLanguageVersion != RuleExpressionLanguage.Version)
            return RuleDefinitionFailures.Invalid<RuleExpressionAuthoringDto>(
                "Rule expression language version is unavailable.");

        Result<IReadOnlyList<RuleParameterDefinition>> parameters = MapParameters(request.Parameters);
        if (parameters.IsFailure)
            return RuleDefinitionFailures.Invalid<RuleExpressionAuthoringDto>(parameters.Error);

        RuleContextSchema? schema = null;
        if (!string.IsNullOrWhiteSpace(request.ContextKey))
        {
            if (request.ContextSchemaVersion is not > 0)
                return RuleDefinitionFailures.Invalid<RuleExpressionAuthoringDto>(
                    "Rule context schema version is required.");

            schema = await contextSchemas.FindAsync(
                workspaceId,
                request.ContextKey,
                request.ContextSchemaVersion.Value,
                cancellationToken);
            if (schema is null)
                return RuleDefinitionFailures.Invalid<RuleExpressionAuthoringDto>(
                    "Rule context schema is unavailable.");
        }

        string language = NormalizeLanguage(request.Language);
        string syntax = request.Syntax ?? string.Empty;
        RuleConditionNode? condition = null;
        RuleExpressionDiagnosticDto? diagnostic = null;

        if (request.Condition is not null && request.Syntax is null)
        {
            Result<RuleConditionNode> mapped = RuleContractMapper.ToDomain(request.Condition);
            if (mapped.IsFailure)
                return RuleDefinitionFailures.Invalid<RuleExpressionAuthoringDto>(mapped.Error);
            condition = mapped.Value;
            syntax = Format(condition);
        }
        else if (request.Syntax is not null && request.Condition is null)
        {
            if (schema is null)
                return RuleDefinitionFailures.Invalid<RuleExpressionAuthoringDto>(
                    "Rule context schema is required for expression authoring.");

            try
            {
                condition = new Parser(syntax, schema, parameters.Value, language).Parse();
                Result valid = Validate(schema, parameters.Value, condition);
                if (valid.IsFailure)
                {
                    condition = null;
                    diagnostic = new(
                        "rules.expression.type_mismatch",
                        valid.Error,
                        0,
                        syntax.Length);
                }
                else
                {
                    syntax = Format(condition);
                }
            }
            catch (ParseException exception)
            {
                diagnostic = new(
                    exception.Code,
                    exception.Message,
                    exception.Start,
                    exception.Length);
            }
        }
        else
        {
            return RuleDefinitionFailures.Invalid<RuleExpressionAuthoringDto>(
                "Provide exactly one expression syntax or canonical condition.");
        }

        IReadOnlyList<RuleExpressionCompletionDto> completions = request.Syntax is null
            ? []
            : BuildCompletions(
                request.Syntax,
                request.CursorOffset,
                schema,
                parameters.Value,
                language);

        return new RuleExpressionAuthoringDto(
            syntax,
            condition is null ? null : RuleContractMapper.ToDto(condition),
            condition is null ? null : Project(condition, schema, parameters.Value, language),
            diagnostic is null ? [] : [diagnostic],
            completions);
    }

    private static Result Validate(
        RuleContextSchema schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        RuleConditionNode condition)
    {
        Result<RuleDecisionOutcome> outcome = RuleDecisionOutcome.Create(DomainDecision.Allow);
        return outcome.IsFailure
            ? Result.Failure(outcome.Error)
            : RuleDefinitionValidator.Validate(
                schema,
                parameters,
                condition,
                outcome.Value,
                DomainOutcomeKind.Decision);
    }

    private static Result<IReadOnlyList<RuleParameterDefinition>> MapParameters(
        IReadOnlyList<RuleParameterDefinitionDto>? parameterDtos)
    {
        List<RuleParameterDefinition> parameters = [];
        foreach (RuleParameterDefinitionDto dto in parameterDtos ?? [])
        {
            Result<RuleParameterDefinition> parameter = RuleContractMapper.ToDomain(dto);
            if (parameter.IsFailure)
                return Result.Failure<IReadOnlyList<RuleParameterDefinition>>(parameter.Error);
            parameters.Add(parameter.Value);
        }

        if (parameters.Select(parameter => parameter.Key).Distinct(StringComparer.Ordinal).Count() !=
            parameters.Count)
        {
            return Result.Failure<IReadOnlyList<RuleParameterDefinition>>(
                "Rule parameter keys must be unique.");
        }

        return parameters;
    }

    private static string NormalizeLanguage(string? language) =>
        language?.Trim().StartsWith("vi", StringComparison.OrdinalIgnoreCase) == true
            ? "vi"
            : "en";

    private static string Format(RuleConditionNode condition) =>
        FormatNode(condition, 0);

    private static string FormatNode(RuleConditionNode node, int depth)
    {
        if (node is RulePredicateCondition predicate)
        {
            string right = predicate.Right is null ? string.Empty : $" {FormatOperand(predicate.Right)}";
            return $"{FormatOperand(predicate.Left)} {predicate.Operator}{right}";
        }

        RuleConditionGroup group = (RuleConditionGroup)node;
        string indent = new(' ', depth * 2);
        string childIndent = new(' ', (depth + 1) * 2);
        string children = string.Join(
            ",\n",
            group.Children.Select(child => $"{childIndent}{FormatNode(child, depth + 1)}"));
        return $"{group.Operator}(\n{children}\n{indent})";
    }

    private static string FormatOperand(RuleOperand operand) => operand.Kind switch
    {
        DomainOperandKind.Context => $"@context.{operand.Reference}",
        DomainOperandKind.Parameter => $"@parameters.{operand.Reference}",
        DomainOperandKind.Function =>
            $"{operand.FunctionKind}({string.Join(", ", operand.Arguments.Select(FormatOperand))})",
        DomainOperandKind.Literal =>
            $"{operand.Literal!.Type}({string.Join(", ", operand.Literal.Values.Select(value => JsonSerializer.Serialize(value)))})",
        _ => throw new InvalidOperationException("Rule operand kind is not supported."),
    };

    private static RuleExpressionDisplayNodeDto Project(
        RuleConditionNode node,
        RuleContextSchema? schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        string language)
    {
        if (node is RuleConditionGroup group)
        {
            RuleExpressionDisplayTokenDto[] groupTokens =
            [
                new(
                    GroupJoiner(group.Operator, language),
                    ContractReferenceKind.LogicalOperator,
                    group.Operator.ToString()),
            ];
            return new(
                node.NodeId,
                groupTokens,
                group.Children.Select(child => Project(child, schema, parameters, language)).ToArray());
        }

        List<RuleExpressionDisplayTokenDto> tokens = [];
        AppendCondition(tokens, node, schema, parameters, language);
        return new(node.NodeId, tokens, []);
    }

    private static string GroupJoiner(DomainLogicalOperator @operator, string language) =>
        (@operator, language) switch
        {
            (DomainLogicalOperator.All, "vi") => "và",
            (DomainLogicalOperator.Any, "vi") => "hoặc",
            (DomainLogicalOperator.Not, "vi") => "không",
            (DomainLogicalOperator.All, _) => "and",
            (DomainLogicalOperator.Any, _) => "or",
            (DomainLogicalOperator.Not, _) => "not",
            _ => throw new InvalidOperationException(
                "Rule logical operator has no natural-language heading."),
        };

    private static void AppendCondition(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RuleConditionNode node,
        RuleContextSchema? schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        string language)
    {
        if (node is RulePredicateCondition predicate)
        {
            AppendPredicate(tokens, predicate, schema, parameters, language);
            return;
        }

        RuleConditionGroup group = (RuleConditionGroup)node;
        if (group.Operator == DomainLogicalOperator.Not)
        {
            AddReference(
                tokens,
                language == "vi" ? "không" : "not",
                ContractReferenceKind.LogicalOperator,
                group.Operator.ToString());
            AppendCondition(tokens, group.Children[0], schema, parameters, language);
            return;
        }

        for (int index = 0; index < group.Children.Count; index += 1)
        {
            if (index > 0)
            {
                if (group.Operator == DomainLogicalOperator.Any)
                    tokens.Add(new(","));
                AddReference(
                    tokens,
                    group.Operator == DomainLogicalOperator.All
                        ? language == "vi" ? "và" : "and"
                        : language == "vi" ? "hoặc" : "or",
                    ContractReferenceKind.LogicalOperator,
                    group.Operator.ToString());
            }
            AppendCondition(tokens, group.Children[index], schema, parameters, language);
        }
    }

    private static void AppendPredicate(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RulePredicateCondition predicate,
        RuleContextSchema? schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        string language)
    {
        if (!AppendBooleanFunctionPredicate(tokens, predicate, schema, parameters, language) &&
            !AppendPresencePredicate(tokens, predicate, schema, parameters, language))
        {
            AppendOperand(tokens, predicate.Left, schema, parameters, language);
            AddReference(
                tokens,
                OperatorPhrase(predicate.Operator, language),
                ContractReferenceKind.PredicateOperator,
                predicate.Operator.ToString());
            if (predicate.Right is not null)
                AppendOperand(tokens, predicate.Right, schema, parameters, language);
        }
    }

    private static bool AppendBooleanFunctionPredicate(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RulePredicateCondition predicate,
        RuleContextSchema? schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
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
                AppendOperand(tokens, arguments[0], schema, parameters, language);
                AddFunctionReference(
                    tokens,
                    predicate.Left.FunctionKind.Value,
                    expected
                        ? language == "vi" ? "đang trống" : "is blank"
                        : language == "vi" ? "không trống" : "is not blank");
                return true;
            case DomainExpressionFunction.MatchesPattern when arguments.Count == 2:
                AppendOperand(tokens, arguments[0], schema, parameters, language);
                AddFunctionReference(
                    tokens,
                    predicate.Left.FunctionKind.Value,
                    expected
                        ? language == "vi" ? "khớp" : "matches"
                        : language == "vi" ? "không khớp" : "does not match");
                AppendOperand(tokens, arguments[1], schema, parameters, language);
                return true;
            case DomainExpressionFunction.HasFormat when arguments.Count == 2:
                AppendOperand(tokens, arguments[0], schema, parameters, language);
                AddFunctionReference(
                    tokens,
                    predicate.Left.FunctionKind.Value,
                    expected
                        ? language == "vi" ? "đúng định dạng" : "has format"
                        : language == "vi" ? "không đúng định dạng" : "does not have format");
                AppendOperand(tokens, arguments[1], schema, parameters, language);
                return true;
            default:
                return false;
        }
    }

    private static bool AppendPresencePredicate(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RulePredicateCondition predicate,
        RuleContextSchema? schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        string language)
    {
        if (predicate.Operator is not (DomainPredicateOperator.IsNull or DomainPredicateOperator.IsNotNull))
            return false;

        AppendOperand(tokens, predicate.Left, schema, parameters, language);
        bool present = predicate.Operator == DomainPredicateOperator.IsNotNull;
        string phrase = predicate.Left.Kind == DomainOperandKind.Parameter
            ? present
                ? language == "vi" ? "được cung cấp" : "is provided"
                : language == "vi" ? "không được cung cấp" : "is not provided"
            : present
                ? language == "vi" ? "có giá trị" : "has a value"
                : language == "vi" ? "không có giá trị" : "has no value";
        AddReference(
            tokens,
            phrase,
            ContractReferenceKind.PredicateOperator,
            predicate.Operator.ToString());
        return true;
    }

    private static void AppendOperand(
        ICollection<RuleExpressionDisplayTokenDto> tokens,
        RuleOperand operand,
        RuleContextSchema? schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
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
                AddFunctionReference(
                    tokens,
                    function,
                    FunctionPrefix(function, language));
                AppendOperand(tokens, operand.Arguments[0], schema, parameters, language);
                return;
            }
            if (operand.Arguments.Count == 1 && function == DomainExpressionFunction.ToDecimal)
            {
                AppendOperand(tokens, operand.Arguments[0], schema, parameters, language);
                return;
            }
            if (operand.Arguments.Count == 1 && function == DomainExpressionFunction.IsBlank)
            {
                AppendOperand(tokens, operand.Arguments[0], schema, parameters, language);
                AddFunctionReference(
                    tokens,
                    function,
                    language == "vi" ? "đang trống" : "is blank");
                return;
            }
            if (operand.Arguments.Count == 2 &&
                function is DomainExpressionFunction.MatchesPattern or DomainExpressionFunction.HasFormat)
            {
                AppendOperand(tokens, operand.Arguments[0], schema, parameters, language);
                AddFunctionReference(
                    tokens,
                    function,
                    function == DomainExpressionFunction.MatchesPattern
                        ? language == "vi" ? "khớp" : "matches"
                        : language == "vi" ? "đúng định dạng" : "has format");
                AppendOperand(tokens, operand.Arguments[1], schema, parameters, language);
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
                AppendOperand(tokens, operand.Arguments[index], schema, parameters, language);
            }
            tokens.Add(new(")"));
            return;
        }

        if (operand.Kind == DomainOperandKind.Context)
        {
            RuleContextField? field = schema?.FindField(operand.Reference!);
            tokens.Add(new(
                field is null
                    ? SystemContextLabel(operand.Reference!, language)
                    : Content(field.Documentation, language).DisplayName,
                ContractReferenceKind.Context,
                operand.Reference));
            return;
        }

        if (operand.Kind == DomainOperandKind.Parameter)
        {
            tokens.Add(new(
                operand.Reference!,
                ContractReferenceKind.Parameter,
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
            _ => throw new InvalidOperationException("Rule expression function has no natural-language prefix."),
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

    private static string OperatorPhrase(
        DomainPredicateOperator @operator,
        string language)
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
            _ => throw new InvalidOperationException(
                "Rule predicate operator has no natural-language phrase."),
        };
    }

    private static IReadOnlyList<RuleExpressionCompletionDto> BuildCompletions(
        string syntax,
        int cursorOffset,
        RuleContextSchema? schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        string language)
    {
        int cursor = Math.Clamp(cursorOffset, 0, syntax.Length);
        int start = cursor;
        while (start > 0 && IsReferenceCharacter(syntax[start - 1]))
            start -= 1;
        string prefix = syntax[start..cursor];
        List<RuleExpressionCompletionDto> completions = [];

        foreach (RuleLogicalOperatorDefinition definition in RuleExpressionLanguage.LogicalOperators)
        {
            string key = definition.Operator.ToString();
            Add(
                completions,
                key,
                $"{key}()",
                key.Length + 1,
                ContractReferenceKind.LogicalOperator,
                key,
                Content(definition.Documentation, language).Summary,
                start,
                prefix.Length);
        }
        foreach (RulePredicateOperatorDefinition definition in RuleExpressionLanguage.Operators)
        {
            string key = definition.Operator.ToString();
            Add(
                completions,
                key,
                key,
                key.Length,
                ContractReferenceKind.PredicateOperator,
                key,
                Content(definition.Documentation, language).Summary,
                start,
                prefix.Length);
        }
        foreach (RuleExpressionFunctionDefinition definition in RuleExpressionLanguage.Functions)
        {
            string key = definition.Function.ToString();
            Add(
                completions,
                key,
                $"{key}()",
                key.Length + 1,
                ContractReferenceKind.Function,
                key,
                Content(definition.Documentation, language).Summary,
                start,
                prefix.Length);
        }
        foreach (RuleValueTypeDefinition definition in RuleExpressionLanguage.ValueTypes)
        {
            string key = definition.Type.ToString();
            Add(
                completions,
                key,
                $"{key}(\"\")",
                key.Length + 2,
                ContractReferenceKind.ValueType,
                key,
                Content(definition.Documentation, language).Summary,
                start,
                prefix.Length);
        }
        foreach (RuleContextField field in schema?.Fields ?? [])
        {
            string token = $"@context.{field.Path}";
            Add(
                completions,
                token,
                token,
                token.Length,
                ContractReferenceKind.Context,
                field.Path,
                Content(field.Documentation, language).Summary,
                start,
                prefix.Length);
        }
        foreach (RuleParameterDefinition parameter in parameters)
        {
            string token = $"@parameters.{parameter.Key}";
            Add(
                completions,
                token,
                token,
                token.Length,
                ContractReferenceKind.Parameter,
                parameter.Key,
                language == "vi" ? "Giá trị cấu hình của rule." : "A configured rule value.",
                start,
                prefix.Length);
        }

        return completions
            .OrderBy(completion => completion.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void Add(
        ICollection<RuleExpressionCompletionDto> completions,
        string label,
        string insertText,
        int cursorOffset,
        ContractReferenceKind kind,
        string key,
        string summary,
        int replacementStart,
        int replacementLength) =>
        completions.Add(new(
            label,
            insertText,
            cursorOffset,
            replacementStart,
            replacementLength,
            kind,
            key,
            summary));

    private static bool IsReferenceCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '.' or '@';

    private static RuleReferenceContent Content(
        RuleReferenceDocumentation documentation,
        string language) =>
        documentation.Locales.TryGetValue(language, out RuleReferenceContent? content)
            ? content
            : documentation.Locales["en"];

    private static string SystemContextLabel(string reference, string language) =>
        reference == "field.value"
            ? language == "vi" ? "Giá trị trường" : "Field value"
            : reference;

    private sealed class Parser(
        string source,
        RuleContextSchema schema,
        IReadOnlyList<RuleParameterDefinition> parameters,
        string language)
    {
        private readonly IReadOnlyList<Token> _tokens = Tokenize(source, language);
        private int _index;
        private int _nodeIndex;

        public RuleConditionNode Parse()
        {
            if (Current.Kind == TokenKind.End)
                throw Error("rules.expression.required", Localize("Expression is required.", "Cần nhập biểu thức."));

            RuleConditionNode condition = ParseCondition();
            if (Current.Kind != TokenKind.End)
                throw Error("rules.expression.unexpected_token", Localize("Unexpected token.", "Token không hợp lệ."));
            return condition;
        }

        private RuleConditionNode ParseCondition()
        {
            if (Current.Kind == TokenKind.Word &&
                Enum.TryParse(Current.Value, ignoreCase: false, out DomainLogicalOperator logical) &&
                Peek.Kind == TokenKind.Open)
            {
                Advance();
                Expect(TokenKind.Open);
                List<RuleConditionNode> children = [];
                if (Current.Kind != TokenKind.Close)
                {
                    while (true)
                    {
                        children.Add(ParseCondition());
                        if (Current.Kind != TokenKind.Comma)
                            break;
                        Advance();
                    }
                }
                Expect(TokenKind.Close);
                Result<RuleConditionGroup> group = RuleConditionGroup.Create(
                    NextNodeId(),
                    logical,
                    children);
                if (group.IsFailure)
                    throw Error("rules.expression.invalid_group", group.Error);
                return group.Value;
            }

            RuleOperand left = ParseOperand();
            Token operatorToken = Expect(TokenKind.Word);
            if (!Enum.TryParse(
                    operatorToken.Value,
                    ignoreCase: false,
                    out DomainPredicateOperator predicateOperator) ||
                RuleExpressionLanguage.Find(predicateOperator) is not { } definition)
            {
                throw new ParseException(
                    "rules.expression.operator_expected",
                    Localize("A registered predicate operator is required.", "Cần một toán tử điều kiện đã đăng ký."),
                    operatorToken.Start,
                    operatorToken.Length);
            }

            RuleOperand? right = definition.RightShapes.Count == 0 ? null : ParseOperand();
            Result<RulePredicateCondition> predicate = RulePredicateCondition.Create(
                NextNodeId(),
                predicateOperator,
                left,
                right);
            if (predicate.IsFailure)
                throw Error("rules.expression.invalid_predicate", predicate.Error);
            return predicate.Value;
        }

        private RuleOperand ParseOperand()
        {
            Token name = Expect(TokenKind.Word);
            if (Current.Kind == TokenKind.Open)
            {
                Advance();
                if (Enum.TryParse(
                        name.Value,
                        ignoreCase: false,
                        out DomainExpressionFunction function) &&
                    RuleExpressionLanguage.Find(function) is not null)
                {
                    List<RuleOperand> arguments = [];
                    if (Current.Kind != TokenKind.Close)
                    {
                        while (true)
                        {
                            arguments.Add(ParseOperand());
                            if (Current.Kind != TokenKind.Comma)
                                break;
                            Advance();
                        }
                    }
                    Expect(TokenKind.Close);
                    Result<RuleOperand> operand = RuleOperand.Function(function, arguments);
                    if (operand.IsFailure)
                        throw Error("rules.expression.invalid_function", operand.Error);
                    return operand.Value;
                }

                if (Enum.TryParse(name.Value, ignoreCase: false, out DomainValueType valueType))
                {
                    List<string> values = [];
                    if (Current.Kind != TokenKind.Close)
                    {
                        while (true)
                        {
                            Token value = Current.Kind is TokenKind.String or TokenKind.Word
                                ? Advance()
                                : throw Error(
                                    "rules.expression.literal_expected",
                                    Localize("A literal value is required.", "Cần một giá trị cố định."));
                            values.Add(value.Value);
                            if (Current.Kind != TokenKind.Comma)
                                break;
                            Advance();
                        }
                    }
                    Expect(TokenKind.Close);
                    Result<RuleValue> literal = RuleValue.Create(
                        valueType,
                        values,
                        allowMultiple: values.Count > 1);
                    if (literal.IsFailure)
                        throw new ParseException(
                            "rules.expression.invalid_literal",
                            literal.Error,
                            name.Start,
                            Math.Max(1, Current.Start - name.Start));
                    return RuleOperand.LiteralValue(literal.Value).Value;
                }

                throw new ParseException(
                    "rules.expression.callable_unknown",
                    Localize("Function or value type is not registered.", "Function hoặc kiểu giá trị chưa được đăng ký."),
                    name.Start,
                    name.Length);
            }

            if (!name.Value.StartsWith('@'))
            {
                throw new ParseException(
                    "rules.expression.reference_expected",
                    Localize(
                        "A dynamic reference must use the @context. or @parameters. namespace.",
                        "Tham chiếu động phải dùng namespace @context. hoặc @parameters."),
                    name.Start,
                    name.Length);
            }

            const string contextPrefix = "@context.";
            if (name.Value.StartsWith(contextPrefix, StringComparison.Ordinal))
            {
                string reference = name.Value[contextPrefix.Length..];
                if (reference.Length > 0 && schema.FindField(reference) is not null)
                    return RuleOperand.Context(reference).Value;
                throw new ParseException(
                    "rules.expression.context_unknown",
                    Localize(
                        "Context value is not available in the selected context.",
                        "Giá trị context không có trong context đã chọn."),
                    name.Start,
                    name.Length);
            }

            const string parametersPrefix = "@parameters.";
            if (name.Value.StartsWith(parametersPrefix, StringComparison.Ordinal))
            {
                string reference = name.Value[parametersPrefix.Length..];
                if (reference.Length > 0 && parameters.Any(parameter =>
                        parameter.Key.Equals(reference, StringComparison.Ordinal)))
                {
                    return RuleOperand.Parameter(reference).Value;
                }
                throw new ParseException(
                    "rules.expression.parameter_unknown",
                    Localize(
                        "Parameter is not defined by this rule.",
                        "Parameter chưa được khai báo trong rule này."),
                    name.Start,
                    name.Length);
            }

            throw new ParseException(
                "rules.expression.reference_namespace_expected",
                Localize(
                    "Dynamic references use the @context. or @parameters. namespace.",
                    "Tham chiếu động dùng namespace @context. hoặc @parameters."),
                name.Start,
                name.Length);
        }

        private Token Expect(TokenKind kind)
        {
            if (Current.Kind != kind)
            {
                throw Error(
                    "rules.expression.unexpected_token",
                    Localize($"Expected {kind.ToString().ToLowerInvariant()}.", $"Cần {kind.ToString().ToLowerInvariant()}."));
            }
            return Advance();
        }

        private Token Advance() => _tokens[_index++];
        private Token Current => _tokens[_index];
        private Token Peek => _tokens[Math.Min(_index + 1, _tokens.Count - 1)];
        private string NextNodeId() => $"syntax-{++_nodeIndex}";
        private string Localize(string english, string vietnamese) =>
            language == "vi" ? vietnamese : english;
        private ParseException Error(string code, string message) =>
            new(code, message, Current.Start, Math.Max(1, Current.Length));
    }

    private static IReadOnlyList<Token> Tokenize(string source, string language)
    {
        List<Token> tokens = [];
        int index = 0;
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index += 1;
                continue;
            }

            char current = source[index];
            if (current is '(' or ')' or ',')
            {
                tokens.Add(new(
                    current == '(' ? TokenKind.Open : current == ')' ? TokenKind.Close : TokenKind.Comma,
                    current.ToString(),
                    index,
                    1));
                index += 1;
                continue;
            }

            if (current == '"')
            {
                int start = index;
                index += 1;
                bool escaped = false;
                while (index < source.Length)
                {
                    char character = source[index++];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (character == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (character == '"')
                        break;
                }
                if (source[index - 1] != '"')
                {
                    throw new ParseException(
                        "rules.expression.string_unterminated",
                        language == "vi" ? "Chuỗi chưa được đóng." : "String is not terminated.",
                        start,
                        source.Length - start);
                }

                string raw = source[start..index];
                string? value;
                try
                {
                    value = JsonSerializer.Deserialize<string>(raw);
                }
                catch (JsonException)
                {
                    throw new ParseException(
                        "rules.expression.string_invalid",
                        language == "vi" ? "Chuỗi không hợp lệ." : "String is invalid.",
                        start,
                        index - start);
                }
                tokens.Add(new(TokenKind.String, value ?? string.Empty, start, index - start));
                continue;
            }

            int wordStart = index;
            while (index < source.Length &&
                   !char.IsWhiteSpace(source[index]) &&
                   source[index] is not '(' and not ')' and not ',' and not '"')
            {
                index += 1;
            }
            tokens.Add(new(TokenKind.Word, source[wordStart..index], wordStart, index - wordStart));
        }
        tokens.Add(new(TokenKind.End, string.Empty, source.Length, 0));
        return tokens;
    }

    private enum TokenKind
    {
        Word,
        String,
        Open,
        Close,
        Comma,
        End,
    }

    private sealed record Token(TokenKind Kind, string Value, int Start, int Length);

    private sealed class ParseException(
        string code,
        string message,
        int start,
        int length) : Exception(message)
    {
        public string Code { get; } = code;
        public int Start { get; } = start;
        public int Length { get; } = length;
    }
}
