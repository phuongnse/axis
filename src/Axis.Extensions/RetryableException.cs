namespace Axis.Extensions;

/// <summary>Thrown by an operation to say that the attempt may succeed later. Any other exception fails the call.</summary>
public class RetryableException : Exception
{
    public RetryableException()
    {
    }

    public RetryableException(string message)
        : base(message)
    {
    }

    public RetryableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
