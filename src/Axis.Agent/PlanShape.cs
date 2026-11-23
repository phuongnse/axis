using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Agent.Models;

namespace Axis.Agent;

/// <summary>
/// The shape of a plan and its title, which the planning step takes from the model's <c>propose_plan</c> call, see <c>docs/reference/authoring-requests.md#states</c>.
/// <see cref="Tool"/> is the tool the model is offered. <see cref="TryRead"/> checks a call strictly and builds the plan as an object with all
/// eight groups, so a stored plan always has them.
/// </summary>
public static class PlanShape
{
    /// <summary>The most characters a title has.</summary>
    internal const int MaxTitle = 80;

    /// <summary>The most bytes a plan has as JSON.</summary>
    internal const int MaxBytes = 64 * 1024;

    /// <summary>The name of the tool the model calls to end planning.</summary>
    internal const string ToolName = "propose_plan";

    /// <summary>Axis's checklist, see <c>docs/reference/authoring-requests.md#states</c>. A plan answers each item, in this order and word for word.</summary>
    public static readonly IReadOnlyList<string> ChecklistItems =
    [
        "The draft does what the request and the approved plan say, and nothing more.",
        "A change that a built-in module covers uses it, with a customization layer.",
        "Each behaviour that the draft adds or changes has a scenario.",
        "Every text the draft adds has each locale the application offers.",
    ];

    private static readonly HashSet<string> _inputKeys = new(StringComparer.Ordinal) { "application", "title", "plan" };

    private static readonly HashSet<string> _planKeys = new(StringComparer.Ordinal)
    {
        "summary", "questions", "changes", "decisions", "risks", "checks", "checklist", "details",
    };

    /// <summary>The <c>propose_plan</c> tool as the model is offered it. The agent host answers its call itself.</summary>
    internal static ModelTool Tool { get; } = new(
        ToolName,
        "Ends planning. Call it once, with the title and the whole plan, when you have read what you need. "
        + "A call that does not fit the shape returns an error that says what is wrong, and you may call it again.",
        JsonSerializer.SerializeToElement(Schema()));

    /// <summary>
    /// Checks the input of a <c>propose_plan</c> call and builds the title and the plan. Unknown properties are refused, <c>summary</c> and
    /// at least one change are required, <c>checklist</c> holds <see cref="ChecklistItems"/> in order, and the plan is at most <see cref="MaxBytes"/> as UTF-8 JSON.
    /// A missing optional group is stored as an empty list.
    /// </summary>
    /// <returns><see langword="true"/> with the title and the plan, or <see langword="false"/> with one short sentence that names what is wrong.</returns>
    internal static bool TryRead(JsonObject input, out string? title, out JsonObject? plan, out string? error)
    {
        title = null;
        plan = null;
        if (input.FirstOrDefault(pair => !_inputKeys.Contains(pair.Key)) is { Key: { } unknownInput })
        {
            error = $"The input has an unknown property '{unknownInput}'.";
            return false;
        }

        if (!TryText(input["title"], out var text))
        {
            error = "The title must be a text that is not blank.";
            return false;
        }

        if (text.Trim().Length > MaxTitle)
        {
            error = $"The title must have at most {MaxTitle} characters.";
            return false;
        }

        if (input["plan"] is not JsonObject given)
        {
            error = "The plan must be an object.";
            return false;
        }

        if (given.FirstOrDefault(pair => !_planKeys.Contains(pair.Key)) is { Key: { } unknownGroup })
        {
            error = $"The plan has an unknown property '{unknownGroup}'.";
            return false;
        }

        var built = new JsonObject();
        if (!TryText(given["summary"], out var summary))
        {
            error = "The plan's summary must be a text that is not blank.";
            return false;
        }

        built["summary"] = summary;
        if (!TryQuestions(given["questions"], out var questions, out error)
            || !TryTexts(given["changes"], "changes", required: true, out var changes, out error)
            || !TryDecisions(given["decisions"], out var decisions, out error)
            || !TryTexts(given["risks"], "risks", required: false, out var risks, out error)
            || !TryTexts(given["checks"], "checks", required: false, out var checks, out error)
            || !TryChecklist(given["checklist"], out var checklist, out error)
            || !TryTexts(given["details"], "details", required: false, out var details, out error))
        {
            return false;
        }

        built["questions"] = questions;
        built["changes"] = changes;
        built["decisions"] = decisions;
        built["risks"] = risks;
        built["checks"] = checks;
        built["checklist"] = checklist;
        built["details"] = details;
        if (Encoding.UTF8.GetByteCount(built.ToJsonString()) > MaxBytes)
        {
            error = $"The plan must have at most {MaxBytes / 1024} KiB.";
            return false;
        }

        error = null;
        title = text.Trim();
        plan = built;
        return true;
    }

    private static bool TryText(JsonNode? node, out string text)
    {
        text = string.Empty;
        if (node is JsonValue value && value.TryGetValue<string>(out var read) && !string.IsNullOrWhiteSpace(read))
        {
            text = read;
            return true;
        }

        return false;
    }

    private static bool TryTexts(JsonNode? node, string name, bool required, out JsonArray texts, out string? error)
    {
        texts = [];
        error = null;
        if (node is null)
        {
            error = required ? $"The plan's {name} must have at least one text." : null;
            return !required;
        }

        if (node is not JsonArray array || (required && array.Count == 0))
        {
            error = required ? $"The plan's {name} must be a list with at least one text." : $"The plan's {name} must be a list of texts.";
            return false;
        }

        foreach (var item in array)
        {
            if (!TryText(item, out var text))
            {
                error = $"Each of the plan's {name} must be a text that is not blank.";
                return false;
            }

            texts.Add(text);
        }

        return true;
    }

    private static bool TryDecisions(JsonNode? node, out JsonArray decisions, out string? error)
    {
        decisions = [];
        error = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonArray array)
        {
            error = "The plan's decisions must be a list of { decision, reason }.";
            return false;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject entry || entry.Count != 2
                || !TryText(entry["decision"], out var decision) || !TryText(entry["reason"], out var reason))
            {
                error = "Each of the plan's decisions must be { decision, reason }, both texts that are not blank.";
                return false;
            }

            decisions.Add(new JsonObject { ["decision"] = decision, ["reason"] = reason });
        }

        return true;
    }

    private static bool TryQuestions(JsonNode? node, out JsonArray questions, out string? error)
    {
        questions = [];
        error = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonArray array)
        {
            error = "The plan's questions must be a list of { question, options, recommended }.";
            return false;
        }

        foreach (var item in array)
        {
            if (item is not JsonObject entry || entry.Count != 3
                || !TryText(entry["question"], out var question)
                || !TryTexts(entry["options"], "question options", required: false, out var options, out _)
                || entry["options"] is null)
            {
                error = "Each of the plan's questions must be { question, options, recommended }, with a question and a list of options that are texts.";
                return false;
            }

            if (entry["recommended"] is not JsonValue number || !number.TryGetValue<int>(out var recommended) || recommended < 0 || recommended > options.Count)
            {
                error = $"A question's recommended must be a whole number from 0 to {options.Count}, the number of an option counting from 1, or 0 for none.";
                return false;
            }

            questions.Add(new JsonObject { ["question"] = question, ["options"] = options, ["recommended"] = recommended });
        }

        return true;
    }

    private static bool TryChecklist(JsonNode? node, out JsonArray checklist, out string? error)
    {
        checklist = [];
        error = null;
        if (node is not JsonArray array || array.Count != ChecklistItems.Count)
        {
            error = $"The plan's checklist must have {ChecklistItems.Count} entries {{ item, how }}, one for each item of Axis's checklist, in order.";
            return false;
        }

        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonObject entry || entry.Count != 2
                || entry["item"] is not JsonValue item || !item.TryGetValue<string>(out var given) || !string.Equals(given, ChecklistItems[index], StringComparison.Ordinal))
            {
                error = $"Entry {index + 1} of the plan's checklist must have the item \"{ChecklistItems[index]}\" word for word.";
                return false;
            }

            if (!TryText(entry["how"], out var how))
            {
                error = $"Entry {index + 1} of the plan's checklist must say how the plan meets the item, in a text that is not blank.";
                return false;
            }

            checklist.Add(new JsonObject { ["item"] = given, ["how"] = how });
        }

        return true;
    }

    private static JsonObject Schema()
    {
        static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };
        static JsonObject Texts(string description) => new() { ["type"] = "array", ["description"] = description, ["items"] = new JsonObject { ["type"] = "string" } };

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("title", "plan"),
            ["properties"] = new JsonObject
            {
                ["application"] = Text("The application's name."),
                ["title"] = new JsonObject
                {
                    ["type"] = "string",
                    ["maxLength"] = MaxTitle,
                    ["description"] = $"What the request does, in a few words, at most {MaxTitle} characters.",
                },
                ["plan"] = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = new JsonArray("summary", "changes", "checklist"),
                    ["properties"] = new JsonObject
                    {
                        ["summary"] = Text("What the plan does, in a sentence or two."),
                        ["questions"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "What you cannot decide alone. Leave it out when there is none.",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["additionalProperties"] = false,
                                ["required"] = new JsonArray("question", "options", "recommended"),
                                ["properties"] = new JsonObject
                                {
                                    ["question"] = new JsonObject { ["type"] = "string" },
                                    ["options"] = Texts("The answers the person may choose from."),
                                    ["recommended"] = new JsonObject
                                    {
                                        ["type"] = "integer",
                                        ["minimum"] = 0,
                                        ["description"] = "The number of the option you recommend, counting from 1, or 0 for none.",
                                    },
                                },
                            },
                        },
                        ["changes"] = Texts("What the application will do differently, one text for each change, in words alone. At least one."),
                        ["decisions"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "Each choice you made that the request did not say, and why.",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["additionalProperties"] = false,
                                ["required"] = new JsonArray("decision", "reason"),
                                ["properties"] = new JsonObject
                                {
                                    ["decision"] = new JsonObject { ["type"] = "string" },
                                    ["reason"] = new JsonObject { ["type"] = "string" },
                                },
                            },
                        },
                        ["risks"] = Texts("What the change does to the data and the people already there, and where it stops."),
                        ["checks"] = Texts("How the result will be checked: the scenarios that will show it works."),
                        ["checklist"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "One entry for each item of Axis's checklist, in order, with the item word for word and how the plan meets it. The items are: "
                                + string.Join(" ", ChecklistItems.Select((item, index) => $"{index + 1}. {item}")),
                            ["minItems"] = ChecklistItems.Count,
                            ["maxItems"] = ChecklistItems.Count,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["additionalProperties"] = false,
                                ["required"] = new JsonArray("item", "how"),
                                ["properties"] = new JsonObject
                                {
                                    ["item"] = new JsonObject { ["type"] = "string" },
                                    ["how"] = new JsonObject { ["type"] = "string" },
                                },
                            },
                        },
                        ["details"] = Texts("The implementation notes: the edits, resource by resource."),
                    },
                },
            },
        };
    }
}
