namespace Axis.Shared.Domain.Primitives;

/// <summary>
/// Well-known error codes used by Result.Failure to drive HTTP status mapping.
/// Endpoints call result.ToProblemDetails() which switches on these codes.
/// </summary>
public static class ErrorCodes
{
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string BusinessRule = "business_rule";
    public const string Forbidden = "forbidden";
    public const string PlanLimit = "plan_limit";
    public const string FieldValidation = "field_validation";
    public const string InvalidInput = "invalid_input";
    public const string RateLimited = "rate_limited";
}

/// <summary>
/// Represents the outcome of an operation that can either succeed or fail.
/// Use instead of throwing exceptions for expected domain failures.
/// </summary>
public class Result
{
    protected Result(
        bool isSuccess,
        string? errorCode,
        string? error,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        PlanLimitFailureDetails? planLimitDetails = null)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("A success result cannot have an error.");
        if (!isSuccess && error is null)
            throw new InvalidOperationException("A failure result must have an error.");

        IsSuccess = isSuccess;
        _errorCode = errorCode;
        _error = error;
        // Defensive copy: prevents callers from mutating error payloads after result creation.
        _fieldErrors = fieldErrors?.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        _planLimitDetails = planLimitDetails;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private readonly string? _errorCode;
    private readonly string? _error;
    private readonly IReadOnlyDictionary<string, string[]>? _fieldErrors;
    private readonly PlanLimitFailureDetails? _planLimitDetails;

    public PlanLimitFailureDetails? PlanLimitDetails => _planLimitDetails;

    /// <summary>Well-known code from ErrorCodes — used by ToProblemDetails() to pick HTTP status.</summary>
    public string? ErrorCode => IsFailure
        ? _errorCode
        : throw new InvalidOperationException("A success result has no error.");

    public string Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A success result has no error.");

    /// <summary>Per-field validation errors. Only populated when ErrorCode == ErrorCodes.FieldValidation.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors => _fieldErrors;

    // ── Factory methods ─────────────────────────────────────────────────────

    public static Result Success() => new(true, null, null);

    /// <summary>Failure without a specific code — maps to HTTP 422 by default.</summary>
    public static Result Failure(string error) => new(false, null, error);

    /// <summary>Failure with a well-known code from <see cref="ErrorCodes"/> — maps to the correct HTTP status.</summary>
    public static Result Failure(string code, string error) => new(false, code, error);

    public static Result PlanLimitFailure(PlanLimitFailureDetails details) =>
        new(false, ErrorCodes.PlanLimit, details.Message, null, details);

    /// <summary>Field-level validation failure — maps to HTTP 422 ValidationProblemDetails with per-field errors.</summary>
    public static Result FieldValidation(IReadOnlyDictionary<string, string[]> fieldErrors)
        => new(false, ErrorCodes.FieldValidation, "One or more validation errors occurred.", fieldErrors);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);
    public static Result<TValue> Failure<TValue>(string error) => Result<TValue>.Failure(error);
    public static Result<TValue> Failure<TValue>(string code, string error) => Result<TValue>.Failure(code, error);

    /// <summary>Field-level validation failure returning a typed result.</summary>
    public static Result<TValue> FieldValidation<TValue>(IReadOnlyDictionary<string, string[]> fieldErrors)
        => Result<TValue>.FieldValidation(fieldErrors);
}

/// <summary>
/// Represents the outcome of an operation that produces a value on success.
/// </summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(
        bool isSuccess,
        TValue? value,
        string? errorCode,
        string? error,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        PlanLimitFailureDetails? planLimitDetails = null)
        : base(isSuccess, errorCode, error, fieldErrors, planLimitDetails)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failure result has no value.");

    public static Result<TValue> Success(TValue value) => new(true, value, null, null);
    public new static Result<TValue> Failure(string error) => new(false, default, null, error);
    public new static Result<TValue> Failure(string code, string error) => new(false, default, code, error);

    public new static Result<TValue> PlanLimitFailure(PlanLimitFailureDetails details) =>
        new(false, default, ErrorCodes.PlanLimit, details.Message, null, details);
    public new static Result<TValue> FieldValidation(IReadOnlyDictionary<string, string[]> fieldErrors)
        => new(false, default, ErrorCodes.FieldValidation, "One or more validation errors occurred.", fieldErrors);

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
