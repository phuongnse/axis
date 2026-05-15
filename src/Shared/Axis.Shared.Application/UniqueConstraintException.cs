namespace Axis.Shared.Application;

/// <summary>
/// Thrown by IUnitOfWork.SaveChangesAsync when the database rejects a write due to
/// a unique constraint violation. Handlers map this to ErrorCodes.Conflict.
/// </summary>
public sealed class UniqueConstraintException : Exception
{
    public UniqueConstraintException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
