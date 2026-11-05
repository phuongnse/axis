namespace Axis.ControlPlane.Cells;

/// <summary>
/// A call to a cell failed. The message names the cell, the call and the status, and never a body, a token or a secret.
/// </summary>
internal sealed class CellCallException : Exception
{
    public CellCallException(string message, int? status, Exception? innerException = null)
        : base(message, innerException)
    {
        Status = status;
    }

    /// <summary>The HTTP status the cell or the token endpoint answered, or null when no answer came.</summary>
    public int? Status { get; }
}
