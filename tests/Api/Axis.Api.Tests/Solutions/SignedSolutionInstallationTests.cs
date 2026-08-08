using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Administration;
using Axis.Api.Tests.Helpers;
using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Contracts;
using Axis.Rules.Contracts;
using Axis.Solutions.Application;
using Axis.Solutions.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Api.Tests.Solutions;

[Collection("Api")]
public sealed class SignedSolutionInstallationTests(ApiTestFixture fixture)
{
    private const string PublisherId = "test_publisher";
    private const string PublisherKeyId = "release_key";
    private const string PolicyKey = "reference_application";
    private const string ObjectKey = "loan_application";
    private const string BindingKey =
        "field.required@1:business-object-field:loan_application.amount:record-save";

    [Fact]
    public async Task SignedSolution_WhenInstalledIntoBlankWorkspace_ConfirmsEveryTypedComponentReadback()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string openApiHash;
        using (IServiceScope scope = fixture.CreateScope())
        {
            IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            openApiHash = configuration["Solutions:AxisOpenApiSha256"]!;
            ITrustedPublisherLedger ledger = scope.ServiceProvider.GetRequiredService<ITrustedPublisherLedger>();
            await ledger.ReconcileAsync(
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                [new(PublisherId, PublisherKeyId, signingKey.ExportSubjectPublicKeyInfoPem(), true)],
                TestContext.Current.CancellationToken);
        }

        byte[] envelope = CreateEnvelope(signingKey, openApiHash);
        using HttpRequestMessage publish = new(HttpMethod.Post, "/api/solutions/versions")
        {
            Content = PackageContent(envelope),
        };
        HttpResponseMessage published = await fixture.SendBrowserMutationAsync(
            publish,
            TestContext.Current.CancellationToken);
        published.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement publishBody = await ReadJsonAsync(published);
        Guid solutionVersionId = publishBody.GetProperty("version").GetProperty("id").GetGuid();
        publishBody.GetProperty("version").GetProperty("trustStatus").GetString()
            .Should().Be("Trusted");

        using HttpRequestMessage install = new(
            HttpMethod.Post,
            $"/api/solutions/versions/{solutionVersionId}/installations");
        install.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        HttpResponseMessage started = await fixture.SendBrowserMutationAsync(
            install,
            TestContext.Current.CancellationToken);
        started.StatusCode.Should().Be(HttpStatusCode.Created);
        Guid operationId = (await ReadJsonAsync(started))
            .GetProperty("operation")
            .GetProperty("id")
            .GetGuid();

        JsonElement operation = await WaitForSuccessAsync(operationId);
        operation.GetProperty("status").GetString().Should().Be("Succeeded");
        operation.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("status").GetString())
            .Should().OnlyContain(status => status == "Confirmed");
        operation.GetProperty("steps").EnumerateArray()
            .Select(step => step.GetProperty("key").GetString())
            .Should().Equal(PolicyKey, BindingKey, ObjectKey);

        using IServiceScope readScope = fixture.CreateScope();
        ProductPolicyComponentReadBack? policy = await readScope.ServiceProvider
            .GetRequiredService<IProductPolicyInstaller>()
            .ReadBackAsync(
                administrator.WorkspaceId,
                solutionVersionId,
                TestContext.Current.CancellationToken);
        RuleBindingInstallationReadBack? binding = await readScope.ServiceProvider
            .GetRequiredService<IRuleBindingSolutionInstaller>()
            .ReadBackAsync(
                administrator.WorkspaceId,
                BindingKey,
                TestContext.Current.CancellationToken);
        BusinessObjectDefinitionInstallationReadBack? definition = await readScope.ServiceProvider
            .GetRequiredService<IBusinessObjectDefinitionSolutionInstaller>()
            .ReadBackAsync(
                administrator.WorkspaceId,
                ObjectKey,
                TestContext.Current.CancellationToken);

        policy.Should().NotBeNull();
        policy!.SolutionVersion.Should().Be("1.0.0");
        binding.Should().NotBeNull();
        binding!.Component.TargetId.Should().Be("loan_application.amount");
        binding.BindingRevision.Should().Be(1);
        definition.Should().NotBeNull();
        definition!.Component.ObjectKey.Should().Be(ObjectKey);
        definition.Component.Fields.Single().BindingKeys.Should().Equal(BindingKey);
        definition.SolutionVersionId.Should().Be(solutionVersionId);
        definition.LeaseEpoch.Should().BeGreaterThan(binding.LeaseEpoch);
    }

    [Fact]
    public async Task SignedSolution_WhenPolicyPresentationIsInvalid_RejectsBeforeInstallationMutation()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string suffix = Guid.NewGuid().ToString("N");
        string publisherId = $"publisher_{suffix}";
        string publisherKeyId = $"key_{suffix}";
        string solutionKey = $"invalid_policy_{suffix}";
        string openApiHash;
        using (IServiceScope scope = fixture.CreateScope())
        {
            IConfiguration configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            openApiHash = configuration["Solutions:AxisOpenApiSha256"]!;
            SolutionsDbContext db = scope.ServiceProvider.GetRequiredService<SolutionsDbContext>();
            string spkiSha256 = Convert.ToHexString(
                SHA256.HashData(signingKey.ExportSubjectPublicKeyInfo()))
                .ToLowerInvariant();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO trusted_publisher_keys
                    (id, publisher_id, key_id, spki_sha256, public_key_pem, status,
                     configuration_revision, is_tombstone)
                VALUES
                    ({Guid.NewGuid()}, {publisherId}, {publisherKeyId}, {spkiSha256},
                     {signingKey.ExportSubjectPublicKeyInfoPem()}, {"Active"}, {1L}, {false})
                """, TestContext.Current.CancellationToken);
        }
        const string invalidPolicy =
            """
            {"schemaVersion":1,"policyKey":"invalid_policy","roles":[{"key":"Applicant","presentation":{"vi":{"displayName":"Applicant"}}}],"grants":[{"roleKey":"Applicant","actionKey":"business-object.record.create","resourceType":"business-object.record","resourceKey":"loan_application","scope":"Own"}]}
            """;
        byte[] envelope = CreatePolicyEnvelope(
            signingKey,
            openApiHash,
            publisherId,
            publisherKeyId,
            solutionKey,
            "invalid_policy",
            invalidPolicy);
        using HttpRequestMessage publish = new(HttpMethod.Post, "/api/solutions/versions")
        {
            Content = PackageContent(envelope),
        };
        HttpResponseMessage published = await fixture.SendBrowserMutationAsync(
            publish,
            TestContext.Current.CancellationToken);
        published.StatusCode.Should().Be(HttpStatusCode.Created);
        Guid solutionVersionId = (await ReadJsonAsync(published))
            .GetProperty("version")
            .GetProperty("id")
            .GetGuid();

        using HttpRequestMessage install = new(
            HttpMethod.Post,
            $"/api/solutions/versions/{solutionVersionId}/installations");
        install.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        HttpResponseMessage rejected = await fixture.SendBrowserMutationAsync(
            install,
            TestContext.Current.CancellationToken);

        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(rejected)).GetProperty("code").GetString()
            .Should().Be("authorization.policy_invalid");
        using IServiceScope observerScope = fixture.CreateScope();
        SolutionsDbContext observer = observerScope.ServiceProvider
            .GetRequiredService<SolutionsDbContext>();
        (await observer.SolutionInstallations.AnyAsync(
            value => value.WorkspaceId == administrator.WorkspaceId
                && value.SolutionVersionId == solutionVersionId,
            TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    private async Task<JsonElement> WaitForSuccessAsync(Guid operationId)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            HttpResponseMessage response = await fixture.Client.GetAsync(
                $"/api/solutions/operations/{operationId}",
                TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            JsonElement status = await ReadJsonAsync(response);
            string state = status.GetProperty("status").GetString()!;
            if (state == "Succeeded")
                return status;
            state.Should().NotBe("Failed");
            state.Should().NotBe("Blocked");
            await Task.Delay(
                TimeSpan.FromMilliseconds(100),
                TestContext.Current.CancellationToken);
        }
        throw new TimeoutException("The signed solution installation did not complete.");
    }

    private static byte[] CreateEnvelope(ECDsa key, string openApiHash)
    {
        byte[] policy = Encoding.UTF8.GetBytes(PolicyJson());
        byte[] definition = Encoding.UTF8.GetBytes(
            """{"schemaVersion":1,"objectKey":"loan_application","name":"Loan Application","fields":[{"fieldKey":"amount","label":"Amount","order":0,"fieldType":"Decimal","bindingKeys":["field.required@1:business-object-field:loan_application.amount:record-save"]}]}""");
        byte[] binding = Encoding.UTF8.GetBytes(
            """{"schemaVersion":1,"definitionKey":"field.required","definitionVersion":1,"targetType":"business-object-field","targetId":"loan_application.amount","useCaseOrTrigger":"record-save","inputMappings":{"value":{"kind":"Context","contextKey":"record.value","literalValues":[]}},"priority":0,"enabled":true,"failureBehavior":"FailClosed"}""");

        string payload = "{\"schemaVersion\":1,\"solutionKey\":\"reference_application\",\"solutionVersion\":\"1.0.0\",\"axisOpenApiSha256\":\"" + openApiHash +
            "\",\"publisher\":{\"publisherId\":\"" + PublisherId + "\",\"publisherKeyId\":\"" + PublisherKeyId +
            "\"},\"provenance\":{\"sourceRevision\":\"" + new string('b', 40) +
            "\",\"buildId\":\"acceptance-test\",\"builtAt\":\"2026-08-07T00:00:00Z\",\"sourceUri\":\"https://example.test/reference-product\"},\"components\":[" +
            Component("authorization.policy.v1", PolicyKey, policy, []) + "," +
            Component(
                "business-object.definition.v1",
                ObjectKey,
                definition,
                [("rule.binding.v1", BindingKey)]) + "," +
            Component("rule.binding.v1", BindingKey, binding, []) + "]}";
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payloadBytes),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Encoding.UTF8.GetBytes(
            "{\"payloadType\":\"" + SolutionPackageVerifier.PayloadType + "\",\"payload\":\"" +
            Base64Url(payloadBytes) + "\",\"signatures\":[{\"keyid\":\"" + PublisherKeyId +
            "\",\"sig\":\"" + Base64Url(signature) + "\"}]}");
    }

    private static byte[] CreatePolicyEnvelope(
        ECDsa key,
        string openApiHash,
        string publisherId,
        string publisherKeyId,
        string solutionKey,
        string policyKey,
        string policyJson)
    {
        byte[] policy = Encoding.UTF8.GetBytes(policyJson);
        string payload = "{\"schemaVersion\":1,\"solutionKey\":\"" + solutionKey +
            "\",\"solutionVersion\":\"1.0.0\",\"axisOpenApiSha256\":\"" + openApiHash +
            "\",\"publisher\":{\"publisherId\":\"" + publisherId + "\",\"publisherKeyId\":\"" + publisherKeyId +
            "\"},\"provenance\":{\"sourceRevision\":\"" + new string('c', 40) +
            "\",\"buildId\":\"invalid-policy-test\",\"builtAt\":\"2026-08-07T00:00:00Z\",\"sourceUri\":\"https://example.test/invalid-policy\"},\"components\":[" +
            Component("authorization.policy.v1", policyKey, policy, []) + "]}";
        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
        byte[] signature = key.SignData(
            SolutionPackageVerifier.CreatePae(SolutionPackageVerifier.PayloadType, payloadBytes),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Encoding.UTF8.GetBytes(
            "{\"payloadType\":\"" + SolutionPackageVerifier.PayloadType + "\",\"payload\":\"" +
            Base64Url(payloadBytes) + "\",\"signatures\":[{\"keyid\":\"" + publisherKeyId +
            "\",\"sig\":\"" + Base64Url(signature) + "\"}]}");
    }

    private static string Component(
        string type,
        string key,
        byte[] content,
        IReadOnlyList<(string Type, string Key)> dependencies)
    {
        string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        string dependsOn = string.Join(
            ',',
            dependencies.Select(dependency =>
                "{\"type\":\"" + dependency.Type + "\",\"key\":\"" + dependency.Key + "\"}"));
        return "{\"type\":\"" + type + "\",\"key\":\"" + key + "\",\"sha256\":\"" + hash +
            "\",\"content\":\"" + Base64Url(content) + "\",\"dependsOn\":[" + dependsOn + "]}";
    }

    private static string PolicyJson() =>
        """
        {"schemaVersion":1,"policyKey":"reference_application","roles":[{"key":"Administrator","presentation":{"en":{"displayName":"Administrator"}}},{"key":"Applicant","presentation":{"en":{"displayName":"Applicant"}}},{"key":"Caseworker","presentation":{"en":{"displayName":"Caseworker"}}}],"grants":[{"roleKey":"Administrator","actionKey":"business-object.definition.manage","resourceType":"business-object.definition","resourceKey":"loan_application","scope":"None"},{"roleKey":"Administrator","actionKey":"business-object.definition.read","resourceType":"business-object.definition","resourceKey":"loan_application","scope":"None"},{"roleKey":"Administrator","actionKey":"business-object.definition.read-published","resourceType":"business-object.definition","resourceKey":"loan_application","scope":"None"},{"roleKey":"Administrator","actionKey":"business-object.record.create","resourceType":"business-object.record","resourceKey":"loan_application","scope":"All"},{"roleKey":"Administrator","actionKey":"business-object.record.list","resourceType":"business-object.record","resourceKey":"loan_application","scope":"All"},{"roleKey":"Administrator","actionKey":"business-object.record.read","resourceType":"business-object.record","resourceKey":"loan_application","scope":"All"},{"roleKey":"Administrator","actionKey":"business-object.record.save","resourceType":"business-object.record","resourceKey":"loan_application","scope":"All"},{"roleKey":"Administrator","actionKey":"business-object.record.submit","resourceType":"business-object.record","resourceKey":"loan_application","scope":"All"},{"roleKey":"Applicant","actionKey":"business-object.definition.read-published","resourceType":"business-object.definition","resourceKey":"loan_application","scope":"None"},{"roleKey":"Applicant","actionKey":"business-object.record.create","resourceType":"business-object.record","resourceKey":"loan_application","scope":"Own"},{"roleKey":"Applicant","actionKey":"business-object.record.read","resourceType":"business-object.record","resourceKey":"loan_application","scope":"Own"},{"roleKey":"Applicant","actionKey":"business-object.record.save","resourceType":"business-object.record","resourceKey":"loan_application","scope":"Own"},{"roleKey":"Applicant","actionKey":"business-object.record.submit","resourceType":"business-object.record","resourceKey":"loan_application","scope":"Own"},{"roleKey":"Caseworker","actionKey":"business-object.definition.read-published","resourceType":"business-object.definition","resourceKey":"loan_application","scope":"None"},{"roleKey":"Caseworker","actionKey":"business-object.record.list","resourceType":"business-object.record","resourceKey":"loan_application","scope":"All"},{"roleKey":"Caseworker","actionKey":"business-object.record.read","resourceType":"business-object.record","resourceKey":"loan_application","scope":"All"}]}
        """;

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static ByteArrayContent PackageContent(byte[] bytes)
    {
        ByteArrayContent content = new(bytes);
        content.Headers.ContentType = new("application/vnd.dsse.envelope.v1+json");
        return content;
    }

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>(
            ApiTestFixture.JsonOptions,
            TestContext.Current.CancellationToken);
}
