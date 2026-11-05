using DnsClient;

namespace Axis.Admin;

/// <summary>Reads the <c>TXT</c> records of a name, so that tests can answer in place of the network.</summary>
internal interface IDnsTxtResolver
{
    /// <summary>
    /// Returns the value of each <c>TXT</c> record of <paramref name="name"/>. A name with no record gives an empty list. A failure of
    /// the lookup, such as a timeout or a server that refuses, throws.
    /// </summary>
    Task<IReadOnlyList<string>> ResolveAsync(string name, CancellationToken cancellationToken);
}

/// <summary>
/// The resolver of the product. It uses <c>DnsClient</c>, because <c>System.Net.Dns</c> cannot read <c>TXT</c> records. It keeps no cache,
/// so a customer who fixes a record is seen at the next check. The strings of one record are joined with no separator.
/// </summary>
internal sealed class DnsClientTxtResolver : IDnsTxtResolver
{
    private readonly LookupClient _client = new(new LookupClientOptions { UseCache = false, Timeout = TimeSpan.FromSeconds(5) });

    public async Task<IReadOnlyList<string>> ResolveAsync(string name, CancellationToken cancellationToken)
    {
        var response = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: cancellationToken);
        if (response.Header.ResponseCode is not (DnsHeaderResponseCode.NoError or DnsHeaderResponseCode.NotExistentDomain))
        {
            throw new InvalidOperationException($"The name server answered {response.Header.ResponseCode}.");
        }

        return [.. response.Answers.TxtRecords().Select(record => string.Concat(record.Text))];
    }
}
