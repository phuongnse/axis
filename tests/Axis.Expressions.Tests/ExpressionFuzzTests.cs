using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Expressions.Functions;
using Axis.Expressions.Parsing;
using Axis.Expressions.Sql;
using Axis.Expressions.Syntax;
using Axis.Expressions.Typing;
using Xunit.Sdk;

namespace Axis.Expressions.Tests;

/// <summary>
/// Fuzz tests of the parser, the type checker and the SQL translator, as docs/reference/fuzz-tests.md
/// describes. The inputs are the expressions of the samples with random edits, and random runs of
/// tokens. None of the three stages may throw or hang, and the SQL may hold no text of the input
/// outside a parameter.
/// </summary>
public sealed partial class ExpressionFuzzTests
{
    private const int MaxLength = 3_000;

    private static readonly IReadOnlyList<string> _corpus = ReadCorpus();

    private static readonly Dictionary<string, ExpressionType> _fields = FuzzFields();

    private static readonly ExpressionScope _scope = FuzzScope();

    private static readonly string[] _dictionary = BuildDictionary();

    private static readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    // The quoted names the translator tests' callbacks write, and the words and punctuation of the translator.
    private static readonly HashSet<string> _callbackIdentifiers = ["\"col\"", "\"items\"", "\"owner\"", "\"r\"", "\"id\"", "\"item_col\""];

    private static readonly HashSet<string> _numbers = ["0", "1"];

    private static readonly HashSet<string> _punctuation = ["(", ")", ",", ".", "+", "-", "*", "<", ">", "=", ":"];

    private static readonly HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR", "NOT", "IS", "DISTINCT", "FROM", "NULL", "IN", "CASE", "WHEN", "THEN", "ELSE", "END", "COALESCE", "concat",
        "char_length", "strpos", "starts_with", "right", "abs", "floor", "ceil", "round", "numeric", "int", "bigint", "extract",
        "year", "month", "day", "timestamptz", "date", "AT", "TIME", "ZONE", "EXISTS", "SELECT", "count", "WHERE",
    };

    [Fact]
    public void The_corpus_holds_the_expressions_of_the_samples()
    {
        Assert.True(_corpus.Count >= 25, $"The corpus holds {_corpus.Count} expressions.");
        Assert.Contains("employee == currentUser()", _corpus);
        Assert.Contains("sum(lineItems, amount)", _corpus);
        Assert.Contains("status == 'resolved'", _corpus);
    }

    [Fact]
    public void The_samples_expressions_parse_check_and_translate_in_the_fuzz_scope_so_the_mutations_start_from_valid_input()
    {
        var translated = 0;
        foreach (var text in _corpus)
        {
            var parsed = ExpressionParser.Parse(text);
            Assert.True(parsed.Succeeded, $"{text}: {parsed.Diagnostic?.Message}");
            var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean, policyFilter: true);
            if (check.Succeeded && SqlTranslator.Translate(parsed.Expression, Column, check, "f", _timeZone, Items).Succeeded)
            {
                translated++;
            }
        }

        Assert.True(translated >= 10, $"Only {translated} of {_corpus.Count} expressions translate.");
    }

    [Fact]
    public void Parser_returns_a_tree_or_a_diagnostic_for_any_text() =>
        Fuzz.Run(
            random => Mutate(random),
            text =>
            {
                var result = ExpressionParser.Parse(text);

                Assert.True(result.Expression is null != result.Diagnostic is null);
            });

    [Fact]
    public void Checker_returns_a_type_or_a_diagnostic_for_any_parsed_expression()
    {
        var number = 0;
        Fuzz.Run(
            random => (Text: Mutate(random), StoredValue: ++number % 2 == 0),
            input =>
            {
                var parsed = ExpressionParser.Parse(input.Text);
                if (!parsed.Succeeded)
                {
                    return;
                }

                var result = ExpressionTypeChecker.Check(
                    parsed.Expression, _scope, ExpressionType.Boolean, storedValue: input.StoredValue, policyFilter: true);

                Assert.True(result.Type is null != result.Diagnostic is null);
            },
            input => Fuzz.Literal(input.Text));
    }

    [Fact]
    public void Translator_keeps_every_input_value_in_a_parameter() =>
        Fuzz.Run(
            random => Mutate(random),
            text =>
            {
                var parsed = ExpressionParser.Parse(text);
                if (!parsed.Succeeded)
                {
                    return;
                }

                TranslateAndCheckSql(parsed.Expression, null);
                var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean, policyFilter: true);
                if (check.Succeeded)
                {
                    TranslateAndCheckSql(parsed.Expression, check);
                }
            });

    [Fact]
    public void The_sql_check_rejects_a_quoted_string_a_number_or_a_word_outside_a_parameter()
    {
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = 'x')", []));
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = 42)", []));
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = DROP)", []));
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = 'it''s' OR 1 = 1)", []));
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = \"other\")", []));
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = 0.5)", []));
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = @f7)", []));
        Assert.ThrowsAny<XunitException>(() => SqlHoldsOnlyParameters("(\"col\" = x; DROP TABLE t)", []));
    }

    [Fact]
    public void The_sql_check_accepts_the_translators_own_sql()
    {
        var parsed = ExpressionParser.Parse("t == 'x' and (i > 1 or today() == dt) and ts < now() and any(lineItems, amount > 0)");
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var check = ExpressionTypeChecker.Check(parsed.Expression, _scope, ExpressionType.Boolean);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);

        var result = SqlTranslator.Translate(parsed.Expression, Column, check, "f", _timeZone, Items);

        Assert.True(result.Succeeded, result.Diagnostic?.Message);
        SqlHoldsOnlyParameters(result.Sql, result.Parameters);
        SqlHoldsOnlyParameters("(\"col\" = @f7)", [new SqlValue("f7", ExpressionTypeKind.Integer, 7L)]);
        SqlHoldsOnlyParameters("(\"col\" = @currentUser)", []);
    }

    private static void TranslateAndCheckSql(ExpressionNode expression, ExpressionCheckResult? check)
    {
        var result = SqlTranslator.Translate(expression, Column, check, "f", _timeZone, Items);

        Assert.True(result.Sql is null != result.Diagnostic is null);
        if (result.Sql is not null)
        {
            SqlHoldsOnlyParameters(result.Sql, result.Parameters);
        }
    }

    /// <summary>
    /// Fails unless every token of <paramref name="sql"/> is a parameter, a quoted name the column callbacks
    /// give, the number 0 or 1, or one of the translator's own words and punctuation marks.
    /// </summary>
    private static void SqlHoldsOnlyParameters(string sql, IReadOnlyList<SqlValue> parameters)
    {
        var names = parameters.Select(parameter => "@" + parameter.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var token in SqlTokens().Matches(sql.Replace(SqlTranslator.NowSql, "", StringComparison.Ordinal)).Select(match => match.Value))
        {
            var allowed = token[0] switch
            {
                '@' => names.Contains(token) || token == "@" + SqlTranslator.CurrentUserParameter,
                '"' => _callbackIdentifiers.Contains(token),
                >= '0' and <= '9' => _numbers.Contains(token),
                var c when char.IsLetter(c) || c == '_' => _words.Contains(token),
                _ => _punctuation.Contains(token),
            };
            if (!allowed)
            {
                Assert.Fail($"The SQL holds {token} outside a parameter: {sql}");
            }
        }
    }

    [GeneratedRegex("""@\w*|"[^"]*"|'(?:[^']|'')*'|\d+(?:\.\d+)?|[\p{L}_]\w*|\S""")]
    private static partial Regex SqlTokens();

    private static string Column(IReadOnlyList<string> path) => "\"col\"";

    private static SqlCollection Items(string name) =>
        new("\"items\"", "(\"items\".\"owner\" = \"r\".\"id\")", _ => "\"item_col\"");

    /// <summary>Picks an expression of the samples and edits it 1 to 4 times, or builds a run of tokens.</summary>
    private static string Mutate(Random random)
    {
        if (random.Next(4) == 0)
        {
            var separator = random.Next(4) == 0 ? "" : " ";
            return string.Join(separator, Enumerable.Range(0, random.Next(1, 41)).Select(_ => Token(random)));
        }

        var text = _corpus[random.Next(_corpus.Count)];
        for (var edits = random.Next(1, 5); edits > 0; edits--)
        {
            text = Edit(random, text);
        }

        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    private static string Edit(Random random, string text)
    {
        var start = random.Next(text.Length + 1);
        var length = random.Next(Math.Min(12, text.Length - start) + 1);
        switch (random.Next(7))
        {
            case 0:
                return text.Insert(start, Token(random));
            case 1:
                return text.Remove(start, length);
            case 2:
                return text.Remove(start, length).Insert(start, Token(random));
            case 3:
                return text.Insert(start, text.Substring(start, length));
            case 4:
                var other = _corpus[random.Next(_corpus.Count)];
                var from = random.Next(other.Length + 1);
                return text.Insert(start, other.Substring(from, random.Next(other.Length - from + 1)));
            default:
                return random.Next(3) switch
                {
                    0 => $"({text})",
                    1 => $"not ({text})",
                    _ => $"{text} and {text}",
                };
        }
    }

    private static string Token(Random random) => _dictionary[random.Next(_dictionary.Length)];

    private static string[] BuildDictionary() =>
    [
        // The pieces of the parser's first, hand-made fuzz test.
        "a", "b1", "f", "(", ")", "(", ")", ",", ".", "-", "-", "+", "*", "/", "not", "and", "or", "is", "in",
        "null", "true", "==", "!=", "<", "<=", ">", ">=", "=", "!", "1", "2.5", "99999999999999999999", "'x'",
        "'", "''", "date", " ", "\n", "#", "é", "\ud83d",
        // The names of the scope and the functions.
        .. _fields.Keys, "manager", "name", "lineItems", "FinanceThreshold", "departments", "user",
        .. ExpressionFunctions.Names, .. ExpressionFunctions.AggregateNames,
        // Injection and edge text.
        "'x''); DROP TABLE t; --'", "'AXISFUZZ'", "';'", "'\\u0000'", "'\0'", "--", "/*", ";", "@f0", "$1", "\"t\"",
        "departments:IsManager", new string('7', 30), "0." + new string('3', 30), new string('(', 40),
    ];

    private static IReadOnlyList<string> ReadCorpus()
    {
        var corpus = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "samples", "apps"), "*.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            Collect(document.RootElement, corpus);
        }

        return [.. corpus.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>Collects the values of the properties that hold an expression, as docs/reference/expressions.md#writing-expressions-in-resource-files lists.</summary>
    private static void Collect(JsonElement element, List<string> corpus)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String && IsExpressionProperty(property.Name))
                    {
                        corpus.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        Collect(property.Value, corpus);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, corpus);
                }

                break;
        }
    }

    private static bool IsExpressionProperty(string name) =>
        name is "expression" or "filter" or "when" || name.EndsWith("When", StringComparison.Ordinal);

    private static Dictionary<string, ExpressionType> FuzzFields()
    {
        string[] statuses = ["draft", "submitted", "assigned", "resolved", "approved", "returned", "rejected", "retired"];
        return new Dictionary<string, ExpressionType>(StringComparer.OrdinalIgnoreCase)
        {
            // The generic fields of the translator tests.
            ["t"] = ExpressionType.Text,
            ["i"] = ExpressionType.Integer,
            ["d"] = ExpressionType.Decimal,
            ["b"] = ExpressionType.Boolean,
            ["dt"] = ExpressionType.Date,
            ["ts"] = ExpressionType.DateTime,
            ["e"] = ExpressionType.Enum("status", ["draft", "submitted"]),
            // The fields the samples use.
            ["status"] = ExpressionType.Enum("status", statuses),
            ["priority"] = ExpressionType.Enum("priority", ["low", "normal", "urgent"]),
            ["statusFilter"] = ExpressionType.EnumParameter("statusFilter", statuses),
            ["totalAmount"] = ExpressionType.Decimal,
            ["quantity"] = ExpressionType.Decimal,
            ["unitPrice"] = ExpressionType.Decimal,
            ["amount"] = ExpressionType.Decimal,
            ["requester"] = ExpressionType.Text,
            ["employee"] = ExpressionType.Text,
            ["holder"] = ExpressionType.Text,
            ["assignedAgent"] = ExpressionType.Text,
            ["firstDay"] = ExpressionType.Date,
            ["lastDay"] = ExpressionType.Date,
            ["warrantyEnd"] = ExpressionType.Date,
            ["department"] = ExpressionType.Reference("Department"),
        };
    }

    private static ExpressionScope FuzzScope()
    {
        var items = new ExpressionScope(new Dictionary<string, ExpressionType>
        {
            ["amount"] = ExpressionType.Decimal,
            ["quantity"] = ExpressionType.Decimal,
        });
        var rules = new List<ExpressionRule>
        {
            Rule("FinanceThreshold", "10000", ExpressionType.Decimal, null),
            Rule("IsManager", "manager == user", ExpressionType.Boolean, "departments", ("manager", ExpressionType.Text), ("user", ExpressionType.Text)),
        };
        return new ExpressionScope(
            _fields,
            rules,
            [new ExpressionCollection("lineItems", "LineItem", items)],
            (target, field) => target == "Department" ? field switch
            {
                "manager" or "name" => ExpressionType.Text,
                _ => null,
            } : null,
            ["departments"]);
    }

    private static ExpressionRule Rule(string name, string body, ExpressionType result, string? module, params (string Name, ExpressionType Type)[] parameters)
    {
        var parsed = ExpressionParser.Parse(body);
        Assert.True(parsed.Succeeded, parsed.Diagnostic?.Message);
        var scope = new ExpressionScope(parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Type));
        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, result);
        Assert.True(check.Succeeded, check.Diagnostic?.Message);
        return new ExpressionRule(name, [.. parameters.Select(parameter => new ExpressionRuleParameter(parameter.Name, parameter.Type))], result)
        {
            Module = module,
            Expression = body,
            Body = parsed.Expression,
            BodyCheck = check,
        };
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Axis.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Axis.slnx was not found above the test output folder.");
    }
}
