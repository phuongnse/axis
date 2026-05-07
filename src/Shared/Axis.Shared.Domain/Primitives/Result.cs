namespace Axis.Shared.Domain.Primitives;

/// <summary>
/// Represents the outcome of an operation that can either succeed or fail.
/// Use this instead of throwing exceptions for expected domain failures.
/// </summary>
public class Result
{
    protected Result(bool isSuccess, string? error)
    {
        if (isSuccess && error is not null)
            throw new InvalidOperationException("A success result cannot have an error.");
        if (!isSuccess && error is null)
            throw new InvalidOperationException("A failure result must have an error.");

        IsSuccess = isSuccess;
        _error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;

    private readonly string? _error;
    public string Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A success result has no error.");

    public static Result Success() => new(true, null);
    public static Result Failure(string error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);
    public static Result<TValue> Failure<TValue>(string error) => Result<TValue>.Failure(error);
}

/// <summary>
/// Represents the outcome of an operation that produces a value on success.
/// </summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    private Result(bool isSuccess, TValue? value, string? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failure result has no value.");

    public static Result<TValue> Success(TValue value) => new(true, value, null);
    public new static Result<TValue> Failure(string error) => new(false, default, error);

    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
