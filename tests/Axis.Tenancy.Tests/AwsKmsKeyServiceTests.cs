using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.KeyManagementService;
using Amazon.Runtime;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Platform;
using Microsoft.Extensions.Configuration;

namespace Axis.Tenancy.Tests;

/// <summary>The <c>aws-kms</c> key service against a stub KMS endpoint, which shows the requests and the handling of the answers, not AWS.</summary>
public sealed class AwsKmsKeyServiceTests
{
    private const string KeyId = "arn:aws:kms:eu-west-1:111122223333:key/0a1b2c3d-4e5f-6789-abcd-ef0123456789";

    [Fact]
    public void Decrypt_is_sent_with_the_key_id_and_the_purpose_and_the_plaintext_is_the_installation_key()
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var ciphertext = RandomNumberGenerator.GetBytes(48);
        var stub = new StubKms(_ => Answer.Json(new { KeyId, Plaintext = Convert.ToBase64String(Encoding.UTF8.GetBytes(plain)), EncryptionAlgorithm = "SYMMETRIC_DEFAULT" }));

        var cipher = TenantSecretCipher.Load(Settings(Convert.ToBase64String(ciphertext)), ServiceOf(stub));

        var request = Assert.Single(stub.Requests);
        Assert.Equal("TrentService.Decrypt", request.Target);
        Assert.Equal(KeyId, request.Body.GetProperty("KeyId").GetString());
        Assert.Equal(Convert.ToBase64String(ciphertext), request.Body.GetProperty("CiphertextBlob").GetString());
        var context = request.Body.GetProperty("EncryptionContext");
        Assert.Single(context.EnumerateObject());
        Assert.Equal("installation-key", context.GetProperty("axis:purpose").GetString());
        var stored = TenantSecretCipher.FromKey(Convert.FromBase64String(plain)).Encrypt("a", TenantSecretFields.RuntimePassword, "p4ssw0rd");
        Assert.Equal("p4ssw0rd", cipher.Decrypt("a", TenantSecretFields.RuntimePassword, stored).Reveal());
    }

    [Fact]
    public void The_host_key_is_unwrapped_with_its_own_purpose()
    {
        var stub = new StubKms(_ => Answer.Json(new { KeyId, Plaintext = Convert.ToBase64String("pem"u8.ToArray()) }));

        var key = ServiceOf(stub).Unwrap(KeyService.AgentHostKey, "Ai:HostKeyFile", Convert.ToBase64String(new byte[10]));

        Assert.Equal("pem", key.Reveal());
        Assert.Equal("agent-host-key", Assert.Single(stub.Requests).Body.GetProperty("EncryptionContext").GetProperty("axis:purpose").GetString());
    }

    [Theory]
    [InlineData("Platform:SecretKey", 1)]
    [InlineData("Platform:PreviousSecretKey", 2)]
    public void An_access_denied_answer_stops_the_load_and_names_the_setting_and_the_key_id_setting_and_holds_no_value(string setting, int deniedCall)
    {
        var wrapped = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var key = Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToBase64String(new byte[32])));
        var stub = new StubKms(call => call == deniedCall
            ? Answer.Error("AccessDeniedException", "User: arn:aws:sts::123:assumed-role/marker-role is not authorized")
            : Answer.Json(new { KeyId, Plaintext = key }));
        var configuration = Configure(("Platform:SecretKey", wrapped), ("Platform:PreviousSecretKey", wrapped));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(configuration, ServiceOf(stub)));

        Assert.Equal($"The setting {setting} does not unwrap at the key of KeyService:KeyId (AccessDeniedException).", exception.Message);
        Assert.Null(exception.InnerException);
        var text = exception.ToString();
        foreach (var hidden in new[] { "marker-role", KeyId, wrapped, "111122223333" })
        {
            Assert.DoesNotContain(hidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_invalid_ciphertext_such_as_a_plain_key_is_refused_with_its_error_code()
    {
        var plain = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var stub = new StubKms(_ => Answer.Error("InvalidCiphertextException", "The ciphertext refers to a customer master key that does not exist"));

        var exception = Assert.Throws<InvalidOperationException>(() => TenantSecretCipher.Load(Settings(plain), ServiceOf(stub)));

        Assert.Equal("The setting Platform:SecretKey does not unwrap at the key of KeyService:KeyId (InvalidCiphertextException).", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(plain, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_that_is_not_base64_makes_no_call()
    {
        var stub = new StubKms(_ => Answer.Error("AccessDeniedException", "unused"));

        var exception = Assert.Throws<InvalidOperationException>(() => ServiceOf(stub).Unwrap(KeyService.InstallationKey, "Platform:SecretKey", "this is not base64 !!"));

        Assert.Equal("The setting Platform:SecretKey does not unwrap at the key of KeyService:KeyId.", exception.Message);
        Assert.Empty(stub.Requests);
    }

    [Fact]
    public void An_unreachable_endpoint_stops_the_load_and_says_so()
    {
        var stub = new StubKms(_ => throw new HttpRequestException("connection refused by marker-host"));

        var exception = Assert.Throws<InvalidOperationException>(() => ServiceOf(stub).Unwrap(KeyService.InstallationKey, "Platform:SecretKey", Convert.ToBase64String(new byte[10])));

        Assert.Equal("The setting Platform:SecretKey does not unwrap at the key of KeyService:KeyId (unreachable).", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("marker-host", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Wrap_sends_Encrypt_with_the_key_id_the_purpose_and_the_key_text_and_returns_the_ciphertext_in_base64()
    {
        var ciphertext = RandomNumberGenerator.GetBytes(48);
        var stub = new StubKms(_ => Answer.Json(new { CiphertextBlob = Convert.ToBase64String(ciphertext), KeyId }));

        var wrapped = ServiceOf(stub).Wrap(KeyService.InstallationKey, "the key text");

        var request = Assert.Single(stub.Requests);
        Assert.Equal("TrentService.Encrypt", request.Target);
        Assert.Equal(KeyId, request.Body.GetProperty("KeyId").GetString());
        Assert.Equal("the key text", Encoding.UTF8.GetString(Convert.FromBase64String(request.Body.GetProperty("Plaintext").GetString()!)));
        Assert.Equal("installation-key", request.Body.GetProperty("EncryptionContext").GetProperty("axis:purpose").GetString());
        Assert.Equal(Convert.ToBase64String(ciphertext), wrapped);
    }

    [Fact]
    public void A_refused_wrap_names_the_key_id_setting_and_holds_no_value()
    {
        var stub = new StubKms(_ => Answer.Error("AccessDeniedException", "User: arn:aws:sts::123:assumed-role/marker-role is not authorized"));

        var exception = Assert.Throws<InvalidOperationException>(() => ServiceOf(stub).Wrap(KeyService.AgentHostKey, "secret-key-text"));

        Assert.Equal("The key does not wrap at the key of KeyService:KeyId (AccessDeniedException).", exception.Message);
        Assert.Null(exception.InnerException);
        foreach (var hidden in new[] { "marker-role", KeyId, "secret-key-text" })
        {
            Assert.DoesNotContain(hidden, exception.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_kind_loads_from_the_settings_with_a_key_arn_and_needs_no_credential_in_them()
    {
        var service = KeyService.Load(Configure(("KeyService:Kind", "aws-kms"), ("KeyService:KeyId", KeyId)));

        Assert.NotNull(service);
    }

    private static AwsKmsKeyService ServiceOf(StubKms stub)
    {
        var client = new AmazonKeyManagementServiceClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonKeyManagementServiceConfig
            {
                ServiceURL = "https://kms.stub.test",
                AuthenticationRegion = "eu-west-1",
                MaxErrorRetry = 0,
                HttpClientFactory = new StubFactory(stub),
            });
        return new AwsKmsKeyService(client, KeyId);
    }

    private static IConfiguration Settings(string secretKey) => Configure(("Platform:SecretKey", secretKey));

    private static IConfiguration Configure(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value))).Build();

    private sealed record Recorded(string? Target, JsonElement Body);

    private sealed class Answer
    {
        public required HttpStatusCode Status { get; init; }
        public required string Body { get; init; }
        public string? ErrorType { get; init; }

        public static Answer Json(object body) => new() { Status = HttpStatusCode.OK, Body = JsonSerializer.Serialize(body) };

        public static Answer Error(string code, string message) => new()
        {
            Status = HttpStatusCode.BadRequest,
            Body = JsonSerializer.Serialize(new Dictionary<string, string> { ["__type"] = code, ["message"] = message }),
            ErrorType = code,
        };
    }

    /// <summary>Records each request and answers it. The function gets the number of the call, starting at 1.</summary>
    private sealed class StubKms(Func<int, Answer> answer) : HttpMessageHandler
    {
        public List<Recorded> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Recorded(request.Headers.TryGetValues("X-Amz-Target", out var target) ? target.Single() : null, JsonDocument.Parse(body).RootElement.Clone()));
            var result = answer(Requests.Count);
            var response = new HttpResponseMessage(result.Status) { Content = new StringContent(result.Body, Encoding.UTF8, "application/x-amz-json-1.1") };
            if (result.ErrorType is not null)
            {
                response.Headers.TryAddWithoutValidation("x-amzn-ErrorType", result.ErrorType);
            }

            return response;
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);
    }
}
