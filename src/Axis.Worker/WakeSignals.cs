using System.Threading.Channels;

namespace Axis.Worker;

/// <summary>
/// The wake signals that reached the worker and the poll has not read yet. The entry is the id of the tenant to check at once,
/// or null to poll every tenant. No notification payload is null, so null cannot be sent from the database.
/// </summary>
internal sealed class WakeSignals
{
    private readonly Channel<string?> _channel = Channel.CreateUnbounded<string?>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<string?> Reader => _channel.Reader;

    /// <summary>Asks the poll to check the tenant and put it in the active list.</summary>
    public void Wake(string tenantId) => _channel.Writer.TryWrite(tenantId);

    /// <summary>Asks the poll to check every tenant.</summary>
    public void WakeAll() => _channel.Writer.TryWrite(null);
}
