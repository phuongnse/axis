using System.Text.Json.Nodes;

namespace Axis.Processes.Instances;

/// <summary>
/// The code of a failed step or a failed call attempt, stored next to its fixed error text, see
/// docs/reference/process-inspection.md#step-errors-in-inspection. Only <c>timeout</c> carries a value,
/// <c>seconds</c>, the wait that ran out. A problem never holds an exception message.
/// </summary>
/// <param name="Code">The code, such as <c>timeout</c>.</param>
/// <param name="Seconds">The wait that ran out, for <c>timeout</c>, or <see langword="null"/>.</param>
internal sealed record StepProblem(string Code, double? Seconds = null)
{
    /// <summary>The outside system could not be reached: a connection that never opened, a missing binding or a failed token request.</summary>
    public static readonly StepProblem Unavailable = new("unavailable");

    /// <summary>The outside system answered with a <c>5xx</c>.</summary>
    public static readonly StepProblem ServerError = new("serverError");

    /// <summary>The outside system answered with another status that is not <c>2xx</c>.</summary>
    public static readonly StepProblem Refused = new("refused");

    /// <summary>A connector call could not be sent, or its success could not be recorded.</summary>
    public static readonly StepProblem NotSent = new("notSent");

    /// <summary>Any other failure. Its stored error is the exception type.</summary>
    public static readonly StepProblem Exception = new("exception");

    /// <summary>An expression of the step failed.</summary>
    public static readonly StepProblem ExpressionFailed = new("expressionFailed");

    /// <summary>A write to a record was refused.</summary>
    public static readonly StepProblem WriteRefused = new("writeRefused");

    /// <summary>The wait of <paramref name="wait"/> ran out without an answer.</summary>
    public static StepProblem Timeout(TimeSpan wait) => new("timeout", wait.TotalSeconds);

    /// <summary>The problem of an answer with <paramref name="status"/> that is not <c>2xx</c>.</summary>
    public static StepProblem ForStatus(int status) => status is >= 500 and < 600 ? ServerError : Refused;

    /// <summary>The stored form: <c>{ "code", "values" }</c>.</summary>
    public string ToJsonString()
    {
        var values = new JsonObject();
        if (Seconds is { } seconds)
        {
            values["seconds"] = JsonValue.Create(seconds);
        }

        return new JsonObject { ["code"] = Code, ["values"] = values }.ToJsonString();
    }
}

/// <summary>A failure of a step: its fixed error text and its code, which is <see langword="null"/> for a text that has none.</summary>
internal sealed record StepFailure(string Error, StepProblem? Problem);
