using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Audit.Contracts;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence.Entities;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Services;
using Axis.Identity.Infrastructure.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace Axis.Identity.Infrastructure.Tests.Services;

[Collection("IdentityDb")]
public sealed class ServiceClientAssertionAuthenticationTests(IdentityDatabaseFixture database)
{
    private const string TokenEndpoint = "https://axis.example/connect/token";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-07T00:00:00Z");

    [Fact]
    public async Task Assertion_ValidEs256_SucceedsAndStoresDigest()
    {
        Seed seed = await SeedAsync();
        string assertion = CreateAssertion(seed, "jti-valid", Now);

        ServiceAssertionAuthenticationResult? result = await AuthenticateAsync(seed.ClientId, assertion);

        Assert.NotNull(result);
        Assert.Equal(seed.IdentityId, result.ServiceIdentityId);
        Assert.Equal(seed.WorkspaceId, result.WorkspaceId);
        await using IdentityDbContext observer = database.CreateContext();
        string digest = Digest(seed.IdentityId, "jti-valid");
        Assert.True(await observer.ServiceAssertionReplayRecords.AnyAsync(x => x.Digest == digest, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("jti-valid", digest, StringComparison.Ordinal);
        IdentityAuditOutboxRecord audit = await observer.IdentityAuditOutboxRecords.SingleAsync(
            value => value.TargetId == seed.IdentityId && value.Outcome == "authenticated",
            TestContext.Current.CancellationToken);
        Assert.Equal("identity.service_authentication", audit.Action);
        Assert.DoesNotContain("jti-valid", audit.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain(assertion, audit.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assertion_ReplayedJti_AllowsOnlyOneConcurrentSuccess()
    {
        Seed seed = await SeedAsync();
        string assertion = CreateAssertion(seed, "jti-race", Now);
        Task<ServiceAssertionAuthenticationResult?> first = Task.Run(() => AuthenticateAsync(seed.ClientId, assertion));
        Task<ServiceAssertionAuthenticationResult?> second = Task.Run(() => AuthenticateAsync(seed.ClientId, assertion));

        ServiceAssertionAuthenticationResult?[] results = await Task.WhenAll(first, second);

        Assert.Single(results, x => x is not null);
        await using IdentityDbContext observer = database.CreateContext();
        Assert.Equal(1, await observer.ServiceAssertionReplayRecords.CountAsync(x => x.Digest == Digest(seed.IdentityId, "jti-race"), TestContext.Current.CancellationToken));
        Assert.Equal(1, await observer.IdentityAuditOutboxRecords.CountAsync(
            value => value.TargetId == seed.IdentityId && value.Outcome == "authenticated",
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await observer.IdentityAuditOutboxRecords.CountAsync(
            value => value.TargetId == seed.IdentityId && value.Outcome == "replay_denied",
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("alg")]
    [InlineData("kid")]
    [InlineData("signature")]
    [InlineData("iss")]
    [InlineData("sub")]
    [InlineData("aud")]
    [InlineData("iat")]
    [InlineData("exp")]
    [InlineData("jti")]
    [InlineData("nbf")]
    [InlineData("nbf-before-iat")]
    [InlineData("lifetime")]
    [InlineData("future")]
    [InlineData("expired")]
    [InlineData("stale")]
    [InlineData("inverted")]
    [InlineData("multiple-audiences")]
    public async Task Assertion_InvalidBoundary_Denies(string caseName)
    {
        Seed seed = await SeedAsync();
        string assertion = caseName switch
        {
            "malformed" => "not.a.jwt",
            "alg" => CreateAssertion(seed, "jti-alg", Now, alg: "RS256"),
            "kid" => CreateAssertion(seed, "jti-kid", Now, kid: "missing"),
            "signature" => CreateAssertion(seed, "jti-signature", Now, tamperSignature: true),
            "iss" => CreateAssertion(seed, "jti-iss", Now, iss: "other"),
            "sub" => CreateAssertion(seed, "jti-sub", Now, sub: "other"),
            "aud" => CreateAssertion(seed, "jti-aud", Now, audience: "https://other.example/token"),
            "iat" => CreateAssertion(seed, "jti-iat", Now, omitIat: true),
            "exp" => CreateAssertion(seed, "jti-exp", Now, omitExp: true),
            "jti" => CreateAssertion(seed, "jti-missing", Now, omitJti: true),
            "nbf" => CreateAssertion(seed, "jti-nbf", Now, nbf: Now.AddSeconds(31)),
            "nbf-before-iat" => CreateAssertion(seed, "jti-nbf-before", Now, nbf: Now.AddSeconds(-1)),
            "lifetime" => CreateAssertion(seed, "jti-lifetime", Now, exp: Now.AddMinutes(5).AddSeconds(1)),
            "future" => CreateAssertion(seed, "jti-future", Now, iat: Now.AddSeconds(31)),
            "expired" => CreateAssertion(seed, "jti-expired", Now, iat: Now.AddMinutes(-4), exp: Now.AddSeconds(-31)),
            "stale" => CreateAssertion(seed, "jti-stale", Now, iat: Now.AddMinutes(-5).AddSeconds(-31), exp: Now.AddSeconds(-31)),
            "inverted" => CreateAssertion(seed, "jti-inverted", Now, exp: Now.AddSeconds(-1)),
            "multiple-audiences" => CreateAssertion(seed, "jti-audiences", Now, audiences: [TokenEndpoint, "https://other.example/token"]),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };

        Assert.Null(await AuthenticateAsync(seed.ClientId, assertion));
        await using IdentityDbContext observer = database.CreateContext();
        Assert.True(await observer.IdentityAuditOutboxRecords.AnyAsync(
            value => value.TargetId == seed.IdentityId
                && value.Outcome == "assertion_denied"
                && value.MetadataJson == "{}",
            TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("future-iat")]
    [InlineData("expired")]
    [InlineData("future-nbf")]
    public async Task Assertion_ExactClockSkewBoundary_Succeeds(string caseName)
    {
        Seed seed = await SeedAsync();
        string assertion = caseName switch
        {
            "future-iat" => CreateAssertion(
                seed,
                "jti-future-boundary",
                Now,
                iat: Now.AddSeconds(30),
                exp: Now.AddMinutes(5).AddSeconds(30)),
            "expired" => CreateAssertion(
                seed,
                "jti-expired-boundary",
                Now,
                iat: Now.AddMinutes(-4),
                exp: Now.AddSeconds(-30)),
            "future-nbf" => CreateAssertion(
                seed,
                "jti-nbf-boundary",
                Now,
                nbf: Now.AddSeconds(30)),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName)),
        };

        Assert.NotNull(await AuthenticateAsync(seed.ClientId, assertion));
    }

    [Fact]
    public async Task Assertion_ReplayDigest_PurgesAtExpiryPlusClockSkew()
    {
        Seed seed = await SeedAsync();
        const string jti = "jti-purge";
        Assert.NotNull(await AuthenticateAsync(
            seed.ClientId,
            CreateAssertion(seed, jti, Now, exp: Now.AddMinutes(1))));

        await using IdentityDbContext context = database.CreateContext();
        ServiceAssertionReplayStore store = new(
            context,
            new IdentityAuditOutbox(context),
            new FixedClock(Now));
        string digest = Digest(seed.IdentityId, jti);
        await store.PurgeExpiredAsync(
            Now.AddMinutes(1).AddSeconds(29).UtcDateTime,
            TestContext.Current.CancellationToken);
        Assert.True(await context.ServiceAssertionReplayRecords.AnyAsync(
            value => value.Digest == digest,
            TestContext.Current.CancellationToken));

        await store.PurgeExpiredAsync(
            Now.AddMinutes(1).AddSeconds(30).UtcDateTime,
            TestContext.Current.CancellationToken);
        Assert.False(await context.ServiceAssertionReplayRecords.AnyAsync(
            value => value.Digest == digest,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Assertion_RevokedAuthority_Denies()
    {
        Seed seed = await SeedAsync(revokeKey: true);
        Assert.Null(await AuthenticateAsync(seed.ClientId, CreateAssertion(seed, "jti-revoked", Now)));
    }

    [Fact]
    public async Task Assertion_RevokedIdentity_Denies()
    {
        Seed seed = await SeedAsync(revokeIdentity: true);
        Assert.Null(await AuthenticateAsync(seed.ClientId, CreateAssertion(seed, "jti-identity-revoked", Now)));
    }

    [Fact]
    public async Task Assertion_ReplayStoreFailure_FailsClosed()
    {
        Seed seed = await SeedAsync();
        await using IdentityDbContext context = database.CreateContext();
        ServiceClientAssertionAuthentication service = new(new ServiceIdentityRepository(context), new ThrowingReplayStore(), new FixedClock(Now));

        Assert.Null(await service.AuthenticateAsync(
            new(seed.ClientId, CreateAssertion(seed, "jti-store", Now), TokenEndpoint),
            TestContext.Current.CancellationToken));
    }

    private async Task<ServiceAssertionAuthenticationResult?> AuthenticateAsync(string clientId, string assertion)
    {
        await using IdentityDbContext context = database.CreateContext();
        FixedClock clock = new(Now);
        ServiceClientAssertionAuthentication service = new(
            new ServiceIdentityRepository(context),
            new ServiceAssertionReplayStore(context, new IdentityAuditOutbox(context), clock),
            clock);
        return await service.AuthenticateAsync(new(clientId, assertion, TokenEndpoint), TestContext.Current.CancellationToken);
    }

    private async Task<Seed> SeedAsync(bool revokeKey = false, bool revokeIdentity = false)
    {
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = signingKey.ExportParameters(true);
        string clientId = $"svc-{Guid.NewGuid():N}";
        Workspace workspace = Workspace.CreatePersonal("Service Test", WorkspaceSlug.Create($"svc-{Guid.NewGuid():N}").Value);
        ServiceIdentity identity = ServiceIdentity.Create(workspace.Id, clientId, Now.UtcDateTime);
        string x = Base64Url(parameters.Q.X!); string y = Base64Url(parameters.Q.Y!);
        identity.AddKey("key-1", Convert.ToHexString(SHA256.HashData(signingKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant(), x, y, identity.Revision, Now.UtcDateTime);
        if (revokeKey) identity.RevokeKey(identity.Keys.Single().Id, identity.Revision, Now.UtcDateTime);
        if (revokeIdentity) identity.Revoke(identity.Revision, Now.UtcDateTime);
        await using IdentityDbContext context = database.CreateContext();
        context.Workspaces.Add(workspace); context.ServiceIdentities.Add(identity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new(identity.Id, workspace.Id, clientId, parameters, "key-1");
    }

    private static string CreateAssertion(Seed seed, string jti, DateTimeOffset now, string? alg = null, string? kid = null, string? iss = null, string? sub = null, string? audience = null, IReadOnlyList<string>? audiences = null, DateTimeOffset? iat = null, DateTimeOffset? exp = null, DateTimeOffset? nbf = null, bool omitIat = false, bool omitExp = false, bool omitJti = false, bool tamperSignature = false)
    {
        Dictionary<string, object?> payload = new()
        {
            ["iss"] = iss ?? seed.ClientId,
            ["sub"] = sub ?? seed.ClientId,
            ["aud"] = audiences ?? (object)(audience ?? TokenEndpoint),
        };
        if (!omitJti) payload["jti"] = jti;
        if (!omitIat) payload["iat"] = (iat ?? now).ToUnixTimeSeconds();
        if (!omitExp) payload["exp"] = (exp ?? now.AddMinutes(5)).ToUnixTimeSeconds();
        if (nbf is not null) payload["nbf"] = nbf.Value.ToUnixTimeSeconds();
        string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = alg ?? "ES256", kid = kid ?? seed.Kid }));
        string body = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        using ECDsa key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = seed.Parameters.D, Q = seed.Parameters.Q });
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{body}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (tamperSignature) signature[0] ^= 1;
        return $"{header}.{body}.{Base64Url(signature)}";
    }

    private static string Digest(Guid identityId, string jti) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes($"{identityId:N}:{jti}")));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private sealed record Seed(Guid IdentityId, Guid WorkspaceId, string ClientId, ECParameters Parameters, string Kid);
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class ThrowingReplayStore : IServiceAssertionReplayStore
    {
        public Task<bool> TryAcceptAsync(string digest, DateTime expiresAt, AuditEventV1 successAudit, AuditEventV1 replayAudit, CancellationToken ct = default) =>
            Task.FromException<bool>(new InvalidOperationException("replay unavailable"));
        public Task RecordAuditAsync(AuditEventV1 auditEvent, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("audit unavailable"));
        public Task PurgeExpiredAsync(DateTime now, CancellationToken ct = default) => Task.CompletedTask;
    }
}
