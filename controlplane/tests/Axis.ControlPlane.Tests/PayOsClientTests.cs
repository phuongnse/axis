using System.Net;
using System.Text;
using System.Text.Json;
using Axis.ControlPlane.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axis.ControlPlane.Tests;

/// <summary>
/// The real PayOS adapter against a recording handler, see <c>docs/reference/payments.md#paying-in-vietnam</c>: the headers and the signature of a request, the signature
/// check of an answer, and that no key reaches a log or a message. Each expected signature is a hex literal computed outside the code with
/// <c>printf '%s' '&lt;text&gt;' | openssl dgst -sha256 -hmac '&lt;checksum key&gt;'</c> over the text that PayOS's documentation says to sign.
/// </summary>
public sealed class PayOsClientTests
{
    private const string ClientId = "client-id-48151623420";
    private const string ApiKey = "api-key-91827364550";
    private const string ChecksumKey = "checksum-key-27182818280";
    private const string PublicUrl = "https://account.vn.example.com";

    // printf '%s' 'amount=250000&cancelUrl=https://account.vn.example.com&description=AXIS 42&orderCode=42&returnUrl=https://account.vn.example.com'
    private const string CreateRequestSignature = "e5cf14dac846fda147e7a1883da090eef1905d6e18e7885c19f00f288f2db1d0";

    // The keys are out of order and expiredAt is null, so the signed text has the keys sorted and expiredAt empty:
    // printf '%s' 'accountName=QUY VAC XIN&accountNumber=113366668888&amount=250000&bin=970415&checkoutUrl=https://pay.payos.vn/web/124c33293c43417ab7879e14c8d9eb18&currency=VND&description=AXIS 42&expiredAt=&orderCode=42&paymentLinkId=124c33293c43417ab7879e14c8d9eb18&qrCode=00020101021238570010A000000727&status=PENDING'
    private const string CreateAnswerSignature = "8506a42c0fc5cd7dd4e5d23c4e20099b616e2e442ee5d5c0846054908ff3c6e1";

    private const string CreateData = """
        {"bin":"970415","accountNumber":"113366668888","accountName":"QUY VAC XIN","currency":"VND","paymentLinkId":"124c33293c43417ab7879e14c8d9eb18",
         "amount":250000,"description":"AXIS 42","orderCode":42,"expiredAt":null,"status":"PENDING",
         "checkoutUrl":"https://pay.payos.vn/web/124c33293c43417ab7879e14c8d9eb18","qrCode":"00020101021238570010A000000727"}
        """;

    // The transactions are compact JSON with the keys of each one sorted, the non-ASCII text as it is, and the nulls kept:
    // printf '%s' 'amount=250000&amountPaid=250000&amountRemaining=0&canceledAt=&cancellationReason=&createdAt=2026-11-10T09:00:00+07:00&id=124c33293c43417ab7879e14c8d9eb18&orderCode=42&status=PAID&transactions=[{"accountNumber":"113366668888","amount":250000,"counterAccountBankId":null,"description":"AXIS 42 Thanh toán","reference":"FT26314","transactionDateTime":"2026-11-10 09:05:00","virtualAccountName":null,"virtualAccountNumber":null}]'
    private const string StatusAnswerSignature = "7e59d2937fd4566a5412af4114efa502332083a993f10ef4376d2b9b583ab18b";

    private const string StatusData = """
        {"id":"124c33293c43417ab7879e14c8d9eb18","orderCode":42,"amount":250000,"amountPaid":250000,"amountRemaining":0,"status":"PAID",
         "createdAt":"2026-11-10T09:00:00+07:00",
         "transactions":[{"reference":"FT26314","amount":250000,"accountNumber":"113366668888","description":"AXIS 42 Thanh toán",
           "transactionDateTime":"2026-11-10 09:05:00","virtualAccountName":null,"virtualAccountNumber":null,"counterAccountBankId":null}],
         "canceledAt":null,"cancellationReason":null}
        """;

    private static readonly PayOsPaymentRequest _request = new(42, 250000, "AXIS 42");

    private static readonly PayOsPaymentLink _expectedLink = new(
        "124c33293c43417ab7879e14c8d9eb18",
        "https://pay.payos.vn/web/124c33293c43417ab7879e14c8d9eb18",
        "00020101021238570010A000000727",
        "970415",
        "113366668888",
        "QUY VAC XIN",
        "AXIS 42");

    [Fact]
    public async Task A_create_request_carries_both_headers_and_the_signature_of_its_fields()
    {
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", CreateData, CreateAnswerSignature));

        await Client(handler).CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api-merchant.payos.test/v2/payment-requests", sent.Uri);
        Assert.Equal(ClientId, sent.Headers["x-client-id"]);
        Assert.Equal(ApiKey, sent.Headers["x-api-key"]);
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        Assert.Equal(42, root.GetProperty("orderCode").GetInt64());
        Assert.Equal(250000, root.GetProperty("amount").GetInt64());
        Assert.Equal("AXIS 42", root.GetProperty("description").GetString());
        Assert.Equal(PublicUrl, root.GetProperty("returnUrl").GetString());
        Assert.Equal(PublicUrl, root.GetProperty("cancelUrl").GetString());
        Assert.Equal(CreateRequestSignature, root.GetProperty("signature").GetString());
    }

    [Fact]
    public async Task A_correctly_signed_create_answer_is_mapped_to_the_payment_link()
    {
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", CreateData, CreateAnswerSignature));

        var link = await Client(handler).CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken);

        Assert.Equal(_expectedLink, link);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("8506a42c0fc5cd7dd4e5d23c4e20099b616e2e442ee5d5c0846054908ff3c6e")]
    [InlineData("")]
    public async Task A_create_answer_with_a_wrong_signature_is_refused(string signature)
    {
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", CreateData, signature));

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS answered without a valid signature.", exception.Message);
    }

    [Fact]
    public async Task A_create_answer_whose_data_was_changed_after_it_was_signed_is_refused()
    {
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", CreateData.Replace("113366668888", "999999999999", StringComparison.Ordinal), CreateAnswerSignature));

        await Assert.ThrowsAsync<PayOsException>(() => Client(handler).CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_create_answer_without_a_signature_is_refused()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, $$"""{"code":"00","desc":"success","data":{{CreateData}}}"""));

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS answered without a valid signature.", exception.Message);
    }

    [Fact]
    public async Task The_status_read_asks_for_the_order_code_and_maps_a_correctly_signed_answer()
    {
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", StatusData, StatusAnswerSignature));

        var status = await Client(handler).GetPaymentStatusAsync(42, TestContext.Current.CancellationToken);

        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        Assert.Equal("https://api-merchant.payos.test/v2/payment-requests/42", sent.Uri);
        Assert.Equal(ClientId, sent.Headers["x-client-id"]);
        Assert.Equal(ApiKey, sent.Headers["x-api-key"]);
        Assert.Equal(new PayOsPaymentStatus(42, "PAID", 250000, 250000), status);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("")]
    public async Task A_status_answer_with_a_wrong_signature_is_refused(string signature)
    {
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", StatusData, signature));

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).GetPaymentStatusAsync(42, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS answered without a valid signature.", exception.Message);
    }

    [Fact]
    public async Task An_error_answer_is_refused_with_its_status_and_code_and_never_its_text()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, """{"code":"20","desc":"free text of PayOS","data":null,"signature":null}"""));

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS refused the request with HTTP 400 and code 20.", exception.Message);
    }

    [Fact]
    public async Task A_refusing_code_in_a_success_status_is_refused()
    {
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "231", CreateData, CreateAnswerSignature));

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS refused the request with HTTP 200 and code 231.", exception.Message);
    }

    [Fact]
    public async Task An_answer_that_is_not_JSON_is_refused_with_its_status_only()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>" + ApiKey + "</html>") });

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).GetPaymentStatusAsync(42, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS refused the request with HTTP 502 and code unknown.", exception.Message);
    }

    [Fact]
    public async Task A_signed_answer_that_lacks_a_field_is_refused()
    {
        // printf '%s' 'id=x&orderCode=42'
        var data = """{"id":"x","orderCode":42}""";
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", data, "f79809c0bd00ee00becad368cb4d6da395bd61c8275c4052b17235058fbfd4fa"));

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).GetPaymentStatusAsync(42, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS answered with data that could not be read.", exception.Message);
    }

    [Fact]
    public async Task A_connection_failure_is_refused_without_the_inner_text()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException($"connection to {ApiKey} failed"));

        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(handler).GetPaymentStatusAsync(42, TestContext.Current.CancellationToken));

        Assert.Equal("PayOS could not be reached.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task A_timeout_is_refused_but_a_cancelled_token_passes_through()
    {
        var timeout = new RecordingHandler(_ => throw new TaskCanceledException($"timed out {ApiKey}"));
        var exception = await Assert.ThrowsAsync<PayOsException>(() => Client(timeout).GetPaymentStatusAsync(42, TestContext.Current.CancellationToken));
        Assert.Equal("PayOS did not answer in time.", exception.Message);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new RecordingHandler(Answer(HttpStatusCode.OK, "00", StatusData, StatusAnswerSignature));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).GetPaymentStatusAsync(42, cancelled.Token));
    }

    [Fact]
    public async Task No_log_entry_and_no_exception_message_holds_a_key_on_a_success_or_on_an_error_answer()
    {
        var capture = new CaptureProvider();
        var answers = new Queue<HttpResponseMessage>();
        answers.Enqueue(Signed(HttpStatusCode.OK, "00", CreateData, CreateAnswerSignature));
        answers.Enqueue(Json(HttpStatusCode.BadRequest, $$"""{"code":"20","desc":"Invalid {{ApiKey}} {{ClientId}} {{ChecksumKey}}","data":null,"signature":null}"""));
        answers.Enqueue(Signed(HttpStatusCode.OK, "00", CreateData, "0000000000000000000000000000000000000000000000000000000000000000"));
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(capture));
        services.AddSingleton(Options());
        services.AddHttpClient<IPayOs, PayOsClient>().ConfigurePrimaryHttpMessageHandler(() => new RecordingHandler(_ => answers.Dequeue()));
        await using var provider = services.BuildServiceProvider();
        var payOs = provider.GetRequiredService<IPayOs>();
        var messages = new List<string>();

        await payOs.CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken);
        messages.Add((await Assert.ThrowsAsync<PayOsException>(() => payOs.CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken))).ToString());
        messages.Add((await Assert.ThrowsAsync<PayOsException>(() => payOs.CreatePaymentLinkAsync(_request, TestContext.Current.CancellationToken))).ToString());

        Assert.NotEmpty(capture.Entries);
        foreach (var text in capture.Entries.Concat(messages))
        {
            Assert.DoesNotContain(ClientId, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ApiKey, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ChecksumKey, text, StringComparison.Ordinal);
        }
    }

    private static ControlPlaneOptions Options() => new()
    {
        PublicUrl = PublicUrl,
        PayOs = new ControlPlanePayOsOptions
        {
            Provider = "PayOs",
            BaseUrl = "https://api-merchant.payos.test/",
            ClientId = ClientId,
            ApiKey = ApiKey,
            ChecksumKey = ChecksumKey,
        },
    };

    private static PayOsClient Client(HttpMessageHandler handler) => new(new HttpClient(handler), Options());

    private static Func<HttpRequestMessage, HttpResponseMessage> Answer(HttpStatusCode status, string code, string data, string signature) =>
        _ => Signed(status, code, data, signature);

    private static HttpResponseMessage Signed(HttpStatusCode status, string code, string data, string signature) =>
        Json(status, $$"""{"code":"{{code}}","desc":"success","data":{{data}},"signature":"{{signature}}"}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record Recorded(HttpMethod Method, string Uri, Dictionary<string, string> Headers, string Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new Recorded(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        private readonly List<string> _entries = [];

        public IReadOnlyCollection<string> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CaptureProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries)
                {
                    owner._entries.Add($"{formatter(state, exception)} {exception}");
                }
            }
        }
    }
}
