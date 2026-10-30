using Axis.Processes.Instances;

namespace Axis.Integration.Tests;

/// <summary>The signature of a webhook request, checked against the example of the Standard Webhooks specification.</summary>
public sealed class WebhookSignatureTests
{
    private const string Id = "msg_p5jXN8AQM9LWM0D4loKWxJek";
    private const long Timestamp = 1614265330;
    private const string Body = """{"test": 2432232314}""";
    private const string Secret = "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw";
    private const string Expected = "v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=";

    [Fact]
    public void A_whsec_secret_gives_the_signature_of_the_specification()
    {
        Assert.Equal(Expected, WebhookSignature.Sign(Id, Timestamp, Body, Secret));
    }

    [Fact]
    public void A_secret_without_the_prefix_is_used_as_its_utf8_bytes()
    {
        var plain = WebhookSignature.Sign(Id, Timestamp, Body, "plain-secret");

        Assert.StartsWith("v1,", plain, StringComparison.Ordinal);
        Assert.NotEqual(Expected, plain);
        Assert.Equal(plain, WebhookSignature.Sign(Id, Timestamp, Body, "plain-secret"));
    }

    [Fact]
    public void Two_secrets_give_two_signatures_in_the_order_of_the_secrets_and_extra_spaces_change_nothing()
    {
        var both = WebhookSignature.Sign(Id, Timestamp, Body, $"{Secret}  plain-secret ");

        Assert.Equal($"{Expected} {WebhookSignature.Sign(Id, Timestamp, Body, "plain-secret")}", both);
    }

    [Fact]
    public void The_id_joins_both_ids_with_an_underscore()
    {
        var eventId = Guid.NewGuid();
        var webhookId = Guid.NewGuid();

        Assert.Equal($"{eventId:D}_{webhookId:D}", WebhookSignature.Id(eventId, webhookId));
    }

    [Fact]
    public void A_whsec_secret_that_is_not_base64_is_refused_without_quoting_it()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => WebhookSignature.Sign(Id, Timestamp, Body, "whsec_not base64!"));

        Assert.DoesNotContain("base64!", exception.Message, StringComparison.Ordinal);
    }
}
