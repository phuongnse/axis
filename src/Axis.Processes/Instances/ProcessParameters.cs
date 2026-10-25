using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Records;
using Axis.Expressions.Evaluation;

namespace Axis.Processes.Instances;

/// <summary>
/// Reads the stored parameter values of a process without a subject and evaluates its expressions
/// over them. The expressions read no record, so no record is locked or read.
/// </summary>
internal static class ProcessParameters
{
    /// <summary>
    /// Maps each parameter the process declares to the CLR value the interpreter reads, matching names
    /// ignoring letter case. A parameter the values lack reads as null.
    /// </summary>
    public static ParameterValues Values(ProcessModel process, JsonObject? parameters)
    {
        ArgumentNullException.ThrowIfNull(process);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        string? inexact = null;
        foreach (var parameter in process.Parameters)
        {
            var node = parameters?.FirstOrDefault(entry => string.Equals(entry.Key, parameter.Name, StringComparison.OrdinalIgnoreCase)).Value;
            if (!RecordClrValues.TryFromStored(parameter.Type, node, out var value))
            {
                inexact ??= parameter.Name;
            }

            values[parameter.Name] = value;
        }

        return new ParameterValues(new ExpressionValues(values), inexact);
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> over <paramref name="values"/>, with <paramref name="now"/> as
    /// the time of <c>now()</c>. A parameter that holds a decimal that cannot be held exactly is a
    /// <see cref="ExpressionRuntimeErrorKind.DecimalOverflow"/> error, as it is for a record field.
    /// </summary>
    public static ExpressionEvaluationResult Evaluate(ExpressionModel expression, ParameterValues values, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(values);
        return values.Inexact is { } name
            ? new ExpressionEvaluationResult(null, new ExpressionRuntimeError(
                ExpressionRuntimeErrorKind.DecimalOverflow,
                $"The stored value of the parameter '{name}' cannot be held exactly as a decimal at character 1.",
                0))
            : ExpressionInterpreter.Evaluate(expression.Syntax, expression.Check, values.Values, null, now);
    }
}

/// <summary>The values of a process's parameters, and the name of the first one that cannot be held exactly, or null.</summary>
internal sealed record ParameterValues(ExpressionValues Values, string? Inexact);
