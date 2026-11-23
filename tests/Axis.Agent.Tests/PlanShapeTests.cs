using System.Text.Json.Nodes;

namespace Axis.Agent.Tests;

public sealed class PlanShapeTests
{
    private static JsonObject Input(Action<JsonObject>? change = null, Action<JsonObject>? changePlan = null)
    {
        var input = new JsonObject
        {
            ["application"] = "PurchaseRequests",
            ["title"] = "Add a cost centre",
            ["plan"] = new JsonObject
            {
                ["summary"] = "Purchases get a cost centre.",
                ["changes"] = new JsonArray("Add a Cost centre field to the purchase form."),
                ["checklist"] = new JsonArray([.. PlanShape.ChecklistItems.Select(item => (JsonNode)new JsonObject { ["item"] = item, ["how"] = "It does." })]),
            },
        };
        changePlan?.Invoke((JsonObject)input["plan"]!);
        change?.Invoke(input);
        return input;
    }

    [Fact]
    public void A_valid_plan_is_accepted_with_all_eight_groups()
    {
        Assert.True(PlanShape.TryRead(Input(), out var title, out var plan, out var error), error);

        Assert.Equal("Add a cost centre", title);
        Assert.Equal(["summary", "questions", "changes", "decisions", "risks", "checks", "checklist", "details"], plan!.Select(pair => pair.Key));
        Assert.Equal("Purchases get a cost centre.", (string?)plan!["summary"]);
        Assert.Empty((JsonArray)plan["questions"]!);
        Assert.Single((JsonArray)plan["changes"]!);
        Assert.Equal(PlanShape.ChecklistItems.Count, ((JsonArray)plan["checklist"]!).Count);
    }

    [Fact]
    public void Every_group_of_a_full_plan_is_accepted_and_kept()
    {
        var input = Input(changePlan: plan =>
        {
            plan["questions"] = new JsonArray(new JsonObject { ["question"] = "Which team?", ["options"] = new JsonArray("Sales", "Finance"), ["recommended"] = 2 });
            plan["decisions"] = new JsonArray(new JsonObject { ["decision"] = "Use a list", ["reason"] = "It is shorter." });
            plan["risks"] = new JsonArray("Old purchases have none.");
            plan["checks"] = new JsonArray("A scenario sets a cost centre.");
            plan["details"] = new JsonArray("Edit forms/purchase.json.");
        });

        Assert.True(PlanShape.TryRead(input, out _, out var plan, out var error), error);

        Assert.Equal(2, (int)plan!["questions"]![0]!["recommended"]!);
        Assert.Equal("It is shorter.", (string?)plan["decisions"]![0]!["reason"]);
        Assert.Equal("Old purchases have none.", (string?)plan["risks"]![0]);
        Assert.Equal("Edit forms/purchase.json.", (string?)plan["details"]![0]);
    }

    [Fact]
    public void A_title_of_80_characters_is_accepted_and_one_of_81_is_refused()
    {
        Assert.True(PlanShape.TryRead(Input(input => input["title"] = new string('a', 80)), out _, out _, out _));

        Assert.False(PlanShape.TryRead(Input(input => input["title"] = new string('a', 81)), out _, out var plan, out var error));
        Assert.Null(plan);
        Assert.Contains("80", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_or_missing_title_is_refused(string? title)
    {
        Assert.False(PlanShape.TryRead(Input(input => input["title"] = title), out _, out _, out var error));
        Assert.Contains("title", error);
    }

    [Fact]
    public void A_plan_with_no_summary_is_refused()
    {
        Assert.False(PlanShape.TryRead(Input(changePlan: plan => plan.Remove("summary")), out _, out _, out var error));
        Assert.Contains("summary", error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_plan_with_no_changes_is_refused(bool listed)
    {
        Assert.False(PlanShape.TryRead(Input(changePlan: plan =>
        {
            if (listed)
            {
                plan["changes"] = new JsonArray();
            }
            else
            {
                plan.Remove("changes");
            }
        }), out _, out _, out var error));
        Assert.Contains("changes", error);
    }

    [Fact]
    public void A_wrong_checklist_is_refused()
    {
        var reworded = Input(changePlan: plan => plan["checklist"]![1]!["item"] = "A change that a module covers uses it.");
        var swapped = Input(changePlan: plan =>
        {
            var checklist = (JsonArray)plan["checklist"]!;
            var first = checklist[0]!;
            checklist.RemoveAt(0);
            checklist.Add(first);
        });
        var short_ = Input(changePlan: plan => ((JsonArray)plan["checklist"]!).RemoveAt(3));
        var missing = Input(changePlan: plan => plan.Remove("checklist"));
        var blankHow = Input(changePlan: plan => plan["checklist"]![2]!["how"] = " ");

        foreach (var input in new[] { reworded, swapped, short_, missing, blankHow })
        {
            Assert.False(PlanShape.TryRead(input, out _, out var plan, out var error));
            Assert.Null(plan);
            Assert.Contains("checklist", error);
        }
    }

    [Fact]
    public void An_unknown_property_is_refused_at_each_level()
    {
        Assert.False(PlanShape.TryRead(Input(input => input["extra"] = 1), out _, out _, out var inputError));
        Assert.Contains("extra", inputError);

        Assert.False(PlanShape.TryRead(Input(changePlan: plan => plan["extra"] = new JsonArray()), out _, out _, out var planError));
        Assert.Contains("extra", planError);

        Assert.False(PlanShape.TryRead(Input(changePlan: plan => plan["checklist"]![0]!["extra"] = "x"), out _, out _, out var entryError));
        Assert.Contains("checklist", entryError);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_question_recommends_an_option_or_none(int recommended)
    {
        var input = Input(changePlan: plan =>
            plan["questions"] = new JsonArray(new JsonObject { ["question"] = "Which?", ["options"] = new JsonArray("A", "B"), ["recommended"] = recommended }));

        Assert.False(PlanShape.TryRead(input, out _, out _, out var error));
        Assert.Contains("recommended", error);
    }

    [Fact]
    public void A_question_with_no_recommended_number_is_refused_and_zero_means_none()
    {
        var question = () => new JsonObject { ["question"] = "Which?", ["options"] = new JsonArray("A") };
        Assert.False(PlanShape.TryRead(Input(changePlan: plan => plan["questions"] = new JsonArray(question())), out _, out _, out _));

        var none = question();
        none["recommended"] = 0;
        Assert.True(PlanShape.TryRead(Input(changePlan: plan => plan["questions"] = new JsonArray(none)), out _, out _, out var error), error);
    }

    [Fact]
    public void A_group_of_the_wrong_kind_is_refused()
    {
        Assert.False(PlanShape.TryRead(Input(changePlan: plan => plan["risks"] = "One risk."), out _, out _, out var risks));
        Assert.Contains("risks", risks);

        Assert.False(PlanShape.TryRead(Input(changePlan: plan => plan["decisions"] = new JsonArray("Use a list")), out _, out _, out var decisions));
        Assert.Contains("decisions", decisions);

        Assert.False(PlanShape.TryRead(Input(input => input["plan"] = "A plan in words."), out _, out _, out var text));
        Assert.Contains("plan", text);
    }

    [Fact]
    public void A_plan_over_64_KiB_is_refused_and_one_under_it_is_accepted()
    {
        Assert.True(PlanShape.TryRead(Input(changePlan: plan => plan["details"] = new JsonArray(new string('a', 60 * 1024))), out _, out _, out var error), error);

        Assert.False(PlanShape.TryRead(Input(changePlan: plan => plan["details"] = new JsonArray(new string('a', 64 * 1024))), out _, out var plan, out error));
        Assert.Null(plan);
        Assert.Contains("64 KiB", error);
    }
}
