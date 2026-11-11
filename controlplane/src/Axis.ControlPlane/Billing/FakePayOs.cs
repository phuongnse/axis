namespace Axis.ControlPlane.Billing;

/// <summary>
/// PayOS in memory, for tests and Development. The settings refuse it in every other environment. It answers the same link for the same
/// <see cref="PayOsPaymentRequest.OrderCode"/>, and a call after <see cref="FailNext"/> is set throws once.
/// </summary>
internal sealed class FakePayOs : IPayOs
{
    private readonly object _gate = new();
    private readonly List<PayOsPaymentRequest> _requests = [];
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
            if (_failNext)
            {
                _failNext = false;
                throw new PayOsException("The fake PayOS was set to fail this call.");
            }
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
}
