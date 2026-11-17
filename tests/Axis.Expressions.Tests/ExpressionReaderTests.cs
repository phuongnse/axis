using Axis.Expressions.Reading;
using Axis.Expressions.Typing;

namespace Axis.Expressions.Tests;

public sealed class ExpressionReaderTests
{
    private static readonly ExpressionType _status = ExpressionType.Enum("status", ["draft", "submitted"]);

    private static readonly ExpressionScope _scope = new(
        new Dictionary<string, ExpressionType>
        {
            ["status"] = _status,
            ["amount"] = ExpressionType.Decimal,
            ["urgent"] = ExpressionType.Boolean,
            ["dueDate"] = ExpressionType.Date,
            ["note"] = ExpressionType.Text,
            ["supplier"] = ExpressionType.Reference("supplier"),
        },
        rules: [new ExpressionRule("IsLarge", [new ExpressionRuleParameter("value", ExpressionType.Decimal)], ExpressionType.Boolean)],
        referenceFields: (target, name) => target == "supplier" && name == "name" ? ExpressionType.Text : null,
        labelKeys: new Dictionary<string, string>
        {
            ["status"] = "pr.status",
            ["amount"] = "pr.amount",
            ["urgent"] = "pr.urgent",
            ["dueDate"] = "pr.dueDate",
            ["supplier"] = "pr.supplier",
        },
        valueLabelKeys: new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["status"] = new Dictionary<string, string> { ["draft"] = "pr.status.draft", ["submitted"] = "pr.status.submitted" },
        },
        referenceLabels: (target, name) => target == "supplier" && name == "name"
            ? new ExpressionScopeField("name", ExpressionType.Text, "supplier.name")
            : null);

    private static readonly ExpressionPlace _place = new(_scope, ExpressionType.Null, false, false, false, false);

    private static ReadingNode Read(string text, ExpressionPlace? place = null) =>
        ExpressionReader.Read(text, place ?? _place) ?? throw new InvalidOperationException($"'{text}' has no reading.");

    private static ReadingPhrase Phrase(string text, string words, ExpressionPlace? place = null)
    {
        var phrase = Assert.IsType<ReadingPhrase>(Read(text, place));
        Assert.Equal(words, phrase.Words);
        return phrase;
    }

    [Fact]
    public void A_path_reads_as_its_last_step_of_the_rest_with_the_label_keys_of_the_fields_it_resolves_to()
    {
        var phrase = Phrase("supplier.name", "of");

        Assert.Equal(new ReadingName("name", "supplier.name"), Assert.Single(phrase.Slots[0]));
        Assert.Equal(new ReadingName("supplier", "pr.supplier"), Assert.Single(phrase.Slots[1]));
    }

    [Fact]
    public void A_path_in_a_scope_without_label_resolution_has_no_label_keys()
    {
        var scope = new ExpressionScope(
            new Dictionary<string, ExpressionType> { ["supplier"] = ExpressionType.Reference("supplier") },
            referenceFields: (_, _) => ExpressionType.Text);
        var phrase = Assert.IsType<ReadingPhrase>(ExpressionReader.Read("supplier.name", new ExpressionPlace(scope, ExpressionType.Null, false, false, false, false)));

        Assert.Equal(new ReadingName("name", null), Assert.Single(phrase.Slots[0]));
    }

    [Fact]
    public void A_yes_or_no_field_on_its_own_reads_as_yes_and_after_not_as_no()
    {
        Assert.Equal(new ReadingName("urgent", "pr.urgent"), Assert.Single(Phrase("urgent", "is-true").Slots[0]));
        Assert.Equal(new ReadingName("urgent", "pr.urgent"), Assert.Single(Phrase("not urgent", "is-false").Slots[0]));
    }

    [Fact]
    public void A_yes_or_no_field_inside_a_comparison_reads_as_a_name()
    {
        var phrase = Phrase("urgent == true", "equal");

        Assert.IsType<ReadingName>(Assert.Single(phrase.Slots[0]));
        Assert.Equal(new ReadingValue("boolean", true, null), Assert.Single(phrase.Slots[1]));
    }

    [Fact]
    public void An_enum_value_in_a_list_carries_its_label_key()
    {
        var phrase = Phrase("status in ('draft', 'submitted')", "in");

        Assert.Equal(new ReadingName("status", "pr.status"), Assert.Single(phrase.Slots[0]));
        Assert.Equal(
            [new ReadingValue("enum", "draft", "pr.status.draft"), new ReadingValue("enum", "submitted", "pr.status.submitted")],
            phrase.Slots[1]);
    }

    [Fact]
    public void A_text_compared_with_an_enum_reads_as_an_enum_value_on_either_side()
    {
        Assert.Equal(new ReadingValue("enum", "draft", "pr.status.draft"), Assert.Single(Phrase("status == 'draft'", "equal").Slots[1]));
        Assert.Equal(new ReadingValue("enum", "draft", "pr.status.draft"), Assert.Single(Phrase("'draft' != status", "not-equal").Slots[0]));
    }

    [Fact]
    public void An_enum_without_value_labels_reads_its_values_with_no_label_key()
    {
        var scope = new ExpressionScope(new Dictionary<string, ExpressionType> { ["status"] = _status });
        var phrase = Assert.IsType<ReadingPhrase>(ExpressionReader.Read("status == 'draft'", new ExpressionPlace(scope, ExpressionType.Null, false, false, false, false)));

        Assert.Equal(new ReadingValue("enum", "draft", null), Assert.Single(phrase.Slots[1]));
    }

    [Fact]
    public void A_text_compared_with_a_text_stays_text()
    {
        Assert.Equal(new ReadingValue("text", "x", null), Assert.Single(Phrase("note == 'x'", "equal").Slots[1]));
    }

    [Theory]
    [InlineData("amount * 1.1", "multiply")]
    [InlineData("amount + 1", "add")]
    [InlineData("amount - 1", "subtract")]
    [InlineData("amount / 2", "divide")]
    [InlineData("-amount", "negate")]
    [InlineData("amount is null", "is-null")]
    [InlineData("amount is not null", "is-not-null")]
    [InlineData("amount > 1", "greater")]
    [InlineData("amount >= 1", "greater-or-equal")]
    [InlineData("amount < 1", "less")]
    [InlineData("amount <= 1", "less-or-equal")]
    [InlineData("dueDate > today()", "greater.date")]
    [InlineData("dueDate >= today()", "greater-or-equal.date")]
    [InlineData("dueDate < today()", "less.date")]
    [InlineData("today() <= dueDate", "less-or-equal.date")]
    [InlineData("not (amount > 1)", "not")]
    [InlineData("IsLarge(amount)", "rule")]
    [InlineData("daysBetween(today(), dueDate) > 3", "greater")]
    public void Each_operator_reads_with_its_words(string text, string words) => Phrase(text, words);

    [Fact]
    public void A_number_reads_as_a_value_and_a_negated_number_folds_into_it()
    {
        var phrase = Phrase("amount * 1.1", "multiply");

        Assert.Equal(new ReadingName("amount", "pr.amount"), Assert.Single(phrase.Slots[0]));
        Assert.Equal(new ReadingValue("decimal", 1.1m, null), Assert.Single(phrase.Slots[1]));
        Assert.Equal(new ReadingValue("integer", -2L, null), Read("-2"));
    }

    [Fact]
    public void A_date_literal_reads_as_a_date_value()
    {
        Assert.Equal(new ReadingValue("date", "2026-10-08", null), Assert.Single(Phrase("dueDate == date('2026-10-08')", "equal").Slots[1]));
        Assert.Equal(new ReadingValue("date-time", "2026-10-08T09:30:00Z", null), Read("dateTime('2026-10-08T09:30:00Z')"));
    }

    [Fact]
    public void A_function_reads_as_its_name_with_a_slot_for_each_argument()
    {
        var phrase = Phrase("daysBetween(today(), dueDate) + 1", "add");
        var call = Assert.IsType<ReadingPhrase>(Assert.Single(phrase.Slots[0]));

        Assert.Equal("fn.daysBetween", call.Words);
        Assert.Equal(2, call.Slots.Count);
        Assert.Equal("fn.today", Assert.IsType<ReadingPhrase>(Assert.Single(call.Slots[0])).Words);
    }

    [Fact]
    public void A_rule_call_reads_as_the_rule_with_its_arguments()
    {
        var phrase = Phrase("IsLarge(amount)", "rule");

        Assert.Equal(new ReadingName("IsLarge", null), Assert.Single(phrase.Slots[0]));
        Assert.Equal(new ReadingName("amount", "pr.amount"), Assert.Single(phrase.Slots[1]));
    }

    [Fact]
    public void A_run_of_and_reads_as_one_all_group_and_a_run_of_or_as_one_any_group()
    {
        var all = Assert.IsType<ReadingGroup>(Read("urgent and amount > 1 and status == 'draft'"));
        var any = Assert.IsType<ReadingGroup>(Read("urgent or amount > 1 or status == 'draft'"));

        Assert.Equal("all", all.Kind);
        Assert.Equal(["is-true", "greater", "equal"], all.Items.Cast<ReadingPhrase>().Select(item => item.Words));
        Assert.Equal("any", any.Kind);
        Assert.Equal(3, any.Items.Count);
    }

    [Fact]
    public void Not_before_a_group_reads_as_not_all_or_none_and_a_group_inside_a_group_stays_one_item()
    {
        var notAll = Assert.IsType<ReadingGroup>(Read("not (urgent and amount > 1)"));
        var none = Assert.IsType<ReadingGroup>(Read("not (urgent or amount > 1 or note is null)"));
        var nested = Assert.IsType<ReadingGroup>(Read("urgent and (amount > 1 or note is null)"));

        Assert.Equal("notAll", notAll.Kind);
        Assert.Equal(2, notAll.Items.Count);
        Assert.Equal("none", none.Kind);
        Assert.Equal(3, none.Items.Count);
        Assert.Equal("all", nested.Kind);
        Assert.Equal("any", Assert.IsType<ReadingGroup>(nested.Items[1]).Kind);
    }

    [Fact]
    public void An_expression_with_a_parse_error_or_a_type_error_has_no_reading()
    {
        Assert.Null(ExpressionReader.Read("1 +", _place));
        Assert.Null(ExpressionReader.Read("amount > 'x'", _place));
        Assert.Null(ExpressionReader.Read("unknown", _place));
    }

    [Fact]
    public void A_place_checked_without_a_type_check_reads_with_no_label_keys_and_the_plain_words()
    {
        var phrase = Assert.IsType<ReadingPhrase>(ExpressionReader.Read("dueDate < today()", null));

        Assert.Equal("less", phrase.Words);
        Assert.Equal(new ReadingName("dueDate", null), Assert.Single(phrase.Slots[0]));
    }

    [Fact]
    public void An_item_expression_reads_its_names_in_the_scope_of_the_items_and_outer_reads_as_the_field()
    {
        var phrase = Assert.IsType<ReadingPhrase>(ExpressionReader.Read("any(lines, price > outer.amount)", LinesPlace()));
        var comparison = Assert.IsType<ReadingPhrase>(Assert.Single(phrase.Slots[1]));

        Assert.Equal("fn.any", phrase.Words);
        Assert.Equal(new ReadingName("lines", null), Assert.Single(phrase.Slots[0]));
        Assert.Equal(new ReadingName("price", "line.price"), Assert.Single(comparison.Slots[0]));
        Assert.Equal(new ReadingName("amount", "pr.amount"), Assert.Single(comparison.Slots[1]));
    }

    [Fact]
    public void Count_with_a_condition_reads_with_its_own_words_and_count_alone_with_the_plain_ones()
    {
        var where = Phrase("count(lines, price > 1)", "fn.count.where", LinesPlace());
        var plain = Phrase("count(lines)", "fn.count", LinesPlace());

        Assert.Equal(2, where.Slots.Count);
        Assert.Equal(new ReadingName("lines", null), Assert.Single(where.Slots[0]));
        Assert.Single(plain.Slots);
    }

    [Fact]
    public void A_function_that_takes_any_number_of_arguments_reads_them_all_in_one_list_slot()
    {
        var phrase = Phrase("concat(note, 'x', note)", "fn.concat");

        Assert.Equal(new ReadingNode[] { new ReadingName("note", null), new ReadingValue("text", "x", null), new ReadingName("note", null) }, Assert.Single(phrase.Slots));
    }

    [Fact]
    public void A_nested_call_reads_in_its_own_phrase_and_a_number_reads_as_its_value()
    {
        var phrase = Phrase("addDays(today(), 7)", "fn.addDays");
        var today = Assert.IsType<ReadingPhrase>(Assert.Single(phrase.Slots[0]));

        Assert.Equal(2, phrase.Slots.Count);
        Assert.Equal("fn.today", today.Words);
        Assert.Empty(today.Slots);
        Assert.Equal(new ReadingValue("integer", 7L, null), Assert.Single(phrase.Slots[1]));
    }

    /// <summary>A place with the collection <c>lines</c> of items that have a <c>price</c>, and the field <c>amount</c> outside it.</summary>
    private static ExpressionPlace LinesPlace()
    {
        var items = new ExpressionScope(
            new Dictionary<string, ExpressionType> { ["price"] = ExpressionType.Decimal },
            labelKeys: new Dictionary<string, string> { ["price"] = "line.price" });
        var scope = new ExpressionScope(
            new Dictionary<string, ExpressionType> { ["amount"] = ExpressionType.Decimal },
            collections: [new ExpressionCollection("lines", "line", items)],
            labelKeys: new Dictionary<string, string> { ["amount"] = "pr.amount" });
        return new ExpressionPlace(scope, ExpressionType.Null, false, false, false, false);
    }

    [Theory]
    [InlineData("urgent")]
    [InlineData("not urgent")]
    [InlineData("urgent and amount > 1 or not (note is null or status in ('draft'))")]
    [InlineData("supplier.name == 'x'")]
    [InlineData("dueDate < today() and dueDate >= date('2026-01-01')")]
    [InlineData("-amount + 1 - 2 * 3 / 4 != 0")]
    [InlineData("note is not null and IsLarge(amount * 2)")]
    public void Every_operator_word_the_reader_answers_is_in_its_list(string text)
    {
        foreach (var words in WordsIn(Read(text)).Where(words => !words.StartsWith("fn.", StringComparison.Ordinal)))
        {
            Assert.Contains(words, ExpressionReader.OperatorWords);
        }
    }

    [Fact]
    public void The_texts_to_ship_hold_every_operator_word_and_the_texts_of_values_and_groups()
    {
        Assert.Subset(ExpressionReader.TextKeys.ToHashSet(), ExpressionReader.OperatorWords.ToHashSet());
        Assert.Contains("value.null", ExpressionReader.TextKeys);
        Assert.Contains("group.none", ExpressionReader.TextKeys);
        Assert.Contains("join.or", ExpressionReader.TextKeys);
        Assert.Equal(ExpressionReader.TextKeys.Count, ExpressionReader.TextKeys.Distinct().Count());
    }

    private static IEnumerable<string> WordsIn(ReadingNode node) => node switch
    {
        ReadingGroup group => group.Items.SelectMany(WordsIn),
        ReadingPhrase phrase => phrase.Slots.SelectMany(slot => slot).SelectMany(WordsIn).Prepend(phrase.Words),
        _ => [],
    };
}
