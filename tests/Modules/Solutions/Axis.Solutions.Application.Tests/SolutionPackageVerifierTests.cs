using System.Security.Cryptography;
using System.Text;
using Axis.Solutions.Application;

namespace Axis.Solutions.Application.Tests;

public sealed class SolutionPackageVerifierTests
{
    private const string OpenApiHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task Verifier_UrlSafeEnvelope_VerifiesExactPayload()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = CreatePayload();
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        byte[] envelope = CreateEnvelope(payload, signature, urlSafe: true, padded: false);
        SolutionPackageVerifier verifier = new(new KeyReader(key));

        VerifiedSolutionPackage result = await verifier.VerifyAsync(envelope, OpenApiHash, TestContext.Current.CancellationToken);

        Assert.Equal("reference_application", result.SolutionKey);
        Assert.Equal(payload, result.PayloadBytes);
        Assert.Single(result.Components);
    }

    [Fact]
    public async Task Verifier_StandardEnvelope_AcceptsPayload()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = CreatePayload();
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        SolutionPackageVerifier verifier = new(new KeyReader(key));

        VerifiedSolutionPackage result = await verifier.VerifyAsync(
            CreateEnvelope(payload, signature, urlSafe: false, padded: true),
            OpenApiHash,
            TestContext.Current.CancellationToken);

        Assert.Equal("0.1.0", result.SolutionVersion);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"solutionKey\":\"reference_application\",\"solutionVersion\":\"0.1.0\",\"axisOpenApiSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"publisher\":{\"publisherId\":\"axis\",\"publisherKeyId\":\"release_key\"},\"provenance\":{\"sourceRevision\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"buildId\":\"build\\u000Aone\",\"builtAt\":\"2026-08-07T00:00:00Z\",\"sourceUri\":\"https://example.test/reference\"},\"components\":[]}")]
    [InlineData("{ \"schemaVersion\":1 }")]
    public async Task Verifier_NoncanonicalPayload_Rejects(string json)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        SolutionPackageVerifier verifier = new(new KeyReader(key));

        SolutionPackageException exception = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            verifier.VerifyAsync(CreateEnvelope(payload, signature, true, false), OpenApiHash, TestContext.Current.CancellationToken));

        Assert.Contains("canonical", exception.ProblemCode);
    }

    [Fact]
    public async Task Verifier_InvalidSignature_Rejects()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = CreatePayload();
        byte[] signature = new byte[64];
        SolutionPackageVerifier verifier = new(new KeyReader(key));

        await Assert.ThrowsAsync<SolutionPackageException>(() =>
            verifier.VerifyAsync(CreateEnvelope(payload, signature, true, false), OpenApiHash, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SolutionPackageException>(() =>
            verifier.VerifyAsync(CreateEnvelope(payload, signature, true, false), new string('c', 64), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Verifier_EmptyObjectPae_MatchesVector()
    {
        Assert.Equal(
            "DSSEv1 37 application/vnd.axis.solution.v1+json 2 {}",
            Encoding.ASCII.GetString(SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, "{}"u8)));
    }

    private static byte[] CreatePayload()
    {
        byte[] component = "{\"schemaVersion\":1}"u8.ToArray();
        string componentHash = Convert.ToHexString(SHA256.HashData(component)).ToLowerInvariant();
        string content = Convert.ToBase64String(component).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        string json = "{\"schemaVersion\":1,\"solutionKey\":\"reference_application\",\"solutionVersion\":\"0.1.0\",\"axisOpenApiSha256\":\"" + OpenApiHash +
            "\",\"publisher\":{\"publisherId\":\"axis\",\"publisherKeyId\":\"release_key\"},\"provenance\":{\"sourceRevision\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"buildId\":\"build-1\",\"builtAt\":\"2026-08-07T00:00:00Z\",\"sourceUri\":\"https://example.test/reference\"},\"components\":[{\"type\":\"authorization.policy.v1\",\"key\":\"reference\",\"sha256\":\"" + componentHash +
            "\",\"content\":\"" + content + "\",\"dependsOn\":[]}]}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] CreateEnvelope(byte[] payload, byte[] signature, bool urlSafe, bool padded)
    {
        static string Encode(byte[] bytes, bool useUrlSafe, bool usePadding)
        {
            string value = Convert.ToBase64String(bytes);
            if (useUrlSafe)
                value = value.Replace('+', '-').Replace('/', '_');
            return usePadding ? value : value.TrimEnd('=');
        }

        string json = "{\"payloadType\":\"" + SolutionPackageVerifier.PayloadType + "\",\"payload\":\"" +
            Encode(payload, urlSafe, padded) + "\",\"signatures\":[{\"keyid\":\"ignored-hint\",\"sig\":\"" +
            Encode(signature, urlSafe, padded) + "\"}],\"ignored\":true}";
        return Encoding.UTF8.GetBytes(json);
    }

    private sealed class KeyReader(ECDsa key) : ITrustedPublisherKeyReader
    {
        private readonly string _pem = key.ExportSubjectPublicKeyInfoPem();

        public Task<TrustedPublisherSnapshot?> FindAsync(
            string publisherId,
            string keyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TrustedPublisherSnapshot?>(new(
                publisherId,
                keyId,
                _pem,
                IsActive: true,
                IsTombstone: false,
                ConfigurationRevision: 1));
    }
}
