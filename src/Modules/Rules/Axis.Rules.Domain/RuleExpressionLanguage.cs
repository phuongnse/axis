using System.Collections.ObjectModel;

namespace Axis.Rules.Domain;

public sealed record RuleExpressionFunctionParameter(
    IReadOnlyList<RuleValueType> AcceptedTypes,
    RuleExpressionCardinality Cardinality);

public sealed record RuleExpressionFunctionDefinition(
    RuleExpressionFunction Function,
    IReadOnlyList<RuleExpressionFunctionParameter> Parameters,
    RuleValueType ReturnType,
    RuleExpressionCardinality ReturnCardinality,
    RuleReferenceDocumentation Documentation);

public sealed record RuleExpressionValueShape(
    RuleValueType Type,
    RuleExpressionCardinality Cardinality);

public sealed record RulePredicateOperatorDefinition(
    RulePredicateOperator Operator,
    IReadOnlyList<RuleExpressionValueShape> LeftShapes,
    IReadOnlyList<RuleExpressionValueShape> RightShapes,
    bool RequiresMatchingTypes,
    RuleReferenceDocumentation Documentation);

public sealed record RuleLogicalOperatorDefinition(
    RuleLogicalOperator Operator,
    int MinimumChildren,
    int? MaximumChildren,
    RuleReferenceDocumentation Documentation);

public sealed record RuleOperandKindDefinition(
    RuleOperandKind Kind,
    RuleReferenceDocumentation Documentation);

public sealed record RuleValueTypeDefinition(
    RuleValueType Type,
    RuleReferenceDocumentation Documentation);

public sealed record RuleExpressionCardinalityDefinition(
    RuleExpressionCardinality Cardinality,
    RuleReferenceDocumentation Documentation);

public sealed record RuleExpressionLimitDefinition(
    string Key,
    int Value,
    RuleReferenceDocumentation Documentation);

public static class RuleExpressionLanguage
{
    private static readonly IReadOnlyList<RuleValueType> AllTypes = Array.AsReadOnly(
        Enum.GetValues<RuleValueType>());

    public const int Version = 1;

    public static IReadOnlyList<RulePredicateOperatorDefinition> Operators { get; } =
        Array.AsReadOnly(
        [
            Binary(
                RulePredicateOperator.Equal,
                Shapes(AllTypes, RuleExpressionCardinality.Any),
                Doc(
                    "Equals", "Checks whether both values are the same.", "Compare values with compatible types.", "Field value Equals 100",
                    "Bằng", "Kiểm tra hai giá trị có giống nhau hay không.", "So sánh các giá trị có kiểu tương thích.", "Giá trị trường Bằng 100")),
            Binary(
                RulePredicateOperator.NotEqual,
                Shapes(AllTypes, RuleExpressionCardinality.Any),
                Doc(
                    "Does not equal", "Checks whether two values are different.", "Compare values with compatible types.", "Status Does not equal Closed",
                    "Không bằng", "Kiểm tra hai giá trị có khác nhau hay không.", "So sánh các giá trị có kiểu tương thích.", "Trạng thái Không bằng Đã đóng")),
            Binary(
                RulePredicateOperator.GreaterThan,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar),
                Doc(
                    "Greater than", "The first value is larger than or later than the second.", "Compare two numbers, dates, or times.", "Amount is greater than 100",
                    "Lớn hơn", "Giá trị đầu tiên lớn hơn hoặc muộn hơn giá trị thứ hai.", "So sánh hai số, ngày hoặc thời điểm.", "Số tiền lớn hơn 100")),
            Binary(
                RulePredicateOperator.GreaterThanOrEqual,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar),
                Doc(
                    "Greater than or equal", "The first value is the same as, larger than, or later than the second.", "Compare two numbers, dates, or times.", "Amount is greater than or equal to 100",
                    "Lớn hơn hoặc bằng", "Giá trị đầu tiên bằng, lớn hơn hoặc muộn hơn giá trị thứ hai.", "So sánh hai số, ngày hoặc thời điểm.", "Số tiền lớn hơn hoặc bằng 100")),
            Binary(
                RulePredicateOperator.LessThan,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar),
                Doc(
                    "Less than", "The first value is smaller than or earlier than the second.", "Compare two numbers, dates, or times.", "Date is earlier than 2026-12-31",
                    "Nhỏ hơn", "Giá trị đầu tiên nhỏ hơn hoặc sớm hơn giá trị thứ hai.", "So sánh hai số, ngày hoặc thời điểm.", "Ngày sớm hơn 2026-12-31")),
            Binary(
                RulePredicateOperator.LessThanOrEqual,
                Shapes(OrderedTypes(), RuleExpressionCardinality.Scalar),
                Doc(
                    "Less than or equal", "The first value is the same as, smaller than, or earlier than the second.", "Compare two numbers, dates, or times.", "Amount is less than or equal to 100",
                    "Nhỏ hơn hoặc bằng", "Giá trị đầu tiên bằng, nhỏ hơn hoặc sớm hơn giá trị thứ hai.", "So sánh hai số, ngày hoặc thời điểm.", "Số tiền nhỏ hơn hoặc bằng 100")),
            new RulePredicateOperatorDefinition(
                RulePredicateOperator.Contains,
                new ReadOnlyCollection<RuleExpressionValueShape>(
                [
                    new(RuleValueType.Text, RuleExpressionCardinality.Any),
                    .. Shapes(
                        AllTypes.Where(type => type != RuleValueType.Text).ToArray(),
                        RuleExpressionCardinality.Multiple),
                ]),
                Shapes(AllTypes, RuleExpressionCardinality.Scalar),
                RequiresMatchingTypes: true,
                Doc(
                    "Contains", "Checks whether text or a collection includes a value.", "Use text or a multi-value operand on the left and one compatible value on the right.", "Selected values Contains Approved",
                    "Chứa", "Kiểm tra văn bản hoặc tập giá trị có chứa một giá trị hay không.", "Dùng văn bản hoặc operand nhiều giá trị bên trái và một giá trị tương thích bên phải.", "Các giá trị đã chọn Chứa Đã duyệt")),
            Binary(
                RulePredicateOperator.StartsWith,
                Shapes([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                Doc(
                    "Starts with", "Checks the beginning of a text value.", "Use text on both sides.", "Code Starts with AX-",
                    "Bắt đầu bằng", "Kiểm tra phần đầu của một giá trị văn bản.", "Dùng văn bản ở cả hai bên.", "Mã Bắt đầu bằng AX-")),
            Binary(
                RulePredicateOperator.EndsWith,
                Shapes([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                Doc(
                    "Ends with", "Checks the ending of a text value.", "Use text on both sides.", "Email Ends with @example.com",
                    "Kết thúc bằng", "Kiểm tra phần cuối của một giá trị văn bản.", "Dùng văn bản ở cả hai bên.", "Email Kết thúc bằng @example.com")),
            Unary(
                RulePredicateOperator.IsNull,
                Doc(
                    "Is empty", "Checks whether a value is absent.", "Use one value; no right operand is needed.", "Closed date Is empty",
                    "Đang trống", "Kiểm tra một giá trị có tồn tại hay không.", "Dùng một giá trị; không cần operand bên phải.", "Ngày đóng Đang trống")),
            Unary(
                RulePredicateOperator.IsNotNull,
                Doc(
                    "Is not empty", "Checks whether a value is present.", "Use one value; no right operand is needed.", "Owner Is not empty",
                    "Không trống", "Kiểm tra một giá trị có tồn tại hay không.", "Dùng một giá trị; không cần operand bên phải.", "Người phụ trách Không trống")),
        ]);

    public static IReadOnlyList<RuleExpressionFunctionDefinition> Functions { get; } =
        Array.AsReadOnly(
        [
            Function(
                RuleExpressionFunction.IsBlank,
                [Parameter(AllTypes, RuleExpressionCardinality.Any)],
                RuleValueType.Boolean,
                Doc(
                    "Is blank", "Returns true when a value is absent or empty.", "Pass one scalar or multi-value operand.", "Is blank(Field value)",
                    "Đang trống", "Trả về đúng khi giá trị không tồn tại hoặc rỗng.", "Truyền một operand đơn hoặc nhiều giá trị.", "Đang trống(Giá trị trường)")),
            Function(
                RuleExpressionFunction.Length,
                [Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar)],
                RuleValueType.Integer,
                Doc(
                    "Length", "Returns the number of characters in text.", "Pass one text value, then compare the returned integer.", "Length(Field value) Greater than 50",
                    "Độ dài", "Trả về số ký tự trong văn bản.", "Truyền một giá trị văn bản rồi so sánh số nguyên trả về.", "Độ dài(Giá trị trường) Lớn hơn 50")),
            Function(
                RuleExpressionFunction.Precision,
                [Parameter([RuleValueType.Decimal], RuleExpressionCardinality.Scalar)],
                RuleValueType.Integer,
                Doc(
                    "Precision", "Returns the total number of digits in a decimal value.", "Pass one decimal value, then compare the returned integer.", "Precision(Amount) Less than or equal 12",
                    "Độ chính xác", "Trả về tổng số chữ số của một giá trị thập phân.", "Truyền một giá trị thập phân rồi so sánh số nguyên trả về.", "Độ chính xác(Số tiền) Nhỏ hơn hoặc bằng 12")),
            Function(
                RuleExpressionFunction.Scale,
                [Parameter([RuleValueType.Decimal], RuleExpressionCardinality.Scalar)],
                RuleValueType.Integer,
                Doc(
                    "Scale", "Returns the number of digits after the decimal point.", "Pass one decimal value, then compare the returned integer.", "Scale(Amount) Less than or equal 2",
                    "Số chữ số thập phân", "Trả về số chữ số sau dấu thập phân.", "Truyền một giá trị thập phân rồi so sánh số nguyên trả về.", "Số chữ số thập phân(Số tiền) Nhỏ hơn hoặc bằng 2")),
            Function(
                RuleExpressionFunction.Count,
                [Parameter(AllTypes, RuleExpressionCardinality.Multiple)],
                RuleValueType.Integer,
                Doc(
                    "Count", "Returns how many values are in a collection.", "Pass one multi-value operand, then compare the returned integer.", "Count(Selected values) Greater than 3",
                    "Số lượng", "Trả về số giá trị trong một tập hợp.", "Truyền một operand nhiều giá trị rồi so sánh số nguyên trả về.", "Số lượng(Các giá trị đã chọn) Lớn hơn 3")),
            Function(
                RuleExpressionFunction.MatchesPattern,
                [
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                ],
                RuleValueType.Boolean,
                Doc(
                    "Matches pattern", "Returns true when text matches a regular-expression pattern.", "Pass the text first and the pattern second.", "Matches pattern(Field value, ^AX-[0-9]+$)",
                    "Khớp mẫu", "Trả về đúng khi văn bản khớp mẫu biểu thức chính quy.", "Truyền văn bản trước và mẫu sau.", "Khớp mẫu(Giá trị trường, ^AX-[0-9]+$)")),
            Function(
                RuleExpressionFunction.HasFormat,
                [
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                    Parameter([RuleValueType.Text], RuleExpressionCardinality.Scalar),
                ],
                RuleValueType.Boolean,
                Doc(
                    "Has format", "Returns true when text has a supported named format.", "Pass the text first and the format name second.", "Has format(Field value, Email)",
                    "Đúng định dạng", "Trả về đúng khi văn bản có định dạng được hỗ trợ.", "Truyền văn bản trước và tên định dạng sau.", "Đúng định dạng(Giá trị trường, Email)")),
            Function(
                RuleExpressionFunction.ToDecimal,
                [
                    Parameter(
                        [RuleValueType.Integer, RuleValueType.Decimal],
                        RuleExpressionCardinality.Scalar),
                ],
                RuleValueType.Decimal,
                Doc(
                    "To decimal", "Converts an integer or decimal operand to a decimal result.", "Pass one numeric value before comparing it with decimal values.", "To decimal(Field value) Greater than 10.5",
                    "Đổi sang thập phân", "Chuyển một operand số nguyên hoặc thập phân thành kết quả thập phân.", "Truyền một giá trị số trước khi so sánh với giá trị thập phân.", "Đổi sang thập phân(Giá trị trường) Lớn hơn 10.5")),
        ]);

    public static IReadOnlyList<RuleLogicalOperatorDefinition> LogicalOperators { get; } =
        Array.AsReadOnly<RuleLogicalOperatorDefinition>(
        [
            new(
                RuleLogicalOperator.All,
                MinimumChildren: 1,
                MaximumChildren: null,
                Doc(
                    "All", "Matches only when every child condition matches.", "Group conditions that must all be true.", "All: Amount Greater than 0; Status Equals Active",
                    "Tất cả", "Chỉ khớp khi mọi điều kiện con đều khớp.", "Nhóm các điều kiện bắt buộc cùng đúng.", "Tất cả: Số tiền Lớn hơn 0; Trạng thái Bằng Đang hoạt động")),
            new(
                RuleLogicalOperator.Any,
                MinimumChildren: 1,
                MaximumChildren: null,
                Doc(
                    "Any", "Matches when at least one child condition matches.", "Group alternative conditions where one match is enough.", "Any: Status Equals Draft; Status Equals Pending",
                    "Bất kỳ", "Khớp khi ít nhất một điều kiện con khớp.", "Nhóm các điều kiện thay thế, chỉ cần một điều kiện đúng.", "Bất kỳ: Trạng thái Bằng Nháp; Trạng thái Bằng Chờ xử lý")),
            new(
                RuleLogicalOperator.Not,
                MinimumChildren: 1,
                MaximumChildren: 1,
                Doc(
                    "Not", "Reverses the result of one child condition.", "Wrap exactly one condition whose result should be inverted.", "Not: Status Equals Closed",
                    "Không", "Đảo ngược kết quả của một điều kiện con.", "Bao đúng một điều kiện cần đảo kết quả.", "Không: Trạng thái Bằng Đã đóng")),
        ]);

    public static IReadOnlyList<RuleOperandKindDefinition> OperandKinds { get; } =
        Array.AsReadOnly<RuleOperandKindDefinition>(
        [
            new(
                RuleOperandKind.Input,
                Doc(
                    "Rule input", "A typed value supplied by the consumer that runs the rule.", "Give the input a clear label, then choose it in a condition.", "Threshold",
                    "Input của rule", "Giá trị có kiểu do consumer chạy rule cung cấp.", "Đặt tên rõ ràng cho input rồi chọn nó trong điều kiện.", "Ngưỡng")),
            new(
                RuleOperandKind.Literal,
                Doc(
                    "Literal value", "A fixed typed value stored directly in the expression.", "Enter a value compatible with the other operand and operator.", "100",
                    "Giá trị cố định", "Giá trị có kiểu được lưu trực tiếp trong biểu thức.", "Nhập giá trị tương thích với operand còn lại và operator.", "100")),
            new(
                RuleOperandKind.Function,
                Doc(
                    "Calculated value", "A typed value calculated by a registered pure function.", "Choose a calculation and provide compatible inputs or fixed values.", "Length of Customer name",
                    "Giá trị tính toán", "Giá trị có kiểu được tính bởi một pure function đã đăng ký.", "Chọn phép tính và cung cấp input hoặc giá trị cố định tương thích.", "Độ dài của Tên khách hàng")),
        ]);

    public static IReadOnlyList<RuleValueTypeDefinition> ValueTypes { get; } =
        Array.AsReadOnly<RuleValueTypeDefinition>(
        [
            new(RuleValueType.Text, Doc(
                "Text", "A sequence of characters.", "Use for names, codes, descriptions, and other textual values.", "AX-100",
                "Văn bản", "Một chuỗi ký tự.", "Dùng cho tên, mã, mô tả và các giá trị dạng chữ.", "AX-100")),
            new(RuleValueType.Integer, Doc(
                "Integer", "A whole number without a decimal part.", "Use for counts and other whole-number values.", "100",
                "Số nguyên", "Số không có phần thập phân.", "Dùng cho số lượng và các giá trị số nguyên khác.", "100")),
            new(RuleValueType.Decimal, Doc(
                "Decimal", "A number that may include a decimal part.", "Use for amounts, measurements, and precise numeric values.", "10.50",
                "Số thập phân", "Số có thể có phần thập phân.", "Dùng cho số tiền, phép đo và giá trị số cần độ chính xác.", "10.50")),
            new(RuleValueType.Date, Doc(
                "Date", "A calendar date without a time of day.", "Use ISO date values in YYYY-MM-DD form.", "2026-12-31",
                "Ngày", "Ngày theo lịch không kèm thời gian trong ngày.", "Dùng giá trị ngày ISO theo dạng YYYY-MM-DD.", "2026-12-31")),
            new(RuleValueType.DateTime, Doc(
                "Date and time", "A date and time value representing an instant.", "Use an ISO 8601 date-time value.", "2026-12-31T08:30:00Z",
                "Ngày và giờ", "Giá trị ngày giờ đại diện cho một thời điểm.", "Dùng giá trị ngày giờ theo ISO 8601.", "2026-12-31T08:30:00Z")),
            new(RuleValueType.Boolean, Doc(
                "Boolean", "A true or false value.", "Use for yes/no states and boolean function results.", "true",
                "Đúng/sai", "Giá trị đúng hoặc sai.", "Dùng cho trạng thái có/không và kết quả boolean của function.", "true")),
        ]);

    public static IReadOnlyList<RuleExpressionCardinalityDefinition> Cardinalities { get; } =
        Array.AsReadOnly<RuleExpressionCardinalityDefinition>(
        [
            new(RuleExpressionCardinality.Scalar, Doc(
                "Single value", "Exactly one value.", "Use where an operator or function expects one value.", "Amount",
                "Một giá trị", "Đúng một giá trị.", "Dùng khi operator hoặc function yêu cầu một giá trị.", "Số tiền")),
            new(RuleExpressionCardinality.Multiple, Doc(
                "Multiple values", "A collection containing zero or more values.", "Use where an operator or function accepts a collection.", "Selected values",
                "Nhiều giá trị", "Tập hợp chứa không hoặc nhiều giá trị.", "Dùng khi operator hoặc function chấp nhận một tập hợp.", "Các giá trị đã chọn")),
            new(RuleExpressionCardinality.Any, Doc(
                "Single or multiple", "Either one value or a collection.", "Use where both scalar and multi-value operands are accepted.", "Field value",
                "Một hoặc nhiều", "Một giá trị hoặc một tập hợp.", "Dùng khi chấp nhận cả operand đơn và nhiều giá trị.", "Giá trị trường")),
        ]);

    public static IReadOnlyList<RuleExpressionLimitDefinition> Limits { get; } =
        Array.AsReadOnly<RuleExpressionLimitDefinition>(
        [
            new("maxDepth", RuleEvaluationLimits.Default.MaxDepth, Doc(
                "Maximum nesting depth", "The deepest supported expression nesting.", "Keep nested groups and functions within this value.", "12",
                "Độ sâu lồng tối đa", "Mức lồng biểu thức sâu nhất được hỗ trợ.", "Giữ group và function lồng nhau trong giới hạn này.", "12")),
            new("maxNodes", RuleEvaluationLimits.Default.MaxNodes, Doc(
                "Maximum condition nodes", "The largest number of condition nodes in one expression.", "Keep the complete condition tree within this value.", "200",
                "Số node điều kiện tối đa", "Số node điều kiện lớn nhất trong một biểu thức.", "Giữ toàn bộ cây điều kiện trong giới hạn này.", "200")),
            new("maxFunctionCalls", RuleEvaluationLimits.Default.MaxFunctionCalls, Doc(
                "Maximum function calls", "The largest number of function calls in one expression.", "Keep all nested and direct function calls within this value.", "50",
                "Số lần gọi function tối đa", "Số lần gọi function lớn nhất trong một biểu thức.", "Giữ mọi lần gọi function trong giới hạn này.", "50")),
            new("maxInputs", RuleEvaluationLimits.Default.MaxInputs, Doc(
                "Maximum inputs", "The largest number of inputs available to one rule.", "Keep the rule input contract within this value.", "100",
                "Số input tối đa", "Số input lớn nhất của một rule.", "Giữ contract input của rule trong giới hạn này.", "100")),
            new("maxExecutionSteps", RuleEvaluationLimits.Default.MaxExecutionSteps, Doc(
                "Maximum evaluation steps", "The evaluation work allowed for one expression.", "Simplify the expression if it exceeds this safety limit.", "1000",
                "Số bước đánh giá tối đa", "Lượng xử lý cho phép khi đánh giá một biểu thức.", "Đơn giản hóa biểu thức nếu vượt giới hạn an toàn này.", "1000")),
        ]);

    public static RuleExpressionFunctionDefinition? Find(RuleExpressionFunction function) =>
        Functions.SingleOrDefault(definition => definition.Function == function);

    public static RulePredicateOperatorDefinition? Find(RulePredicateOperator @operator) =>
        Operators.SingleOrDefault(definition => definition.Operator == @operator);

    private static RuleExpressionFunctionParameter Parameter(
        IReadOnlyList<RuleValueType> acceptedTypes,
        RuleExpressionCardinality cardinality) =>
        new(
            new ReadOnlyCollection<RuleValueType>(acceptedTypes.ToArray()),
            cardinality);

    private static RuleExpressionFunctionDefinition Function(
        RuleExpressionFunction function,
        IReadOnlyList<RuleExpressionFunctionParameter> parameters,
        RuleValueType returnType,
        RuleReferenceDocumentation documentation) =>
        new(
            function,
            new ReadOnlyCollection<RuleExpressionFunctionParameter>(parameters.ToArray()),
            returnType,
            RuleExpressionCardinality.Scalar,
            documentation);

    private static RulePredicateOperatorDefinition Binary(
        RulePredicateOperator @operator,
        IReadOnlyList<RuleExpressionValueShape> shapes,
        RuleReferenceDocumentation documentation) =>
        new(@operator, shapes, shapes, RequiresMatchingTypes: true, documentation);

    private static RulePredicateOperatorDefinition Unary(
        RulePredicateOperator @operator,
        RuleReferenceDocumentation documentation) =>
        new(
            @operator,
            Shapes(AllTypes, RuleExpressionCardinality.Any),
            [],
            RequiresMatchingTypes: false,
            documentation);

    private static RuleReferenceDocumentation Doc(
        string englishName,
        string englishSummary,
        string englishUsage,
        string englishExample,
        string vietnameseName,
        string vietnameseSummary,
        string vietnameseUsage,
        string vietnameseExample) =>
        RuleReferenceDocumentation.Bilingual(
            englishName,
            englishSummary,
            englishUsage,
            englishExample,
            vietnameseName,
            vietnameseSummary,
            vietnameseUsage,
            vietnameseExample);

    private static IReadOnlyList<RuleExpressionValueShape> Shapes(
        IReadOnlyList<RuleValueType> types,
        RuleExpressionCardinality cardinality) =>
        new ReadOnlyCollection<RuleExpressionValueShape>(
            types.Select(type => new RuleExpressionValueShape(type, cardinality)).ToArray());

    private static IReadOnlyList<RuleValueType> OrderedTypes() =>
        Array.AsReadOnly(
        [
            RuleValueType.Text,
            RuleValueType.Integer,
            RuleValueType.Decimal,
            RuleValueType.Date,
            RuleValueType.DateTime,
        ]);
}
