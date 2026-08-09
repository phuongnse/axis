using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public async Task Verifier_CommittedConformanceFixture_PreservesExactVerifiedBytes()
    {
        ConformanceFixture fixture = LoadFixture();
        byte[] envelope = Convert.FromBase64String(fixture.EnvelopeBase64);
        byte[] payload = Convert.FromBase64String(fixture.PayloadBase64);
        byte[] expectedPae = Convert.FromBase64String(fixture.PaeBase64);
        byte[] signature = Convert.FromBase64String(fixture.SignatureBase64);
        SolutionPackageVerifier verifier = new(new PemKeyReader(fixture.PublicKeyPem));

        VerifiedSolutionPackage result = await verifier.VerifyAsync(
            envelope,
            fixture.AxisOpenApiSha256,
            TestContext.Current.CancellationToken);

        Assert.Equal(payload, result.PayloadBytes);
        Assert.Equal(expectedPae, SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload));
        using ECDsa publicKey = ECDsa.Create();
        publicKey.ImportFromPem(fixture.PublicKeyPem);
        Assert.True(publicKey.VerifyData(
            expectedPae,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Verifier_ConformanceFixtureBase64Variant_Accepts(bool urlSafe, bool padded)
    {
        ConformanceFixture fixture = LoadFixture();
        byte[] payload = Convert.FromBase64String(fixture.PayloadBase64);
        byte[] signature = Convert.FromBase64String(fixture.SignatureBase64);
        byte[] envelope = CreateEnvelope(payload, signature, urlSafe, padded);
        SolutionPackageVerifier verifier = new(new PemKeyReader(fixture.PublicKeyPem));

        VerifiedSolutionPackage result = await verifier.VerifyAsync(
            envelope,
            fixture.AxisOpenApiSha256,
            TestContext.Current.CancellationToken);

        Assert.Equal(payload, result.PayloadBytes);
    }

    [Theory]
    [InlineData("bom")]
    [InlineData("newline")]
    [InlineData("duplicate")]
    [InlineData("long_newline")]
    [InlineData("lowercase_hex")]
    [InlineData("escaped_printable")]
    [InlineData("escaped_slash")]
    public async Task Verifier_NoncanonicalVector_Rejects(string vector)
    {
        byte[] canonical = CreatePayload();
        byte[] payload = vector switch
        {
            "bom" => [0xef, 0xbb, 0xbf, .. canonical],
            "newline" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical).Insert(1, "\n")),
            "duplicate" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(canonical)[..^1] + ",\"schemaVersion\":1}"),
            "long_newline" => ReplacePayload(canonical, "build-1", "build\\u000A1"),
            "lowercase_hex" => ReplacePayload(canonical, "build-1", "build\\u000b1"),
            "escaped_printable" => ReplacePayload(canonical, "build-1", "\\u0062uild-1"),
            "escaped_slash" => ReplacePayload(canonical, "https://example.test/reference", "https:\\/\\/example.test\\/reference"),
            _ => throw new ArgumentOutOfRangeException(nameof(vector)),
        };

        SolutionPackageException exception = await VerifyRejectedPayloadAsync(payload);

        Assert.Contains(
            exception.ProblemCode,
            new[] { "solutions.package.canonical_json_invalid", "solutions.package.duplicate_property" });
    }

    [Theory]
    [InlineData("build\\n1")]
    [InlineData("build\\u000B1")]
    public async Task Verifier_CanonicalControlEscape_Accepts(string buildId)
    {
        byte[] payload = CreatePayload(buildId);

        VerifiedSolutionPackage result = await VerifyPayloadAsync(payload);

        Assert.Equal(payload, result.PayloadBytes);
    }

    [Theory]
    [InlineData("whitespace")]
    [InlineData("bad_padding")]
    [InlineData("mixed_alphabet")]
    public async Task Verifier_InvalidEnvelopeBase64_Rejects(string vector)
    {
        ConformanceFixture fixture = LoadFixture();
        string payload = ToBase64(Convert.FromBase64String(fixture.PayloadBase64), urlSafe: false, padded: true);
        string signature = ToBase64(Convert.FromBase64String(fixture.SignatureBase64), urlSafe: false, padded: true);
        if (vector == "whitespace")
            payload = payload.Insert(4, " ");
        else if (vector == "bad_padding")
            payload += "===";
        else
            signature = signature.Replace('/', '_');
        byte[] envelope = CreateEncodedEnvelope(payload, signature, keyId: "release_key");
        SolutionPackageVerifier verifier = new(new PemKeyReader(fixture.PublicKeyPem));

        SolutionPackageException exception = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            verifier.VerifyAsync(
                envelope,
                fixture.AxisOpenApiSha256,
                TestContext.Current.CancellationToken));

        Assert.Equal("solutions.package.base64_invalid", exception.ProblemCode);
    }

    [Fact]
    public async Task Verifier_ComponentBase64UrlVector_EnforcesUnpaddedEncoding()
    {
        byte[] valid = CreatePayload([new TestComponent("component", "{}"u8.ToArray(), [])]);
        byte[] padded = ReplacePayload(valid, "\"content\":\"e30\"", "\"content\":\"e30=\"");

        VerifiedSolutionPackage accepted = await VerifyPayloadAsync(valid);
        SolutionPackageException rejected = await VerifyRejectedPayloadAsync(padded);

        Assert.Equal("{}"u8.ToArray(), accepted.Components.Single().Content);
        Assert.Equal("solutions.package.component_content_invalid", rejected.ProblemCode);
    }

    [Fact]
    public async Task Verifier_UnknownPayloadProperty_Rejects()
    {
        byte[] canonical = CreatePayload();
        byte[] payload = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(canonical)[..^1] + ",\"unknown\":true}");

        SolutionPackageException exception = await VerifyRejectedPayloadAsync(payload);

        Assert.Equal("solutions.package.payload_invalid", exception.ProblemCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Verifier_MissingOrEmptyKeyHint_Accepts(string? keyId)
    {
        ConformanceFixture fixture = LoadFixture();
        string payload = ToBase64(Convert.FromBase64String(fixture.PayloadBase64), urlSafe: true, padded: false);
        string signature = ToBase64(Convert.FromBase64String(fixture.SignatureBase64), urlSafe: true, padded: false);
        byte[] envelope = CreateEncodedEnvelope(payload, signature, keyId);
        SolutionPackageVerifier verifier = new(new PemKeyReader(fixture.PublicKeyPem));

        VerifiedSolutionPackage result = await verifier.VerifyAsync(
            envelope,
            fixture.AxisOpenApiSha256,
            TestContext.Current.CancellationToken);

        Assert.Equal("reference_application", result.SolutionKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("e30")]
    public async Task Verifier_InvalidSignatureLength_Rejects(string signature)
    {
        ConformanceFixture fixture = LoadFixture();
        string payload = ToBase64(Convert.FromBase64String(fixture.PayloadBase64), urlSafe: true, padded: false);
        byte[] envelope = CreateEncodedEnvelope(payload, signature, keyId: "release_key");
        SolutionPackageVerifier verifier = new(new PemKeyReader(fixture.PublicKeyPem));

        SolutionPackageException exception = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            verifier.VerifyAsync(
                envelope,
                fixture.AxisOpenApiSha256,
                TestContext.Current.CancellationToken));

        Assert.Contains(
            exception.ProblemCode,
            new[] { "solutions.package.envelope_invalid", "solutions.package.signature_invalid" });
    }

    [Theory]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public async Task Verifier_ComponentCountBoundary_EnforcesLimit(int count, bool accepted)
    {
        byte[] content = "{\"schemaVersion\":1}"u8.ToArray();
        TestComponent[] components = Enumerable.Range(0, count)
            .Select(index => new TestComponent($"component_{index:D3}", content, []))
            .ToArray();
        byte[] payload = CreatePayload(components);

        if (accepted)
            Assert.Equal(count, (await VerifyPayloadAsync(payload)).Components.Count);
        else
            Assert.Equal("solutions.package.components_invalid", (await VerifyRejectedPayloadAsync(payload)).ProblemCode);
    }

    [Theory]
    [InlineData(1048576, true)]
    [InlineData(1048577, false)]
    public async Task Verifier_ComponentByteBoundary_EnforcesLimit(int size, bool accepted)
    {
        byte[] payload = CreatePayload([new TestComponent("component", new byte[size], [])]);

        if (accepted)
            Assert.Equal(size, (await VerifyPayloadAsync(payload)).Components.Single().Content.Length);
        else
            Assert.Equal("solutions.package.component_hash_invalid", (await VerifyRejectedPayloadAsync(payload)).ProblemCode);
    }

    [Theory]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public async Task Verifier_DependencyEdgeBoundary_EnforcesLimit(int edgeCount, bool accepted)
    {
        byte[] payload = CreatePayload(CreateWideGraph(edgeCount));

        if (accepted)
            Assert.Equal(256, (await VerifyPayloadAsync(payload)).Components.Count);
        else
            Assert.Equal("solutions.package.dependencies_invalid", (await VerifyRejectedPayloadAsync(payload)).ProblemCode);
    }

    [Theory]
    [InlineData(32, true)]
    [InlineData(33, false)]
    public async Task Verifier_DependencyDepthBoundary_EnforcesLimit(int depth, bool accepted)
    {
        byte[] content = "{\"schemaVersion\":1}"u8.ToArray();
        TestComponent[] components = Enumerable.Range(0, depth)
            .Select(index => new TestComponent(
                $"component_{index:D3}",
                content,
                index == 0 ? [] : [$"component_{index - 1:D3}"]))
            .ToArray();
        byte[] payload = CreatePayload(components);

        if (accepted)
            Assert.Equal(depth, (await VerifyPayloadAsync(payload)).Components.Count);
        else
            Assert.Equal("solutions.package.dependencies_invalid", (await VerifyRejectedPayloadAsync(payload)).ProblemCode);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("missing")]
    [InlineData("self")]
    [InlineData("duplicate")]
    [InlineData("unsorted")]
    public async Task Verifier_InvalidDependencyGraph_Rejects(string vector)
    {
        byte[] content = "{\"schemaVersion\":1}"u8.ToArray();
        TestComponent[] components = vector switch
        {
            "cycle" =>
            [
                new("component_a", content, ["component_b"]),
                new("component_b", content, ["component_a"]),
            ],
            "missing" => [new("component_a", content, ["component_missing"])],
            "self" => [new("component_a", content, ["component_a"])],
            "duplicate" =>
            [
                new("component_a", content, []),
                new("component_b", content, ["component_a", "component_a"]),
            ],
            "unsorted" =>
            [
                new("component_a", content, []),
                new("component_b", content, []),
                new("component_c", content, ["component_b", "component_a"]),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(vector)),
        };

        SolutionPackageException exception = await VerifyRejectedPayloadAsync(CreatePayload(components));

        Assert.Equal("solutions.package.dependencies_invalid", exception.ProblemCode);
    }

    [Fact]
    public async Task Verifier_EnvelopeByteBoundary_EnforcesLimit()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload = CreatePayload();
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        SolutionPackageVerifier verifier = new(new KeyReader(key));
        byte[] maximum = CreateSizedEnvelope(payload, signature, SolutionPackageVerifier.MaximumEnvelopeBytes);

        VerifiedSolutionPackage accepted = await verifier.VerifyAsync(
            maximum,
            OpenApiHash,
            TestContext.Current.CancellationToken);
        SolutionPackageException rejected = await Assert.ThrowsAsync<SolutionPackageException>(() =>
            verifier.VerifyAsync(
                CreateSizedEnvelope(payload, signature, SolutionPackageVerifier.MaximumEnvelopeBytes + 1),
                OpenApiHash,
                TestContext.Current.CancellationToken));

        Assert.Equal(payload, accepted.PayloadBytes);
        Assert.Equal("solutions.package.size_invalid", rejected.ProblemCode);
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

    private static byte[] CreatePayload(string buildId = "build-1") =>
        CreatePayload([new TestComponent("reference", "{\"schemaVersion\":1}"u8.ToArray(), [])], buildId);

    private static byte[] CreatePayload(
        IReadOnlyList<TestComponent> components,
        string buildId = "build-1")
    {
        string componentJson = string.Join(
            ',',
            components.Select(component =>
            {
                string hash = Convert.ToHexString(SHA256.HashData(component.Content)).ToLowerInvariant();
                string dependencies = string.Join(
                    ',',
                    component.DependsOn.Select(dependency =>
                        "{\"type\":\"authorization.policy.v1\",\"key\":\"" + dependency + "\"}"));
                return "{\"type\":\"authorization.policy.v1\",\"key\":\"" + component.Key +
                    "\",\"sha256\":\"" + hash + "\",\"content\":\"" +
                    ToBase64(component.Content, urlSafe: true, padded: false) +
                    "\",\"dependsOn\":[" + dependencies + "]}";
            }));
        string json = "{\"schemaVersion\":1,\"solutionKey\":\"reference_application\",\"solutionVersion\":\"0.1.0\",\"axisOpenApiSha256\":\"" + OpenApiHash +
            "\",\"publisher\":{\"publisherId\":\"axis\",\"publisherKeyId\":\"release_key\"},\"provenance\":{\"sourceRevision\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"buildId\":\"" + buildId +
            "\",\"builtAt\":\"2026-08-07T00:00:00Z\",\"sourceUri\":\"https://example.test/reference\"},\"components\":[" + componentJson + "]}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static byte[] CreateEnvelope(byte[] payload, byte[] signature, bool urlSafe, bool padded)
    {
        return CreateEncodedEnvelope(
            ToBase64(payload, urlSafe, padded),
            ToBase64(signature, urlSafe, padded),
            keyId: "ignored-hint");
    }

    private static byte[] CreateEncodedEnvelope(string payload, string signature, string? keyId)
    {
        string keyHint = keyId is null ? string.Empty : "\"keyid\":\"" + keyId + "\",";
        return Encoding.UTF8.GetBytes(
            "{\"payloadType\":\"" + SolutionPackageVerifier.PayloadType + "\",\"payload\":\"" +
            payload + "\",\"signatures\":[{" + keyHint + "\"sig\":\"" + signature + "\"}],\"ignored\":true}");
    }

    private static byte[] CreateSizedEnvelope(byte[] payload, byte[] signature, int size)
    {
        string prefix = "{\"payloadType\":\"" + SolutionPackageVerifier.PayloadType + "\",\"payload\":\"" +
            ToBase64(payload, urlSafe: true, padded: false) +
            "\",\"signatures\":[{\"sig\":\"" + ToBase64(signature, urlSafe: true, padded: false) +
            "\"}],\"padding\":\"";
        const string suffix = "\"}";
        int padding = size - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount(suffix);
        if (padding < 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        return Encoding.UTF8.GetBytes(prefix + new string('a', padding) + suffix);
    }

    private static string ToBase64(byte[] bytes, bool urlSafe, bool padded)
    {
        string value = Convert.ToBase64String(bytes);
        if (urlSafe)
            value = value.Replace('+', '-').Replace('/', '_');
        return padded ? value : value.TrimEnd('=');
    }

    private static byte[] ReplacePayload(byte[] payload, string oldValue, string newValue) =>
        Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(payload).Replace(oldValue, newValue, StringComparison.Ordinal));

    private static async Task<VerifiedSolutionPackage> VerifyPayloadAsync(byte[] payload)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        SolutionPackageVerifier verifier = new(new KeyReader(key));
        return await verifier.VerifyAsync(
            CreateEnvelope(payload, signature, urlSafe: true, padded: false),
            OpenApiHash,
            TestContext.Current.CancellationToken);
    }

    private static async Task<SolutionPackageException> VerifyRejectedPayloadAsync(byte[] payload)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payload),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        SolutionPackageVerifier verifier = new(new KeyReader(key));
        return await Assert.ThrowsAsync<SolutionPackageException>(() =>
            verifier.VerifyAsync(
                CreateEnvelope(payload, signature, urlSafe: true, padded: false),
                OpenApiHash,
                TestContext.Current.CancellationToken));
    }

    private static TestComponent[] CreateWideGraph(int edgeCount)
    {
        const int roots = 128;
        byte[] content = "{\"schemaVersion\":1}"u8.ToArray();
        List<TestComponent> components = Enumerable.Range(0, roots)
            .Select(index => new TestComponent($"component_{index:D3}", content, []))
            .ToList();
        int remaining = edgeCount;
        for (int index = roots; index < 256; index++)
        {
            int current = Math.Min(remaining, roots);
            components.Add(new TestComponent(
                $"component_{index:D3}",
                content,
                Enumerable.Range(0, current).Select(root => $"component_{root:D3}").ToArray()));
            remaining -= current;
        }
        Assert.Equal(0, remaining);
        return components.ToArray();
    }

    private static ConformanceFixture LoadFixture()
    {
        byte[] json = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "solution-package-v1.json"));
        return JsonSerializer.Deserialize<ConformanceFixture>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
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

    private sealed class PemKeyReader(string pem) : ITrustedPublisherKeyReader
    {
        public Task<TrustedPublisherSnapshot?> FindAsync(
            string publisherId,
            string keyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<TrustedPublisherSnapshot?>(new(
                publisherId,
                keyId,
                pem,
                IsActive: true,
                IsTombstone: false,
                ConfigurationRevision: 1));
    }

    private sealed record TestComponent(
        string Key,
        byte[] Content,
        IReadOnlyList<string> DependsOn);

    private sealed record ConformanceFixture(
        int SchemaVersion,
        string AxisOpenApiSha256,
        string PublicKeyPem,
        string EnvelopeBase64,
        string PayloadBase64,
        string PaeBase64,
        string SignatureBase64);
}
