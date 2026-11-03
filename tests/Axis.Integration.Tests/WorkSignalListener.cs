using System.Threading.Channels;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>A connection to the platform database that runs <c>LISTEN axis_work</c> and keeps the tenant ids it is sent.</summary>
internal sealed class WorkSignalListener : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly Channel<string> _signals = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _stop = new();
    private Task _loop = Task.CompletedTask;

    private WorkSignalListener(NpgsqlConnection connection) => _connection = connection;

    public static async Task<WorkSignalListener> StartAsync(string platformConnectionString)
    {
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(platformConnectionString) { Pooling = false }.ConnectionString);
        var listener = new WorkSignalListener(connection);
        connection.Notification += (_, notification) => listener._signals.Writer.TryWrite(notification.Payload);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var listen = new NpgsqlCommand("LISTEN axis_work", connection))
        {
            await listen.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        listener._loop = listener.WaitAsync();
        return listener;
    }

    /// <summary>Waits up to <paramref name="within"/> for a signal that names <paramref name="tenant"/>. Other signals are skipped.</summary>
    public async Task<bool> ReceivesAsync(string tenant, TimeSpan within)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(within);
        try
        {
            while (await _signals.Reader.WaitToReadAsync(timeout.Token))
            {
                if (_signals.Reader.TryRead(out var payload) && payload == tenant)
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
        }

        return false;
    }

    /// <summary>Forgets the signals received so far.</summary>
    public void Drain()
    {
        while (_signals.Reader.TryRead(out _))
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        await _connection.DisposeAsync();
        _stop.Dispose();
    }

    private async Task WaitAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await _connection.WaitAsync(_stop.Token);
        }
    }
}
