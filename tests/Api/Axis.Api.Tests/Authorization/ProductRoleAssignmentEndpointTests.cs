using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Administration;
using Axis.Api.Tests.Helpers;
using Axis.Authorization.Contracts;
using Axis.Authorization.Infrastructure.Persistence;
using Axis.BusinessObjects.Contracts;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.BusinessObjects.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Aggregates;
using Axis.Identity.Domain.ValueObjects;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Rules.Contracts;
using Axis.Rules.Infrastructure.Persistence;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using DomainDefinitionKey = Axis.BusinessObjects.Domain.ValueObjects.BusinessObjectDefinitionKey;
using DomainDefinitionVersionId = Axis.BusinessObjects.Domain.ValueObjects.BusinessObjectDefinitionVersionId;
using DomainSubjectReference = Axis.BusinessObjects.Domain.ValueObjects.SubjectReference;

namespace Axis.Api.Tests.Authorization;

[Collection("Api")]
public sealed class ProductRoleAssignmentEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task ProductRoleAssignmentEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        using HttpClient anonymous = fixture.CreateAnonymousClient();
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/product-role-assignments/assign")
        {
            Content = JsonContent.Create(AssignmentBody(Guid.NewGuid(), Guid.NewGuid(), "caseworker"), options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");

        (await anonymous.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AssignmentLifecycle_WhenAdministrator_RoundTripsCanonicalStateAndHidesUnavailableTargets()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        Guid policyVersionId = Guid.NewGuid();
        await SeedPolicyAsync(administrator.WorkspaceId, policyVersionId);

        string assignmentKey = Guid.NewGuid().ToString("N");
        string assignmentCorrelation = $"assignment-{Guid.NewGuid():N}";
        HttpResponseMessage assigned = await SendMutationAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(administrator.UserId, policyVersionId, "caseworker"),
            assignmentKey,
            assignmentCorrelation);
        HttpResponseMessage retry = await SendMutationAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(administrator.UserId, policyVersionId, "caseworker"),
            assignmentKey);

        string assignedPayload = await assigned.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);
        assigned.StatusCode.Should().Be(HttpStatusCode.OK, assignedPayload);
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement assignedBody = await ReadJsonAsync(assigned);
        JsonElement retryBody = await ReadJsonAsync(retry);
        assignedBody.GetProperty("workspaceId").GetGuid().Should().Be(administrator.WorkspaceId);
        JsonElement subject = assignedBody.GetProperty("subject");
        subject.GetProperty("kind").GetString().Should().Be("Human");
        subject.GetProperty("subjectId").GetGuid().Should().Be(administrator.UserId);
        subject.TryGetProperty("id", out _).Should().BeFalse();
        assignedBody.TryGetProperty("userId", out _).Should().BeFalse();
        assignedBody.GetProperty("roleKey").GetString().Should().Be("caseworker");
        assignedBody.GetProperty("isActive").GetBoolean().Should().BeTrue();
        retryBody.GetProperty("revision").GetInt32().Should().Be(1);

        HttpResponseMessage changedContent = await SendMutationAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(administrator.UserId, policyVersionId, "applicant"),
            assignmentKey);
        changedContent.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadJsonAsync(changedContent)).GetProperty("code").GetString()
            .Should().Be("authorization.assignment.idempotency_conflict");

        HttpResponseMessage revoked = await SendMutationAsync(
            "/api/product-role-assignments/revoke",
            AssignmentBody(administrator.UserId, policyVersionId, "caseworker", 1),
            Guid.NewGuid().ToString("N"));
        revoked.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement revokedBody = await ReadJsonAsync(revoked);
        revokedBody.GetProperty("isActive").GetBoolean().Should().BeFalse();
        revokedBody.GetProperty("revision").GetInt32().Should().Be(2);

        HttpResponseMessage management = await fixture.Client.GetAsync(
            "/api/product-role-assignments?language=vi",
            TestContext.Current.CancellationToken);
        management.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement managementBody = await ReadJsonAsync(management);
        managementBody.GetProperty("subjects").EnumerateArray()
            .Should().Contain(value =>
                value.GetProperty("subject").GetProperty("kind").GetString() == "Human" &&
                value.GetProperty("subject").GetProperty("subjectId").GetGuid() == administrator.UserId);
        managementBody.GetProperty("roles").EnumerateArray()
            .Should().Contain(value =>
                value.GetProperty("roleKey").GetString() == "caseworker" &&
                value.GetProperty("displayName").GetString() == "Chuyên viên");
        managementBody.GetProperty("assignments").EnumerateArray()
            .Should().Contain(value =>
                value.GetProperty("roleKey").GetString() == "caseworker" &&
                !value.GetProperty("isActive").GetBoolean() &&
                value.GetProperty("revision").GetInt32() == 2);

        Guid foreignUserId = await SeedForeignWorkspaceSubjectAsync();
        HttpResponseMessage unavailableTarget = await SendMutationAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(foreignUserId, policyVersionId, "caseworker"),
            Guid.NewGuid().ToString("N"));
        unavailableTarget.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonElement unavailableProblem = await ReadJsonAsync(unavailableTarget);
        unavailableProblem.GetProperty("code").GetString()
            .Should().Be("authorization.assignment.unavailable");
        unavailableProblem.GetRawText().Contains(
            foreignUserId.ToString(),
            StringComparison.OrdinalIgnoreCase).Should().BeFalse();

        using IServiceScope scope = fixture.CreateScope();
        AuthorizationDbContext db = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        ProductRoleAssignmentRow persisted = await db.Assignments.SingleAsync(
            value => value.WorkspaceId == administrator.WorkspaceId
                && value.SubjectId == administrator.UserId
                && value.PolicyVersionId == policyVersionId
                && value.RoleKey == "caseworker",
            TestContext.Current.CancellationToken);
        persisted.IsActive.Should().BeFalse();
        persisted.Revision.Should().Be(2);
        (await db.AuditOutbox.CountAsync(
            TestContext.Current.CancellationToken)).Should().BeGreaterThanOrEqualTo(4);
        Guid assignedAuditEventId = await db.IdempotencyRecords
            .Where(value => value.WorkspaceId == administrator.WorkspaceId
                && value.IdempotencyKey == assignmentKey)
            .Select(value => value.AuditEventId)
            .SingleAsync(TestContext.Current.CancellationToken);
        string assignedAudit = await db.AuditOutbox
            .Where(value => value.Id == assignedAuditEventId)
            .Select(value => value.Payload)
            .SingleAsync(TestContext.Current.CancellationToken);
        JsonElement assignedAuditEnvelope = JsonSerializer.Deserialize<JsonElement>(assignedAudit);
        assignedAuditEnvelope.GetProperty("CorrelationId").GetString()
            .Should().Be(assignmentCorrelation);
        assignedAuditEnvelope.GetProperty("CorrelationId").GetString()
            .Should().NotBe(assignmentKey);
    }

    [Fact]
    public async Task AssignProductRole_WhenIdempotencyKeyMissing_ReturnsStableBadRequest()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        Guid policyVersionId = Guid.NewGuid();
        await SeedPolicyAsync(administrator.WorkspaceId, policyVersionId);

        HttpResponseMessage response = await fixture.PostBrowserJsonAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(administrator.UserId, policyVersionId, "caseworker"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("code").GetString()
            .Should().Be("authorization.assignment.invalid");
    }

    [Fact]
    public async Task AssignProductRole_WhenRetiredFlatSubjectShapeIsSent_ReturnsStableBadRequest()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        Guid policyVersionId = Guid.NewGuid();
        await SeedPolicyAsync(administrator.WorkspaceId, policyVersionId);

        HttpResponseMessage response = await SendMutationAsync(
            "/api/product-role-assignments/assign",
            new
            {
                subjectKind = "Human",
                subjectId = administrator.UserId,
                policyVersionId,
                roleKey = "caseworker",
            },
            Guid.NewGuid().ToString("N"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(response)).GetProperty("code").GetString()
            .Should().Be("authorization.assignment.invalid");
    }

    [Fact]
    public async Task RoleEnforcement_WhenAssignmentsChange_AppliesReferenceOutcomes()
    {
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator =
            await WorkspaceAdministratorApiTestSession.CreateAdministratorAsync(fixture);
        ReferenceProductSeed seed = await InstallReferenceProductAsync(administrator);
        Guid foreignOwnedRecordId = await SeedForeignOwnedRecordAsync(administrator.WorkspaceId, seed.PublishedVersionId);

        (await SendMutationAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(administrator.UserId, seed.PolicyVersionId, "Applicant"),
            Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage applicantDefinition = await fixture.Client.GetAsync(
            $"/api/business-object-definitions/{seed.DefinitionId:D}",
            TestContext.Current.CancellationToken);
        applicantDefinition.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement applicantRecord = await CreateRecordAsync("applicant-record");
        Guid applicantRecordId = applicantRecord.GetProperty("id").GetGuid();
        (await fixture.Client.GetAsync(
            $"/api/business-object-records/{applicantRecordId:D}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.Client.GetAsync(
            $"/api/business-object-records/{foreignOwnedRecordId:D}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        HttpResponseMessage applicantSave = await SendBrowserPutAsync(
            $"/api/business-object-records/{applicantRecordId}",
            new { expectedRevision = 1, values = new { display_name = new[] { "Applicant" } } });
        applicantSave.StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage applicantSubmit = await fixture.PostBrowserJsonAsync(
            $"/api/business-object-records/{applicantRecordId:D}/submit",
            new { expectedRevision = 2 },
            TestContext.Current.CancellationToken);
        applicantSubmit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(applicantSubmit)).GetProperty("isSubmitted").GetBoolean().Should().BeTrue();
        (await fixture.Client.GetAsync(
            "/api/business-object-records?page=1&pageSize=20&objectKey=loan_application",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await SendMutationAsync(
            "/api/product-role-assignments/revoke",
            AssignmentBody(administrator.UserId, seed.PolicyVersionId, "Applicant", 1),
            Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendMutationAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(administrator.UserId, seed.PolicyVersionId, "Caseworker"),
            Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await fixture.Client.GetAsync(
            $"/api/business-object-definitions/{seed.DefinitionId:D}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage caseworkerList = await fixture.Client.GetAsync(
            "/api/business-object-records?page=1&pageSize=20&objectKey=loan_application",
            TestContext.Current.CancellationToken);
        caseworkerList.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(caseworkerList)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain([foreignOwnedRecordId, applicantRecordId]);
        (await fixture.Client.GetAsync(
            $"/api/business-object-records/{foreignOwnedRecordId:D}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage caseworkerSave = await SendBrowserPutAsync(
            $"/api/business-object-records/{applicantRecordId}",
            new { expectedRevision = 3, values = new { display_name = new[] { "Forbidden" } } });
        caseworkerSave.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.PostBrowserJsonAsync(
            "/api/business-object-records/loan_application",
            new { idempotencyKey = "caseworker-forbidden", values = new { display_name = new[] { "Forbidden" } } },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await SendMutationAsync(
            "/api/product-role-assignments/revoke",
            AssignmentBody(administrator.UserId, seed.PolicyVersionId, "Caseworker", 1),
            Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendMutationAsync(
            "/api/product-role-assignments/assign",
            AssignmentBody(administrator.UserId, seed.PolicyVersionId, "Administrator"),
            Guid.NewGuid().ToString("N"))).StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage administratorDefinition = await fixture.Client.GetAsync(
            $"/api/business-object-definitions/{seed.DefinitionId:D}",
            TestContext.Current.CancellationToken);
        administratorDefinition.StatusCode.Should().Be(HttpStatusCode.OK);
        int definitionRevision = (await ReadJsonAsync(administratorDefinition)).GetProperty("revision").GetInt32();
        (await fixture.PostBrowserJsonAsync(
            $"/api/business-object-definitions/{seed.DefinitionId:D}/publish",
            new { expectedRevision = definitionRevision },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await fixture.Client.GetAsync(
            $"/api/rules/{RuleDefinitionKeys.Required}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserJsonAsync(
            $"/api/rules/{RuleDefinitionKeys.Required}/versions",
            new { expectedRevision = 1 },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        int bindingCount = await CountRuleBindingsAsync(administrator.WorkspaceId);
        HttpResponseMessage ungrantedBinding = await fixture.PostBrowserJsonAsync(
            "/api/rule-bindings",
            RuleBindingBody(RuleDefinitionKeys.TextLength, "ungranted-binding"),
            TestContext.Current.CancellationToken);
        ungrantedBinding.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CountRuleBindingsAsync(administrator.WorkspaceId)).Should().Be(bindingCount);

        HttpResponseMessage createdBindingResponse = await fixture.PostBrowserJsonAsync(
            "/api/rule-bindings",
            RuleBindingBody(RuleDefinitionKeys.Required, "authorized-binding"),
            TestContext.Current.CancellationToken);
        createdBindingResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement createdBinding = await ReadJsonAsync(createdBindingResponse);
        Guid bindingId = createdBinding.GetProperty("id").GetGuid();
        (await fixture.Client.GetAsync(
            $"/api/rule-bindings/{bindingId:D}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage updatedBindingResponse = await SendBrowserPutAsync(
            $"/api/rule-bindings/{bindingId:D}",
            RuleBindingBody(RuleDefinitionKeys.Required, "authorized-binding-updated", expectedRevision: 1));
        updatedBindingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage bindingUsageResponse = await fixture.Client.GetAsync(
            $"/api/rules/{RuleDefinitionKeys.Required}/bindings?version=1",
            TestContext.Current.CancellationToken);
        bindingUsageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(bindingUsageResponse)).EnumerateArray()
            .Select(item => item.GetProperty("bindingId").GetGuid())
            .Should().Contain(bindingId);
        (await SendBrowserDeleteAsync(
            $"/api/rule-bindings/{bindingId:D}",
            new { expectedRevision = 2 })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage administratorList = await fixture.Client.GetAsync(
            "/api/business-object-records?page=1&pageSize=20&objectKey=loan_application",
            TestContext.Current.CancellationToken);
        administratorList.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(administratorList)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain([foreignOwnedRecordId, applicantRecordId]);
        (await fixture.Client.GetAsync(
            $"/api/business-object-records/{foreignOwnedRecordId:D}",
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.PostBrowserJsonAsync(
            "/api/business-object-records/loan_application",
            new { idempotencyKey = "administrator-forbidden", values = new { display_name = new[] { "Forbidden" } } },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendBrowserPutAsync(
            $"/api/business-object-records/{applicantRecordId:D}",
            new { expectedRevision = 3, values = new { display_name = new[] { "Forbidden" } } })).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
        (await fixture.PostBrowserJsonAsync(
            $"/api/business-object-records/{applicantRecordId:D}/submit",
            new { expectedRevision = 3 },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<ReferenceProductSeed> InstallReferenceProductAsync(
        WorkspaceAdministratorApiTestSession.AdministratorContext administrator)
    {
        Guid solutionVersionId = Guid.NewGuid();
        Guid operationId = Guid.NewGuid();
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        using IServiceScope scope = fixture.CreateScope();
        IBusinessObjectDefinitionSolutionInstaller definitions = scope.ServiceProvider
            .GetRequiredService<IBusinessObjectDefinitionSolutionInstaller>();
        BusinessObjectDefinitionInstallationResult definitionResult = await definitions.InstallAsync(
            administrator.WorkspaceId,
            new BusinessObjectDefinitionSolutionComponent(
                "loan_application",
                "loan_application",
                "Loan Application",
                [new("display_name", "Display name", 0, BusinessObjectSolutionFieldType.Text, null, [])]),
            new BusinessObjectDefinitionInstallationReceipt(
                solutionVersionId,
                SubjectReference.Human(administrator.UserId),
                hash,
                operationId,
                Guid.NewGuid(),
                1),
            TestContext.Current.CancellationToken);
        definitionResult.IsSuccess.Should().BeTrue(definitionResult.ProblemCode);

        IProductPolicyInstaller policies = scope.ServiceProvider
            .GetRequiredService<IProductPolicyInstaller>();
        ProductPolicyComponent policy = new(
            "reference_application",
            solutionVersionId,
            [
                Role("Administrator"),
                Role("Applicant"),
                Role("Caseworker"),
            ],
            [
                Grant("Administrator", "business-object.definition.manage", "business-object.definition", "loan_application"),
                Grant("Administrator", "business-object.definition.read", "business-object.definition", "loan_application"),
                Grant("Administrator", "business-object.record.list", "business-object.record", "loan_application", ProductActionScope.All),
                Grant("Administrator", "business-object.record.read", "business-object.record", "loan_application", ProductActionScope.All),
                Grant("Administrator", "rule.binding.manage", "rule.binding", RuleDefinitionKeys.NumericRange),
                Grant("Administrator", "rule.binding.manage", "rule.binding", RuleDefinitionKeys.Required),
                Grant("Administrator", "rule.binding.manage", "rule.binding", RuleDefinitionKeys.TextFormat),
                Grant("Administrator", "rule.binding.read", "rule.binding", RuleDefinitionKeys.NumericRange),
                Grant("Administrator", "rule.binding.read", "rule.binding", RuleDefinitionKeys.Required),
                Grant("Administrator", "rule.binding.read", "rule.binding", RuleDefinitionKeys.TextFormat),
                Grant("Administrator", "rule.definition.manage", "rule.definition", RuleDefinitionKeys.NumericRange),
                Grant("Administrator", "rule.definition.manage", "rule.definition", RuleDefinitionKeys.Required),
                Grant("Administrator", "rule.definition.manage", "rule.definition", RuleDefinitionKeys.TextFormat),
                Grant("Administrator", "rule.definition.read", "rule.definition", RuleDefinitionKeys.NumericRange),
                Grant("Administrator", "rule.definition.read", "rule.definition", RuleDefinitionKeys.Required),
                Grant("Administrator", "rule.definition.read", "rule.definition", RuleDefinitionKeys.TextFormat),
                Grant("Applicant", "business-object.definition.read-published", "business-object.definition", "loan_application"),
                Grant("Applicant", "business-object.record.create", "business-object.record", "loan_application", ProductActionScope.Own),
                Grant("Applicant", "business-object.record.read", "business-object.record", "loan_application", ProductActionScope.Own),
                Grant("Applicant", "business-object.record.save", "business-object.record", "loan_application", ProductActionScope.Own),
                Grant("Applicant", "business-object.record.submit", "business-object.record", "loan_application", ProductActionScope.Own),
                Grant("Caseworker", "business-object.definition.read-published", "business-object.definition", "loan_application"),
                Grant("Caseworker", "business-object.record.list", "business-object.record", "loan_application", ProductActionScope.All),
                Grant("Caseworker", "business-object.record.read", "business-object.record", "loan_application", ProductActionScope.All),
            ]);
        ProductPolicyInstallResult policyResult = await policies.InstallAsync(
            new InstallProductPolicyRequest(
                administrator.WorkspaceId,
                policy,
                "1.0.0",
                hash,
                operationId.ToString("D"),
                Guid.NewGuid().ToString("D"),
                2,
                SubjectReference.Human(administrator.UserId),
                "seed-policy"),
            TestContext.Current.CancellationToken);
        policyResult.IsInstalled.Should().BeTrue(policyResult.Error);
        BusinessObjectDefinitionInstallationReadBack? readBack = await definitions.ReadBackAsync(
            administrator.WorkspaceId,
            "loan_application",
            TestContext.Current.CancellationToken);
        readBack.Should().NotBeNull();
        return new ReferenceProductSeed(
            solutionVersionId,
            readBack!.DefinitionId,
            readBack.PublishedVersionId);
    }

    private async Task<JsonElement> CreateRecordAsync(string idempotencyKey)
    {
        HttpResponseMessage response = await fixture.PostBrowserJsonAsync(
            "/api/business-object-records/loan_application",
            new { idempotencyKey, values = new { display_name = new[] { "Draft" } } },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await ReadJsonAsync(response);
    }

    private async Task<Guid> SeedForeignOwnedRecordAsync(Guid workspaceId, Guid publishedVersionId)
    {
        Result<BusinessObjectRecord> record = BusinessObjectRecord.CreateDraft(
            workspaceId,
            DomainDefinitionVersionId.From(publishedVersionId),
            1,
            DomainDefinitionKey.Create("loan_application").Value,
            $"foreign-{Guid.NewGuid():N}",
            "foreign-payload",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["display_name"] = ["Foreign owner"],
            },
            DomainSubjectReference.Service(Guid.NewGuid()),
            DateTime.UtcNow);
        record.IsSuccess.Should().BeTrue();
        using IServiceScope scope = fixture.CreateScope();
        BusinessObjectsDbContext db = scope.ServiceProvider.GetRequiredService<BusinessObjectsDbContext>();
        await db.BusinessObjectRecords.AddAsync(record.Value, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return record.Value.Id.Value;
    }

    private async Task<int> CountRuleBindingsAsync(Guid workspaceId)
    {
        using IServiceScope scope = fixture.CreateScope();
        RulesDbContext db = scope.ServiceProvider.GetRequiredService<RulesDbContext>();
        return await db.RuleBindings.CountAsync(
            binding => binding.WorkspaceId == workspaceId,
            TestContext.Current.CancellationToken);
    }

    private static object RuleBindingBody(
        string definitionKey,
        string targetId,
        int? expectedRevision = null) => new
        {
            expectedRevision,
            definitionKey,
            definitionVersion = 1,
            targetType = "neutral-consumer",
            targetId,
            useCaseOrTrigger = "validate",
            inputMappings = new
            {
                value = new
                {
                    kind = "Literal",
                    contextKey = (string?)null,
                    literalValues = new[] { "Approved" },
                },
            },
            priority = 0,
            enabled = true,
            failureBehavior = "FailClosed",
        };

    private async Task<HttpResponseMessage> SendBrowserPutAsync(string path, object body)
    {
        await fixture.RefreshBrowserSecurityContextAsync(TestContext.Current.CancellationToken);
        return await fixture.Client.PutAsJsonAsync(
            path,
            body,
            Json,
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> SendBrowserDeleteAsync(string path, object body)
    {
        using HttpRequestMessage request = new(HttpMethod.Delete, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        return await fixture.SendBrowserMutationAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static ProductPolicyRole Role(string roleKey) =>
        new(roleKey, new Dictionary<string, ProductRolePresentation>
        {
            ["en"] = new(roleKey, null),
        });

    private static ProductPolicyGrant Grant(
        string roleKey,
        string actionKey,
        string resourceType,
        string resourceKey,
        ProductActionScope scope = ProductActionScope.None) =>
        new(roleKey, actionKey, resourceType, resourceKey, scope);

    private async Task SeedPolicyAsync(Guid workspaceId, Guid policyVersionId)
    {
        var component = new
        {
            policyKey = "reference",
            versionId = policyVersionId,
            roles = new[]
            {
                new
                {
                    roleKey = "caseworker",
                    presentation = new Dictionary<string, object>
                    {
                        ["en"] = new { displayName = "Caseworker", description = (string?)null },
                        ["vi"] = new { displayName = "Chuyên viên", description = (string?)null },
                    },
                },
                new
                {
                    roleKey = "applicant",
                    presentation = new Dictionary<string, object>
                    {
                        ["en"] = new { displayName = "Applicant", description = (string?)null },
                    },
                },
            },
            grants = Array.Empty<object>(),
        };
        using IServiceScope scope = fixture.CreateScope();
        AuthorizationDbContext db = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        await db.Policies.AddAsync(new InstalledPolicyRow
        {
            WorkspaceId = workspaceId,
            VersionId = policyVersionId,
            PolicyKey = component.policyKey,
            CanonicalContent = JsonSerializer.Serialize(component, Json),
            Provenance = "api-test",
            InstalledAt = DateTimeOffset.UtcNow,
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> SeedForeignWorkspaceSubjectAsync()
    {
        User user = User.Create(
            "Foreign Subject",
            Email.Create($"foreign-{Guid.NewGuid():N}@example.com").Value);
        Workspace workspace = Workspace.CreatePersonal(
            "Foreign Workspace",
            WorkspaceSlug.Create($"foreign-{Guid.NewGuid():N}").Value);
        workspace.ActivateAfterOwnerVerification();
        WorkspaceMembership membership = WorkspaceMembership.CreatePersonalOwner(workspace.Id, user.Id);
        using IServiceScope scope = fixture.CreateScope();
        IdentityDbContext db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.Workspaces.AddAsync(workspace, TestContext.Current.CancellationToken);
        await db.WorkspaceMemberships.AddAsync(membership, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user.Id;
    }

    private async Task<HttpResponseMessage> SendMutationAsync(
        string path,
        object body,
        string idempotencyKey,
        string? correlationId = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body, options: Json),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (correlationId is not null)
            request.Headers.Add("X-Correlation-Id", correlationId);
        return await fixture.SendBrowserMutationAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static object AssignmentBody(
        Guid subjectId,
        Guid policyVersionId,
        string roleKey,
        int? expectedRevision = null) => new
        {
            target = new
            {
                kind = "Human",
                subjectId,
            },
            policyVersionId,
            roleKey,
            expectedRevision,
        };

    private static Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);

    private sealed record ReferenceProductSeed(
        Guid PolicyVersionId,
        Guid DefinitionId,
        Guid PublishedVersionId);
}
