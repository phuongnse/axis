using System.Text.RegularExpressions;
using Axis.Shared.Domain.Primitives;

namespace Axis.Rules.Domain;

public abstract record RuleOutcome
{
    protected RuleOutcome(RuleOutcomeKind kind)
    {
        Kind = kind;
    }

    public RuleOutcomeKind Kind { get; }
}

public sealed partial record RuleValidationOutcome : RuleOutcome
{
    private RuleValidationOutcome(string code, RuleSeverity severity, string message)
        : base(RuleOutcomeKind.Validation)
    {
        Code = code;
        Severity = severity;
        Message = message;
    }

    public string Code { get; }
    public RuleSeverity Severity { get; }
    public string Message { get; }

    public static Result<RuleValidationOutcome> Create(
        string code,
        RuleSeverity severity,
        string message)
    {
        string normalizedCode = code?.Trim() ?? string.Empty;
        if (!OutcomeCodePattern().IsMatch(normalizedCode))
            return Result.Failure<RuleValidationOutcome>("Rule validation outcome code format is invalid.");

        if (!Enum.IsDefined(severity))
            return Result.Failure<RuleValidationOutcome>("Rule validation severity is not supported.");

        string normalizedMessage = message?.Trim() ?? string.Empty;
        if (normalizedMessage.Length == 0 || normalizedMessage.Length > 500)
            return Result.Failure<RuleValidationOutcome>("Rule validation message is required and cannot exceed 500 characters.");

        return new RuleValidationOutcome(normalizedCode, severity, normalizedMessage);
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*(\\.[a-z][a-z0-9_]*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex OutcomeCodePattern();
}

public sealed record RuleDecisionOutcome : RuleOutcome
{
    private RuleDecisionOutcome(RuleDecision decision)
        : base(RuleOutcomeKind.Decision)
    {
        Decision = decision;
    }

    public RuleDecision Decision { get; }

    public static Result<RuleDecisionOutcome> Create(RuleDecision decision) =>
        Enum.IsDefined(decision)
            ? new RuleDecisionOutcome(decision)
            : Result.Failure<RuleDecisionOutcome>("Rule decision is not supported.");
}
