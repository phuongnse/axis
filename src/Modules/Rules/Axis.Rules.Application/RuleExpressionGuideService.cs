using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Axis.Rules.Application.Search;
using Axis.Rules.Contracts;
using Axis.Rules.Domain;
using Axis.Shared.Domain.Primitives;
using ContractReferenceKind = Axis.Rules.Contracts.RuleExpressionReferenceKind;
using DomainCardinality = Axis.Rules.Domain.RuleExpressionCardinality;
using DomainOperandKind = Axis.Rules.Domain.RuleOperandKind;
using DomainValueType = Axis.Rules.Domain.RuleValueType;

namespace Axis.Rules.Application;

public sealed partial class RuleExpressionGuideService(
    RuleContextSchemaRegistry contextSchemas,
    IRuleTextSearchProvider search)
{
    public async Task<Result<RuleExpressionGuideDto>> SearchAsync(
        Guid workspaceId,
        SearchRuleExpressionGuideRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ExpressionLanguageVersion != RuleExpressionLanguage.Version)
        {
            return RuleDefinitionFailures.Invalid<RuleExpressionGuideDto>(
                "Rule expression language version is unavailable.");
        }

        RuleContextSchema? schema = null;
        if (!string.IsNullOrWhiteSpace(request.ContextKey))
        {
            if (request.ContextSchemaVersion is not > 0)
            {
                return RuleDefinitionFailures.Invalid<RuleExpressionGuideDto>(
                    "Rule context schema version is required.");
            }

            schema = await contextSchemas.FindAsync(
                workspaceId,
                request.ContextKey,
                request.ContextSchemaVersion.Value,
                cancellationToken);
            if (schema is null)
            {
                return RuleDefinitionFailures.Invalid<RuleExpressionGuideDto>(
                    "Rule context schema is unavailable.");
            }
        }

        string language = NormalizeLanguage(request.Language);
        List<GuideSection> sections =
        [
            ContextSection(schema, request.DefinitionKey, language),
            ParameterSection(request.Parameters, language),
            LogicalOperatorSection(language),
            PredicateOperatorSection(language),
            FunctionSection(language),
            ValueTypeSection(language),
            OperandKindSection(language),
            LimitSection(language),
        ];
        sections.RemoveAll(section => section.Items.Count == 0);

        string query = request.Query?.Trim() ?? string.Empty;
        IReadOnlyDictionary<string, int>? order = null;
        if (query.Length > 0)
        {
            RuleTextSearchDocument[] documents = sections
                .SelectMany(section => section.Items)
                .Select(item => new RuleTextSearchDocument(
                    item.SearchKey,
                    item.Content.DisplayName,
                    string.Join(
                        ' ',
                        item.ReferenceKey,
                        item.Content.Summary,
                        item.Content.Usage,
                        item.Detail)))
                .ToArray();
            IReadOnlyList<RuleTextSearchMatch> matches =
                await search.SearchAsync(documents, query, cancellationToken);
            order = matches
                .Select((match, index) => (match.Key, index))
                .ToDictionary(match => match.Key, match => match.index, StringComparer.Ordinal);
        }

        IEnumerable<GuideSection> orderedSections = sections;
        if (order is not null)
        {
            orderedSections = sections
                .Where(section => section.Items.Any(item => order.ContainsKey(item.SearchKey)))
                .OrderBy(section => section.Items
                    .Where(item => order.ContainsKey(item.SearchKey))
                    .Min(item => order[item.SearchKey]));
        }

        RuleExpressionGuideSectionDto[] visible = orderedSections
            .Select(section => ToDto(section, query, order))
            .Where(section => section.Items.Count > 0)
            .ToArray();

        return new RuleExpressionGuideDto(
            RuleExpressionLanguage.Version,
            visible.Sum(section => section.Items.Count),
            visible);
    }

    private static GuideSection ContextSection(
        RuleContextSchema? schema,
        string? definitionKey,
        string language)
    {
        List<GuideItem> items = [];
        if (schema is not null)
        {
            items.AddRange(schema.Fields.Select(field => Item(
                ContractReferenceKind.Context,
                field.Path,
                Content(field.Documentation, language),
                $"@context.{field.Path} · {ValueTypeName(field.Type, language)}" +
                (field.AllowMultiple ? $" · {CardinalityName(DomainCardinality.Multiple, language)}" : string.Empty))));
        }
        else if (!string.IsNullOrWhiteSpace(definitionKey))
        {
            SystemRuleDefinition? definition = SystemRuleCatalog.Definitions
                .Where(candidate => candidate.Key.Value.Equals(definitionKey.Trim(), StringComparison.Ordinal))
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault();
            if (definition is not null)
            {
                foreach (string reference in ContextReferences(definition.Condition))
                {
                    string targetTypes = string.Join(", ", definition.Applicability.TargetTypeKeys);
                    items.Add(Item(
                        ContractReferenceKind.Context,
                        reference,
                        language == "vi"
                            ? new(
                                "Giá trị trường",
                                "Giá trị được context sản phẩm cung cấp khi rule chạy.",
                                "Dùng giá trị này trong các operator và function tương thích.",
                                [$"@context.{reference}"])
                            : new(
                                "Field value",
                                "The value supplied by the product context when the rule runs.",
                                "Use this value with compatible operators and functions.",
                                [$"@context.{reference}"]),
                        $"@context.{reference} · {targetTypes}"));
                }
            }
        }

        return new(
            "context",
            language == "vi" ? "Context hiện tại" : "Current context",
            language == "vi"
                ? "Các giá trị có kiểu mà rule hiện tại có thể đọc."
                : "Typed values the current rule can read.",
            items);
    }

    private static GuideSection ParameterSection(
        IReadOnlyList<RuleParameterDefinitionDto> parameters,
        string language)
    {
        List<GuideItem> items = parameters
            .OrderBy(parameter => parameter.Key, StringComparer.Ordinal)
            .Select(parameter =>
            {
                string typeName = ValueTypeName((DomainValueType)parameter.Type, language);
                string cardinality = CardinalityName(
                    parameter.AllowMultiple
                        ? DomainCardinality.Multiple
                        : DomainCardinality.Scalar,
                    language);
                RuleReferenceContent content = language == "vi"
                    ? new(
                        $"@parameters.{parameter.Key}",
                        $"Parameter {typeName} được cấu hình khi áp dụng rule.",
                        "Tham chiếu stable key này trong biểu thức; giá trị được kiểm tra theo contract parameter.",
                        [$"@parameters.{parameter.Key}"])
                    : new(
                        $"@parameters.{parameter.Key}",
                        $"A {typeName} parameter configured when the rule is applied.",
                        "Reference this stable key in the expression; its value is checked against the parameter contract.",
                        [$"@parameters.{parameter.Key}"]);
                string required = parameter.IsRequired
                    ? language == "vi" ? "bắt buộc" : "required"
                    : language == "vi" ? "tùy chọn" : "optional";
                return Item(
                    ContractReferenceKind.Parameter,
                    parameter.Key,
                    content,
                    $"{typeName} · {cardinality} · {required}");
            })
            .ToList();

        return new(
            "parameters",
            language == "vi" ? "Parameters của rule" : "Rule parameters",
            language == "vi"
                ? "Các giá trị cấu hình có stable key riêng của rule hiện tại."
                : "Configured values with stable keys owned by the current rule.",
            items);
    }

    private static GuideSection LogicalOperatorSection(string language) =>
        new(
            "groups",
            language == "vi" ? "Kết hợp điều kiện" : "Combining conditions",
            language == "vi"
                ? "Dùng để tạo cấu trúc and, or và not nhiều cấp."
                : "Build nested and, or, and not condition structures.",
            RuleExpressionLanguage.LogicalOperators.Select(definition =>
            {
                string childRange = definition.MaximumChildren is null
                    ? language == "vi"
                        ? $"Từ {definition.MinimumChildren} điều kiện con"
                        : $"At least {definition.MinimumChildren} child condition(s)"
                    : language == "vi"
                        ? $"{definition.MinimumChildren}–{definition.MaximumChildren} điều kiện con"
                        : $"{definition.MinimumChildren}–{definition.MaximumChildren} child condition(s)";
                return Item(
                    ContractReferenceKind.LogicalOperator,
                    definition.Operator.ToString(),
                    Content(definition.Documentation, language),
                    childRange);
            }).ToList());

    private static GuideSection PredicateOperatorSection(string language) =>
        new(
            "operators",
            language == "vi" ? "Operators" : "Operators",
            language == "vi"
                ? "So sánh hoặc kiểm tra các operand có kiểu tương thích."
                : "Compare or inspect operands with compatible types.",
            RuleExpressionLanguage.Operators.Select(definition => Item(
                ContractReferenceKind.PredicateOperator,
                definition.Operator.ToString(),
                Content(definition.Documentation, language),
                OperatorSignature(definition, language))).ToList());

    private static GuideSection FunctionSection(string language) =>
        new(
            "functions",
            language == "vi" ? "Functions" : "Functions",
            language == "vi"
                ? "Tạo giá trị có kiểu từ các operand đầu vào."
                : "Produce typed values from input operands.",
            RuleExpressionLanguage.Functions.Select(definition => Item(
                ContractReferenceKind.Function,
                definition.Function.ToString(),
                Content(definition.Documentation, language),
                FunctionSignature(definition, language))).ToList());

    private static GuideSection ValueTypeSection(string language) =>
        new(
            "types",
            language == "vi" ? "Kiểu giá trị" : "Value types",
            language == "vi"
                ? "Các kiểu literal, context, parameter và kết quả function được hỗ trợ."
                : "Supported literal, context, parameter, and function-result types.",
            RuleExpressionLanguage.ValueTypes.Select(definition => Item(
                ContractReferenceKind.ValueType,
                definition.Type.ToString(),
                Content(definition.Documentation, language),
                $"{definition.Type}(\"…\")")).ToList());

    private static GuideSection OperandKindSection(string language) =>
        new(
            "operands",
            language == "vi" ? "Nguồn giá trị" : "Value sources",
            language == "vi"
                ? "Phân biệt giá trị đến từ context, parameter, literal hay function."
                : "Distinguish context, parameter, literal, and function values.",
            RuleExpressionLanguage.OperandKinds.Select(definition => Item(
                ContractReferenceKind.OperandKind,
                definition.Kind.ToString(),
                Content(definition.Documentation, language))).ToList());

    private static GuideSection LimitSection(string language) =>
        new(
            "limits",
            language == "vi" ? "Giới hạn biểu thức" : "Expression limits",
            language == "vi"
                ? "Giới hạn an toàn của expression language hiện tại."
                : "Safety limits of the current expression language.",
            RuleExpressionLanguage.Limits.Select(definition => Item(
                ContractReferenceKind.Limit,
                definition.Key,
                Content(definition.Documentation, language),
                definition.Value.ToString(CultureInfo.InvariantCulture))).ToList());

    private static RuleExpressionGuideSectionDto ToDto(
        GuideSection section,
        string query,
        IReadOnlyDictionary<string, int>? order)
    {
        IEnumerable<GuideItem> items = section.Items;
        if (order is not null)
        {
            items = items
                .Where(item => order.ContainsKey(item.SearchKey))
                .OrderBy(item => order[item.SearchKey]);
        }

        return new(
            section.Key,
            section.Title,
            section.Description,
            items.Select(item => new RuleExpressionGuideItemDto(
                item.ReferenceKind,
                item.ReferenceKey,
                Highlight(item.Content.DisplayName, query),
                Highlight(item.Content.Summary, query),
                Highlight(item.Content.Usage, query),
                item.Content.Examples.Select(example => Highlight(example, string.Empty)).ToArray(),
                string.IsNullOrWhiteSpace(item.Detail) ? null : Highlight(item.Detail, query)))
                .ToArray());
    }

    private static GuideItem Item(
        ContractReferenceKind kind,
        string key,
        RuleReferenceContent content,
        string? detail = null) =>
        new(kind, key, content, detail ?? string.Empty);

    private static RuleReferenceContent Content(
        RuleReferenceDocumentation documentation,
        string language) =>
        documentation.Locales.TryGetValue(language, out RuleReferenceContent? content)
            ? content
            : documentation.Locales["en"];

    private static string NormalizeLanguage(string? language) =>
        language?.Trim().StartsWith("vi", StringComparison.OrdinalIgnoreCase) == true
            ? "vi"
            : "en";

    private static string ValueTypeName(DomainValueType type, string language) =>
        Content(
            RuleExpressionLanguage.ValueTypes.Single(definition => definition.Type == type).Documentation,
            language).DisplayName;

    private static string CardinalityName(
        DomainCardinality cardinality,
        string language) =>
        Content(
            RuleExpressionLanguage.Cardinalities.Single(
                definition => definition.Cardinality == cardinality).Documentation,
            language).DisplayName;

    private static string OperatorSignature(
        RulePredicateOperatorDefinition definition,
        string language)
    {
        string left = ShapeNames(definition.LeftShapes, language);
        if (definition.RightShapes.Count == 0)
            return language == "vi" ? $"Một operand: {left}" : $"One operand: {left}";

        string right = ShapeNames(definition.RightShapes, language);
        return $"{left} → {definition.Operator} → {right}";
    }

    private static string FunctionSignature(
        RuleExpressionFunctionDefinition definition,
        string language)
    {
        string parameters = string.Join(
            ", ",
            definition.Parameters.Select(parameter =>
                $"{string.Join(" | ", parameter.AcceptedTypes.Select(type => ValueTypeName(type, language)))}" +
                $" · {CardinalityName(parameter.Cardinality, language)}"));
        return $"{definition.Function}({parameters}) → " +
               $"{ValueTypeName(definition.ReturnType, language)} · " +
               CardinalityName(definition.ReturnCardinality, language);
    }

    private static string ShapeNames(
        IReadOnlyList<RuleExpressionValueShape> shapes,
        string language) =>
        string.Join(
            ", ",
            shapes.Select(shape =>
                    $"{ValueTypeName(shape.Type, language)} · {CardinalityName(shape.Cardinality, language)}")
                .Distinct(StringComparer.Ordinal));

    private static IEnumerable<string> ContextReferences(RuleConditionNode node)
    {
        if (node is RuleConditionGroup group)
            return group.Children.SelectMany(ContextReferences).Distinct(StringComparer.Ordinal);

        RulePredicateCondition predicate = (RulePredicateCondition)node;
        return ContextReferences(predicate.Left)
            .Concat(predicate.Right is null ? [] : ContextReferences(predicate.Right))
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> ContextReferences(RuleOperand operand)
    {
        if (operand.Kind == DomainOperandKind.Context && operand.Reference is not null)
            yield return operand.Reference;
        foreach (RuleOperand argument in operand.Arguments)
        {
            foreach (string reference in ContextReferences(argument))
                yield return reference;
        }
    }

    private static SearchTextDto Highlight(string text, string query)
    {
        if (text.Length == 0)
            return new(text, []);
        if (string.IsNullOrWhiteSpace(query))
            return new(text, [new(text, IsMatch: false)]);

        NormalizedText normalized = NormalizeWithMap(text);
        List<(int Start, int End)> ranges = [];
        foreach (string term in Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int direct = normalized.Value.IndexOf(term, StringComparison.Ordinal);
            if (direct >= 0)
            {
                ranges.Add((
                    normalized.Map[direct],
                    normalized.Map[direct + term.Length - 1] + 1));
                continue;
            }

            (int Start, int End, int Distance)? best = null;
            foreach (Match match in SearchWordPattern().Matches(text).Cast<Match>())
            {
                int distance = EditDistance(Normalize(match.Value), term);
                int threshold = Math.Max(1, term.Length / 3);
                if (distance <= threshold && (best is null || distance < best.Value.Distance))
                    best = (match.Index, match.Index + match.Length, distance);
            }
            if (best is not null)
                ranges.Add((best.Value.Start, best.Value.End));
        }

        List<(int Start, int End)> merged = [];
        foreach ((int start, int end) in ranges.OrderBy(range => range.Start))
        {
            if (merged.Count > 0 && start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            else
                merged.Add((start, end));
        }

        if (merged.Count == 0)
            return new(text, [new(text, IsMatch: false)]);

        List<SearchTextSegmentDto> segments = [];
        int cursor = 0;
        foreach ((int start, int end) in merged)
        {
            if (start > cursor)
                segments.Add(new(text[cursor..start], IsMatch: false));
            segments.Add(new(text[start..end], IsMatch: true));
            cursor = end;
        }
        if (cursor < text.Length)
            segments.Add(new(text[cursor..], IsMatch: false));
        return new(text, segments);
    }

    private static NormalizedText NormalizeWithMap(string value)
    {
        StringBuilder text = new();
        List<int> map = [];
        for (int index = 0; index < value.Length; index++)
        {
            string normalized = Normalize(value[index].ToString());
            foreach (char character in normalized)
            {
                text.Append(character);
                map.Add(index);
            }
        }
        return new(text.ToString(), map);
    }

    private static string Normalize(string value)
    {
        string decomposed = value
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormD);
        StringBuilder normalized = new();
        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                normalized.Append(char.ToLowerInvariant(character));
        }
        return normalized.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int EditDistance(string left, string right)
    {
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (int leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            int diagonal = previous[0];
            previous[0] = leftIndex;
            for (int rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                int above = previous[rightIndex];
                previous[rightIndex] = Math.Min(
                    Math.Min(previous[rightIndex] + 1, previous[rightIndex - 1] + 1),
                    diagonal + (left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return previous[^1];
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SearchWordPattern();

    private sealed record GuideSection(
        string Key,
        string Title,
        string Description,
        List<GuideItem> Items);

    private sealed record GuideItem(
        ContractReferenceKind ReferenceKind,
        string ReferenceKey,
        RuleReferenceContent Content,
        string Detail)
    {
        public string SearchKey => $"{ReferenceKind}:{ReferenceKey}";
    }

    private sealed record NormalizedText(string Value, IReadOnlyList<int> Map);
}
