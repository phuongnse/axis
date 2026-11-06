namespace Axis.ControlPlane.Dns;

/// <summary>
/// Sets and removes the <c>CNAME</c> record of one tenant subdomain, see <c>docs/reference/region-directory.md#routing-a-host</c>.
/// A name is a fully qualified host name without the trailing dot, such as <c>acme.vn.example.com</c>, and names compare
/// without regard to case. Both calls are idempotent.
/// </summary>
internal interface IDnsRecords
{
    /// <summary>The time to live of every record, in seconds.</summary>
    public const int TimeToLive = 300;

    /// <summary>Makes <paramref name="name"/> an alias of <paramref name="target"/>, in place of any <c>CNAME</c> it has.</summary>
    /// <exception cref="DnsUpdateException">The server refused or did not answer the update.</exception>
    Task SetCnameAsync(string name, string target, CancellationToken cancellationToken);

    /// <summary>Removes the <c>CNAME</c> of <paramref name="name"/>. A name with none stays as it is.</summary>
    /// <exception cref="DnsUpdateException">The server refused or did not answer the update.</exception>
    Task RemoveCnameAsync(string name, CancellationToken cancellationToken);
}
