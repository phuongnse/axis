using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public static class SystemRuleCatalog
{
    public static IReadOnlyList<RuleDefinition> Definitions { get; } = Array.AsReadOnly<RuleDefinition>(
    [
        Definition(
            "field.required",
            "Required value",
            "Requires a value for the field.",
            [Input("value", [RuleValueType.Text, RuleValueType.Integer, RuleValueType.Decimal, RuleValueType.Date, RuleValueType.DateTime, RuleValueType.Boolean], false, allowMultiple: true)],
            Predicate(
                "required",
                RulePredicateOperator.Equal,
                Function(RuleExpressionFunction.IsBlank, Value()),
                Boolean(false))),
        Definition(
            "field.numeric_range",
            "Numeric range",
            "Constrains numeric values to optional minimum and maximum bounds.",
            [
                Input("value", [RuleValueType.Integer, RuleValueType.Decimal], true),
                Input("min", RuleValueType.Decimal, false),
                Input("max", RuleValueType.Decimal, false),
            ],
            Range("numeric-range", Function(RuleExpressionFunction.ToDecimal, Value()))),
        Definition(
            "field.decimal_precision",
            "Decimal precision",
            "Constrains decimal precision and scale.",
            [
                Input("value", RuleValueType.Decimal, true),
                Input("precision", RuleValueType.Integer, true),
                Input("scale", RuleValueType.Integer, true),
            ],
            All(
                "decimal-precision",
                Predicate(
                    "decimal-precision-digits",
                    RulePredicateOperator.LessThanOrEqual,
                    Function(RuleExpressionFunction.Precision, Value()),
                    InputOperand("precision")),
                Predicate(
                    "decimal-precision-scale",
                    RulePredicateOperator.LessThanOrEqual,
                    Function(RuleExpressionFunction.Scale, Value()),
                    InputOperand("scale")))),
        Definition(
            "field.date_range",
            "Date range",
            "Constrains calendar dates to optional earliest and latest dates.",
            [Input("value", RuleValueType.Date, true), Input("min", RuleValueType.Date, false), Input("max", RuleValueType.Date, false)],
            Range("date-range", Value())),
        Definition(
            "field.datetime_range",
            "Date and time range",
            "Constrains date-and-time instants to optional earliest and latest values.",
            [Input("value", RuleValueType.DateTime, true), Input("min", RuleValueType.DateTime, false), Input("max", RuleValueType.DateTime, false)],
            Range("datetime-range", Value())),
        Definition(
            "field.text_length",
            "Text length",
            "Constrains text values to optional minimum and maximum lengths.",
            [Input("value", RuleValueType.Text, true), Input("min", RuleValueType.Integer, false), Input("max", RuleValueType.Integer, false)],
            Range("text-length", Function(RuleExpressionFunction.Length, Value()))),
        Definition(
            "field.text_pattern",
            "Text pattern",
            "Requires text values to match a system-supported regular expression pattern.",
            [Input("value", RuleValueType.Text, true), Input("pattern", RuleValueType.Text, true)],
            Predicate(
                "text-pattern",
                RulePredicateOperator.Equal,
                Function(RuleExpressionFunction.MatchesPattern, Value(), InputOperand("pattern")),
                Boolean(true))),
        Definition(
            "field.text_format",
            "Text format",
            "Requires text values to use a supported enterprise format.",
            [Input("value", RuleValueType.Text, true), Input("format", RuleValueType.Text, true, allowedValues: ["Email", "Url", "Uuid"])],
            Predicate(
                "text-format",
                RulePredicateOperator.Equal,
                Function(RuleExpressionFunction.HasFormat, Value(), InputOperand("format")),
                Boolean(true))),
        Definition(
            "field.choice_selection_count",
            "Choice selection count",
            "Constrains the number of selected values for a multiple-choice field.",
            [Input("value", RuleValueType.Text, true, allowMultiple: true), Input("min", RuleValueType.Integer, false), Input("max", RuleValueType.Integer, false)],
            Range("choice-selection-count", Function(RuleExpressionFunction.Count, Value())))
    ]);

    public static RuleDefinition? Find(string key, int version) =>
        Definitions.SingleOrDefault(definition =>
            definition.Key.Value.Equals(key?.Trim(), StringComparison.Ordinal) &&
            definition.LatestPublishedVersion == version);

    private static RuleDefinition Definition(
        string key,
        string displayName,
        string description,
        IReadOnlyList<RuleInputDefinition> inputs,
        RuleConditionNode condition)
    {
        Result<RuleDefinitionKey> definitionKey = RuleDefinitionKey.Create(key);
        if (definitionKey.IsFailure)
            throw new InvalidOperationException(definitionKey.Error);

        Result<RuleDefinition> definition = RuleDefinition.CreateSystem(
            definitionKey.Value,
            version: 1,
            displayName,
            description,
            Documentation(key, displayName, description),
            inputs,
            condition,
            RuleOutputContract.BooleanMatch);
        return definition.IsSuccess
            ? definition.Value
            : throw new InvalidOperationException(definition.Error);
    }

    private static RuleReferenceDocumentation Documentation(
        string key,
        string englishName,
        string englishSummary)
    {
        (
            string vietnameseName,
            string vietnameseSummary,
            string englishUsage,
            string vietnameseUsage,
            string englishExample,
            string vietnameseExample) =
            key switch
            {
                "field.required" => ("Giá trị bắt buộc", "Bắt buộc bản ghi cung cấp giá trị cho trường.", "No setup needed.", "Không cần cấu hình.", "A customer name cannot be left blank.", "Tên khách hàng không được để trống."),
                "field.numeric_range" => ("Khoảng số", "Giới hạn số nguyên hoặc số thập phân bằng khoảng tùy chọn.", "Configure optional minimum and maximum values.", "Cấu hình giá trị nhỏ nhất và lớn nhất tùy chọn.", "A discount must stay between 0 and 100.", "Mức giảm giá phải nằm trong khoảng từ 0 đến 100."),
                "field.decimal_precision" => ("Độ chính xác thập phân", "Giới hạn precision và scale của số thập phân.", "Configure precision and scale.", "Cấu hình precision và scale.", "A price allows up to 12 digits with 2 decimal places.", "Giá cho phép tối đa 12 chữ số với 2 chữ số thập phân."),
                "field.date_range" => ("Khoảng ngày", "Giới hạn ngày bằng giá trị sớm nhất và muộn nhất tùy chọn.", "Configure optional earliest and latest dates.", "Cấu hình ngày sớm nhất và muộn nhất tùy chọn.", "A booking date must fall within the current year.", "Ngày đặt chỗ phải nằm trong năm hiện tại."),
                "field.datetime_range" => ("Khoảng ngày giờ", "Giới hạn thời điểm ngày giờ bằng offset rõ ràng.", "Configure optional earliest and latest instants.", "Cấu hình thời điểm sớm nhất và muộn nhất tùy chọn.", "A submission must occur before the closing instant.", "Bài gửi phải được thực hiện trước thời điểm đóng."),
                "field.text_length" => ("Độ dài văn bản", "Giới hạn văn bản bằng độ dài tối thiểu và tối đa tùy chọn.", "Configure optional minimum and maximum length.", "Cấu hình độ dài tối thiểu và tối đa tùy chọn.", "A summary must contain between 20 and 200 characters.", "Phần tóm tắt phải có từ 20 đến 200 ký tự."),
                "field.text_pattern" => ("Mẫu văn bản", "Bắt buộc văn bản khớp pattern đã cấu hình.", "Configure the required regular-expression pattern.", "Cấu hình pattern biểu thức chính quy bắt buộc.", "An order code must match the configured code pattern.", "Mã đơn hàng phải khớp mẫu mã đã cấu hình."),
                "field.text_format" => ("Định dạng văn bản", "Bắt buộc văn bản dùng định dạng email, URL hoặc UUID được hỗ trợ.", "Choose a supported text format.", "Chọn một định dạng văn bản được hỗ trợ.", "A contact address must use a valid email format.", "Địa chỉ liên hệ phải dùng định dạng email hợp lệ."),
                "field.choice_selection_count" => ("Số lượng lựa chọn", "Giới hạn số lựa chọn của trường chọn nhiều.", "Configure optional minimum and maximum selections.", "Cấu hình số lựa chọn tối thiểu và tối đa tùy chọn.", "A preference field requires one to three selections.", "Trường sở thích yêu cầu từ một đến ba lựa chọn."),
                _ => throw new InvalidOperationException($"System rule documentation is missing for '{key}'."),
            };

        return RuleReferenceDocumentation.Bilingual(
            englishName,
            englishSummary,
            englishUsage,
            englishExample,
            vietnameseName,
            vietnameseSummary,
            vietnameseUsage,
            vietnameseExample);
    }

    private static RuleInputDefinition Input(
        string key,
        IReadOnlyList<RuleValueType> types,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null) =>
        RuleInputDefinition.CreateSystem(
            key,
            InputLabel(key),
            types,
            isRequired,
            allowMultiple,
            allowedValues).Value;

    private static RuleInputDefinition Input(
        string key,
        RuleValueType type,
        bool isRequired,
        bool allowMultiple = false,
        IReadOnlyList<string>? allowedValues = null) =>
        Input(key, [type], isRequired, allowMultiple, allowedValues);

    private static string InputLabel(string key) => key switch
    {
        "value" => "Value",
        "min" => "Minimum",
        "max" => "Maximum",
        "precision" => "Total digits",
        "scale" => "Decimal places",
        "pattern" => "Required pattern",
        "format" => "Required format",
        _ => throw new InvalidOperationException($"System rule input label is missing for '{key}'."),
    };

    private static RuleConditionGroup Range(string prefix, RuleOperand value) =>
        All(
            prefix,
            Any(
                $"{prefix}-minimum",
                Predicate($"{prefix}-minimum-absent", RulePredicateOperator.IsNull, InputOperand("min")),
                Predicate($"{prefix}-minimum-satisfied", RulePredicateOperator.GreaterThanOrEqual, value, InputOperand("min"))),
            Any(
                $"{prefix}-maximum",
                Predicate($"{prefix}-maximum-absent", RulePredicateOperator.IsNull, InputOperand("max")),
                Predicate($"{prefix}-maximum-satisfied", RulePredicateOperator.LessThanOrEqual, value, InputOperand("max"))));

    private static RuleConditionGroup All(string nodeId, params RuleConditionNode[] children) =>
        RuleConditionGroup.Create(nodeId, RuleLogicalOperator.All, children).Value;

    private static RuleConditionGroup Any(string nodeId, params RuleConditionNode[] children) =>
        RuleConditionGroup.Create(nodeId, RuleLogicalOperator.Any, children).Value;

    private static RulePredicateCondition Predicate(
        string nodeId,
        RulePredicateOperator @operator,
        RuleOperand left,
        RuleOperand? right = null) =>
        RulePredicateCondition.Create(nodeId, @operator, left, right).Value;

    private static RuleOperand Value() => RuleOperand.Input("value").Value;

    private static RuleOperand InputOperand(string key) => RuleOperand.Input(key).Value;

    private static RuleOperand Function(
        RuleExpressionFunction function,
        params RuleOperand[] arguments) =>
        RuleOperand.Function(function, arguments).Value;

    private static RuleOperand Boolean(bool value) =>
        RuleOperand.LiteralValue(
            RuleValue.Create(RuleValueType.Boolean, [value.ToString()]).Value).Value;
}
