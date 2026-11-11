namespace Axis.ControlPlane.Billing;

/// <summary>
/// PayOS in memory, for tests and Development. The settings refuse it in every other environment. It answers the same link for the same
/// <see cref="PayOsPaymentRequest.OrderCode"/>, and a call after <see cref="FailNext"/> is set throws once. A status read answers <c>PENDING</c> for
/// a code it has created, and throws for any other.
/// </summary>
internal sealed class FakePayOs : IPayOs
{
    private readonly object _gate = new();
    private readonly List<PayOsPaymentRequest> _requests = [];
    private readonly Dictionary<long, long> _amounts = [];
    private bool _failNext;

    /// <summary>A copy of the requests got so far, in order, with the failed ones.</summary>
    public IReadOnlyList<PayOsPaymentRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>When set, the next call throws <see cref="PayOsException"/> and the flag resets.</summary>
    public bool FailNext
    {
        get
        {
            lock (_gate)
            {
                return _failNext;
            }
        }

        set
        {
            lock (_gate)
            {
                _failNext = value;
            }
        }
    }

    public Task<PayOsPaymentLink> CreatePaymentLinkAsync(PayOsPaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _requests.Add(request);
            ThrowIfFailNext();
            _amounts[request.OrderCode] = request.Amount;
        }

        var code = request.OrderCode;
        return Task.FromResult(new PayOsPaymentLink(
            $"fake-{code}",
            $"https://payos.invalid/web/fake-{code}",
            $"fake-vietqr-{code}",
            "970000",
            "0000000000",
            "AXIS FAKE",
            request.Description));
    }

    public Task<PayOsPaymentStatus> GetPaymentStatusAsync(long orderCode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ThrowIfFailNext();
            if (!_amounts.TryGetValue(orderCode, out var amount))
            {
                throw new PayOsException("The fake PayOS has no payment link with this order code.");
            }

            return Task.FromResult(new PayOsPaymentStatus(orderCode, "PENDING", amount, 0));
        }
    }

    // Called with the gate held.
    private void ThrowIfFailNext()
    {
        if (_failNext)
        {
            _failNext = false;
            throw new PayOsException("The fake PayOS was set to fail this call.");
        }
    }
}
