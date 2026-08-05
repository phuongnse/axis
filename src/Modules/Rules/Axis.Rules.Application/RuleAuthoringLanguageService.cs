using System.Text.Encodings.Web;
using System.Text.Json;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using DomainExpressionFunction = Axis.Rules.Domain.RuleExpressionFunction;
using DomainLogicalOperator = Axis.Rules.Domain.RuleLogicalOperator;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainPredicateOperator = Axis.Rules.Domain.RulePredicateOperator;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

/// <summary>Projects the v1 non-executable authoring language to canonical rule conditions.</summary>
public sealed class RuleAuthoringLanguageService
{
    private const int MaxSourceLength = 32 * 1024;
    private const int MaxTokens = 4096;
    private const int MaxCompletions = 50;
    private static readonly RuleConditionDisplayCompiler DisplayCompiler = new();
    private static readonly JsonSerializerOptions DslJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public RuleAuthoringProjectionDto Project(
        RuleAuthoringSourceDto source,
        IReadOnlyList<RuleInputDefinitionDto> inputs,
        int expressionLanguageVersion,
        string? language)
    {
        if (source is null || inputs is null)
            return Invalid("authoring.invalid", "Authoring source and inputs are required.", 0, 0);
        if (expressionLanguageVersion != RuleExpressionLanguage.Version)
            return Invalid("authoring.version", "Rule expression language version is unavailable.", 0, 0);

        Result<IReadOnlyList<RuleInputDefinition>> mappedInputs = MapInputs(inputs);
        if (mappedInputs.IsFailure)
            return Invalid("authoring.inputs", mappedInputs.Error, 0, 0);

        ParseResult parsed = source switch
        {
            { Text: not null, Ast: null } => new Parser(source.Text).Parse(),
            { Text: null, Ast: not null } => FromAst(source.Ast),
            _ => ParseResult.Error("authoring.source", "Provide exactly one authoring source.", 0, 0),
        };
        if (parsed.Diagnostic is not null)
            return new(null, null, null, [parsed.Diagnostic]);

        RuleConditionNode canonical = NormalizeNodeIds(parsed.Condition!);
        Result valid = RuleDefinitionValidator.Validate(mappedInputs.Value, canonical, RuleOutputContract.BooleanMatch);
        if (valid.IsFailure)
            return Invalid("authoring.semantic", valid.Error, 0, source.Text?.Length ?? 0);

        return new(
            RuleContractMapper.ToDto(canonical),
            Format(canonical),
            DisplayCompiler.Compile(canonical, mappedInputs.Value, NormalizeLanguage(language)),
            []);
    }

    public IReadOnlyList<RuleAuthoringCompletionDto> Complete(
        string? text,
        int cursor,
        IReadOnlyList<RuleInputDefinitionDto> inputs,
        int expressionLanguageVersion)
    {
        if (expressionLanguageVersion != RuleExpressionLanguage.Version)
            return [];
        string source = text ?? string.Empty;
        int position = Math.Clamp(cursor, 0, source.Length);
        int start = position;
        while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] == '_'))
            start -= 1;
        string prefix = source[start..position];
        IEnumerable<(string Label, string InsertText, string Kind)> candidates =
        [
            ("all", "all()", "logical"), ("any", "any()", "logical"), ("not", "not()", "logical"),
            ("input", "input(\"key\")", "input"), ("text", "text(\"value\")", "literal"),
            ("integer", "integer(0)", "literal"), ("decimal", "decimal(0.0)", "literal"),
            ("boolean", "boolean(true)", "literal"), ("date", "date(\"YYYY-MM-DD\")", "literal"),
            ("datetime", "datetime(\"YYYY-MM-DDTHH:mm:ssZ\")", "literal"),
            .. Enum.GetNames<DomainPredicateOperator>().Select(name => (Lower(name), Lower(name) + "()", "predicate")),
            .. Enum.GetNames<DomainExpressionFunction>().Select(name => (Lower(name), Lower(name) + "()", "function")),
            .. inputs.OrderBy(input => input.Key, StringComparer.Ordinal).Select(input => (input.Key, $"input(\"{input.Key}\")", "input")),
        ];
        return candidates.Where(candidate => candidate.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(candidate => candidate.Label, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.Label, StringComparer.Ordinal)
            .Take(MaxCompletions)
            .Select(candidate => new RuleAuthoringCompletionDto(candidate.Label, candidate.InsertText, candidate.Kind, start, position - start))
            .ToArray();
    }

    private static Result<IReadOnlyList<RuleInputDefinition>> MapInputs(IReadOnlyList<RuleInputDefinitionDto> inputs)
    {
        if (inputs.Count > RuleEvaluationLimits.Default.MaxInputs)
            return Result.Failure<IReadOnlyList<RuleInputDefinition>>("Rule definition exceeds the maximum input count.");
        List<RuleInputDefinition> result = [];
        foreach (RuleInputDefinitionDto input in inputs)
        {
            Result<RuleInputDefinition> mapped = RuleContractMapper.ToDomain(input);
            if (mapped.IsFailure) return Result.Failure<IReadOnlyList<RuleInputDefinition>>(mapped.Error);
            result.Add(mapped.Value);
        }
        return result;
    }

    private static ParseResult FromAst(RuleConditionNodeDto ast)
    {
        Result<RuleConditionNode> mapped = RuleContractMapper.ToDomain(ast);
        return mapped.IsSuccess ? new(mapped.Value, null) : ParseResult.Error("authoring.ast", mapped.Error, 0, 0);
    }

    private static RuleConditionNode NormalizeNodeIds(RuleConditionNode node, string path = "0") => node switch
    {
        RuleConditionGroup group => RuleConditionGroup.Create(path, group.Operator,
            group.Children.Select((child, index) => NormalizeNodeIds(child, $"{path}.{index}")).ToArray()).Value,
        RulePredicateCondition predicate => RulePredicateCondition.Create(path, predicate.Operator, predicate.Left, predicate.Right).Value,
        _ => throw new InvalidOperationException("Rule condition node type is not supported."),
    };

    private static string Format(RuleConditionNode node) => node switch
    {
        RuleConditionGroup group => $"{Lower(group.Operator.ToString())}({string.Join(", ", group.Children.Select(Format))})",
        RulePredicateCondition predicate when predicate.Right is null => $"{Lower(predicate.Operator.ToString())}({Format(predicate.Left)})",
        RulePredicateCondition predicate => $"{Lower(predicate.Operator.ToString())}({Format(predicate.Left)}, {Format(predicate.Right!)})",
        _ => throw new InvalidOperationException("Rule condition node type is not supported."),
    };

    private static string Format(RuleOperand operand) => operand.Kind switch
    {
        DomainOperandKind.Input => $"input({JsonSerializer.Serialize(operand.Reference!, DslJsonOptions)})",
        DomainOperandKind.Literal => Format(operand.Literal!),
        DomainOperandKind.Function => $"{Lower(operand.FunctionKind!.Value.ToString())}({string.Join(", ", operand.Arguments.Select(Format))})",
        _ => throw new InvalidOperationException("Rule operand kind is not supported."),
    };

    private static string Format(RuleValue value) => value.Type switch
    {
        DomainValueType.Text => $"text({JsonSerializer.Serialize(value.Values[0], DslJsonOptions)})",
        DomainValueType.Integer => $"integer({value.Values[0]})",
        DomainValueType.Decimal => $"decimal({value.Values[0]})",
        DomainValueType.Date => $"date({JsonSerializer.Serialize(value.Values[0], DslJsonOptions)})",
        DomainValueType.DateTime => $"datetime({JsonSerializer.Serialize(value.Values[0], DslJsonOptions)})",
        DomainValueType.Boolean => $"boolean({value.Values[0]})",
        _ => throw new InvalidOperationException("Rule literal type is not supported."),
    };

    private static string Lower(string value) => char.ToLowerInvariant(value[0]) + value[1..];
    private static string NormalizeLanguage(string? language) => language?.Trim().StartsWith("vi", StringComparison.OrdinalIgnoreCase) == true ? "vi" : "en";
    private static RuleAuthoringProjectionDto Invalid(string code, string message, int start, int length) => new(null, null, null, [new(code, message, start, length)]);

    private sealed record ParseResult(RuleConditionNode? Condition, RuleAuthoringDiagnosticDto? Diagnostic)
    {
        public static ParseResult Error(string code, string message, int start, int length) => new(null, new(code, message, start, length));
    }

    private sealed class Parser(string text)
    {
        private readonly string _text = text;
        private List<Token> _tokens = [];
        private int _index;

        public ParseResult Parse()
        {
            if (_text.Length > MaxSourceLength) return ParseResult.Error("authoring.limit", "Authoring source exceeds 32 KiB.", 0, _text.Length);
            _tokens = Tokenize(_text);
            if (_tokens.Count > MaxTokens) return ParseResult.Error("authoring.limit", "Authoring source exceeds the maximum token count.", 0, _text.Length);
            try
            {
                RuleConditionNode condition = Condition();
                if (Current.Kind != TokenKind.End) throw Error("Unexpected token.");
                return new(condition, null);
            }
            catch (ParseException error) { return ParseResult.Error(error.Code, error.Message, error.Start, error.Length); }
        }

        private RuleConditionNode Condition()
        {
            Token name = Require(TokenKind.Identifier, "A condition function is required."); Require(TokenKind.Open, "'(' is required after the condition function.");
            if (TryLogical(name.Text, out DomainLogicalOperator logical))
            {
                List<RuleConditionNode> children = [];
                if (Current.Kind != TokenKind.Close) do { children.Add(Condition()); } while (Take(TokenKind.Comma));
                Require(TokenKind.Close, "')' is required.");
                Result<RuleConditionGroup> group = RuleConditionGroup.Create("parsed", logical, children);
                if (group.IsFailure) throw Error(group.Error, name);
                return group.Value;
            }
            if (!Enum.TryParse(name.Text, true, out DomainPredicateOperator predicate) || Lower(predicate.ToString()) != name.Text)
                throw Error("authoring.syntax", "The condition function is not registered.", name);
            RuleOperand left = Operand(); RuleOperand? right = null;
            if (Take(TokenKind.Comma)) right = Operand();
            Require(TokenKind.Close, "')' is required.");
            Result<RulePredicateCondition> result = RulePredicateCondition.Create("parsed", predicate, left, right);
            if (result.IsFailure) throw Error(result.Error, name);
            return result.Value;
        }

        private RuleOperand Operand()
        {
            Token name = Require(TokenKind.Identifier, "An operand is required.");
            Require(TokenKind.Open, "'(' is required after the operand function.");
            if (name.Text == "input")
            {
                Token key = Require(TokenKind.String, "input requires a quoted key."); Require(TokenKind.Close, "')' is required.");
                Result<RuleOperand> input = RuleOperand.Input(key.Text); if (input.IsFailure) throw Error(input.Error, key); return input.Value;
            }
            if (name.Text is "text" or "date" or "datetime")
            {
                Token value = Require(TokenKind.String, $"{name.Text} requires a quoted value."); Require(TokenKind.Close, "')' is required.");
                return Literal(name.Text switch { "text" => DomainValueType.Text, "date" => DomainValueType.Date, _ => DomainValueType.DateTime }, value.Text);
            }
            if (name.Text is "integer" or "decimal")
            {
                Token value = Current.Kind is TokenKind.Number or TokenKind.String
                    ? Advance()
                    : throw Error($"{name.Text} requires a number or quoted number.");
                Require(TokenKind.Close, "')' is required.");
                return Literal(name.Text == "integer" ? DomainValueType.Integer : DomainValueType.Decimal, value.Text);
            }
            if (name.Text == "boolean")
            {
                Token value = Require(TokenKind.Identifier, "boolean requires true or false.");
                if (value.Text is not ("true" or "false")) throw Error("boolean requires true or false.", value);
                Require(TokenKind.Close, "')' is required.");
                return Literal(DomainValueType.Boolean, value.Text);
            }
            if (!Enum.TryParse(name.Text, true, out DomainExpressionFunction function) || Lower(function.ToString()) != name.Text)
                throw Error("authoring.syntax", "The operand function is not registered.", name);
            List<RuleOperand> arguments = [];
            if (Current.Kind != TokenKind.Close) do { arguments.Add(Operand()); } while (Take(TokenKind.Comma));
            Require(TokenKind.Close, "')' is required.");
            Result<RuleOperand> result = RuleOperand.Function(function, arguments); if (result.IsFailure) throw Error(result.Error, name); return result.Value;
        }

        private RuleOperand Literal(DomainValueType type, string value)
        {
            Result<RuleValue> literal = RuleValue.Create(type, [value]); if (literal.IsFailure) throw Error(literal.Error);
            return RuleOperand.LiteralValue(literal.Value).Value;
        }

        private bool TryLogical(string name, out DomainLogicalOperator logical) => Enum.TryParse(name, true, out logical) && Lower(logical.ToString()) == name;
        private Token Current => _tokens[Math.Min(_index, _tokens.Count - 1)];
        private Token Advance() => _tokens[_index++];
        private bool Take(TokenKind kind) { if (Current.Kind != kind) return false; _index += 1; return true; }
        private Token Require(TokenKind kind, string message) { if (Current.Kind != kind) throw Error(message); return Advance(); }
        private ParseException Error(string message, Token? token = null) => Error("authoring.syntax", message, token ?? Current);
        private static ParseException Error(string code, string message, Token token) => new(code, message, token.Start, token.Length);

        private static List<Token> Tokenize(string source)
        {
            List<Token> result = []; int index = 0;
            while (index < source.Length)
            {
                if (char.IsWhiteSpace(source[index])) { index++; continue; }
                int start = index; char current = source[index];
                if (current is '(' or ')' or ',') { result.Add(new(current == '(' ? TokenKind.Open : current == ')' ? TokenKind.Close : TokenKind.Comma, current.ToString(), start, 1)); index++; continue; }
                if (current == '"')
                {
                    index++;
                    while (index < source.Length && source[index] != '"')
                    {
                        if (source[index] == '\\') index += 2;
                        else index += 1;
                    }
                    if (index >= source.Length) { result.Add(new(TokenKind.Invalid, "", start, source.Length - start)); break; }
                    index++;
                    string literal = source[start..index];
                    try { result.Add(new(TokenKind.String, JsonSerializer.Deserialize<string>(literal)!, start, index - start)); }
                    catch (JsonException) { result.Add(new(TokenKind.Invalid, literal, start, index - start)); }
                    continue;
                }
                if (char.IsLetter(current) || current == '_') { while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_')) index++; result.Add(new(TokenKind.Identifier, source[start..index], start, index - start)); continue; }
                if (char.IsDigit(current) || current == '-') { index++; while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.')) index++; result.Add(new(TokenKind.Number, source[start..index], start, index - start)); continue; }
                result.Add(new(TokenKind.Invalid, current.ToString(), start, 1)); index++;
            }
            result.Add(new(TokenKind.End, string.Empty, source.Length, 0)); return result;
        }
    }

    private sealed record Token(TokenKind Kind, string Text, int Start, int Length);
    private enum TokenKind { Identifier, String, Number, Open, Close, Comma, Invalid, End }
    private sealed class ParseException(string code, string message, int start, int length) : Exception(message) { public string Code { get; } = code; public int Start { get; } = start; public int Length { get; } = length; }
}
