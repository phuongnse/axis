namespace Axis.Shared.Application;

/// <summary>
/// Thrown by IUnitOfWork.SaveChangesAsync when the database rejects a write due to an
/// optimistic concurrency conflict (xmin mismatch). Handlers catch this to return a
/// business-safe conflict instead of leaking database exceptions.
/// </summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(Exception? innerException = null)
        : base("A concurrency conflict was detected — another process modified this record.", innerException) { }
}
