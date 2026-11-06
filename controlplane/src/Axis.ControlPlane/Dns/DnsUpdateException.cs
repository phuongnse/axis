namespace Axis.ControlPlane.Dns;

/// <summary>
/// A dynamic update of the DNS server failed. The message names the record and the answer code of the server, and never the key.
/// </summary>
internal sealed class DnsUpdateException : Exception
{
    public DnsUpdateException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
