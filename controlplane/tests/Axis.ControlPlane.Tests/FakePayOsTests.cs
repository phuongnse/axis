using Axis.ControlPlane.Billing;

namespace Axis.ControlPlane.Tests;

public sealed class FakePayOsTests
{
    [Fact]
    public async Task A_status_read_answers_pending_for_a_created_code_and_refuses_an_unknown_one()
    {
        var payOs = new FakePayOs();
        await payOs.CreatePaymentLinkAsync(new PayOsPaymentRequest(7, 120000, "AXIS 7"), TestContext.Current.CancellationToken);

        var status = await payOs.GetPaymentStatusAsync(7, TestContext.Current.CancellationToken);

        Assert.Equal(new PayOsPaymentStatus(7, "PENDING", 120000, 0), status);
        await Assert.ThrowsAsync<PayOsException>(() => payOs.GetPaymentStatusAsync(8, TestContext.Current.CancellationToken));
    }
}
