using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Legal;
using Axis.Rules.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Api.Tests.BusinessObjects;

[Collection("Api")]
public sealed class BusinessObjectRecordEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task BusinessObjectRecordEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        HttpResponseMessage response = await fixture.Client.GetAsync(
            "/api/business-object-records",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecordWorkflow_WhenSubmitted_PersistsDraftValuesRuleEvidenceAndIsIdempotent()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        Workflow workflow = await CreateWorkflowAsync(accessToken);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{workflow.ObjectKey}",
            accessToken,
            new
            {
                idempotencyKey = Guid.NewGuid().ToString("N"),
                values = new Dictionary<string, string[]>
                {
                    ["applicant_name"] = ["Alice Smith"],
                    ["requested_amount"] = ["25000"],
                },
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await ReadJsonAsync(createResponse);
        Guid recordId = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().Be("Draft");
        created.GetProperty("revision").GetInt32().Should().Be(1);
        created.GetProperty("fields").GetArrayLength().Should().Be(2);

        HttpResponseMessage submitResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{recordId}/submit",
            accessToken,
            new { expectedRevision = 1 });

        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement submitted = await ReadJsonAsync(submitResponse);
        submitted.GetProperty("isSubmitted").GetBoolean().Should().BeTrue();
        submitted.GetProperty("record").GetProperty("status").GetString().Should().Be("Submitted");
        submitted.GetProperty("record").GetProperty("revision").GetInt32().Should().Be(2);
        submitted.GetProperty("ruleEvaluations").GetArrayLength().Should().Be(2);
        submitted.GetProperty("ruleEvaluations").EnumerateArray()
            .Should().AllSatisfy(evaluation => evaluation.GetProperty("isMatch").GetBoolean().Should().BeTrue());

        HttpResponseMessage repeatedSubmitResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{recordId}/submit",
            accessToken,
            new { expectedRevision = 1 });

        repeatedSubmitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement repeated = await ReadJsonAsync(repeatedSubmitResponse);
        repeated.GetProperty("isSubmitted").GetBoolean().Should().BeTrue();
        repeated.GetProperty("record").GetProperty("revision").GetInt32().Should().Be(2);

        HttpResponseMessage getResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/business-object-records/{recordId}",
            accessToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement persisted = await ReadJsonAsync(getResponse);
        persisted.GetProperty("status").GetString().Should().Be("Submitted");
        persisted.GetProperty("values").GetProperty("requested_amount")[0].GetString().Should().Be("25000");
        persisted.GetProperty("ruleEvaluations").GetArrayLength().Should().Be(2);

        HttpResponseMessage listResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/business-object-records?page=1&pageSize=20&objectKey={workflow.ObjectKey}",
            accessToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement list = await ReadJsonAsync(listResponse);
        list.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain(recordId);
    }

    [Fact]
    public async Task RecordWorkflow_WhenRuleDoesNotMatch_RemainsDraftAndCanBeCorrected()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        Workflow workflow = await CreateWorkflowAsync(accessToken);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{workflow.ObjectKey}",
            accessToken,
            new
            {
                idempotencyKey = Guid.NewGuid().ToString("N"),
                values = new Dictionary<string, string[]>
                {
                    ["applicant_name"] = ["Alice Smith"],
                    ["requested_amount"] = ["50"],
                },
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await ReadJsonAsync(createResponse);
        Guid recordId = created.GetProperty("id").GetGuid();

        HttpResponseMessage rejectedSubmit = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{recordId}/submit",
            accessToken,
            new { expectedRevision = 1 });

        rejectedSubmit.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement rejected = await ReadJsonAsync(rejectedSubmit);
        rejected.GetProperty("isSubmitted").GetBoolean().Should().BeFalse();
        rejected.GetProperty("record").GetProperty("status").GetString().Should().Be("Draft");
        rejected.GetProperty("record").GetProperty("revision").GetInt32().Should().Be(1);
        rejected.GetProperty("ruleEvaluations").EnumerateArray()
            .Single(evaluation => evaluation.GetProperty("fieldKey").GetString() == "requested_amount")
            .GetProperty("isMatch").GetBoolean()
            .Should().BeFalse();

        HttpResponseMessage saveResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/business-object-records/{recordId}",
            accessToken,
            new
            {
                expectedRevision = 1,
                values = new Dictionary<string, string[]>
                {
                    ["applicant_name"] = ["Alice Smith"],
                    ["requested_amount"] = ["25000"],
                },
            });
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement saved = await ReadJsonAsync(saveResponse);
        saved.GetProperty("revision").GetInt32().Should().Be(2);

        HttpResponseMessage submitResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{recordId}/submit",
            accessToken,
            new { expectedRevision = 2 });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement submitted = await ReadJsonAsync(submitResponse);
        submitted.GetProperty("isSubmitted").GetBoolean().Should().BeTrue();
        submitted.GetProperty("record").GetProperty("status").GetString().Should().Be("Submitted");
    }

    [Fact]
    public async Task RecordWorkflow_UsesPublishedBindingRevision_AfterBindingUpdate()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        Workflow workflow = await CreateWorkflowAsync(accessToken);

        HttpResponseMessage updateBindingResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/rule-bindings/{workflow.AmountBindingId}",
            accessToken,
            new
            {
                expectedRevision = workflow.AmountBindingRevision,
                definitionKey = RuleDefinitionKeys.NumericRange,
                definitionVersion = 1,
                targetType = "business-object-field",
                targetId = $"{workflow.ObjectKey}.requested_amount",
                useCaseOrTrigger = "field-validation",
                inputMappings = new
                {
                    value = new { kind = "Context", contextKey = "record.value", literalValues = Array.Empty<string>() },
                    min = new { kind = "Literal", contextKey = (string?)null, literalValues = new[] { "2000" } },
                    max = new { kind = "Literal", contextKey = (string?)null, literalValues = new[] { "50000" } },
                },
            });
        updateBindingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(updateBindingResponse)).GetProperty("revision").GetInt32()
            .Should().Be(workflow.AmountBindingRevision + 1);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{workflow.ObjectKey}",
            accessToken,
            new
            {
                idempotencyKey = Guid.NewGuid().ToString("N"),
                values = new Dictionary<string, string[]>
                {
                    ["applicant_name"] = ["Alice Smith"],
                    ["requested_amount"] = ["1500"],
                },
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        Guid recordId = (await ReadJsonAsync(createResponse)).GetProperty("id").GetGuid();

        HttpResponseMessage submitResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{recordId}/submit",
            accessToken,
            new { expectedRevision = 1 });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement submitted = await ReadJsonAsync(submitResponse);
        submitted.GetProperty("isSubmitted").GetBoolean().Should().BeTrue();
        submitted.GetProperty("ruleEvaluations").EnumerateArray()
            .Single(evaluation => evaluation.GetProperty("fieldKey").GetString() == "requested_amount")
            .GetProperty("bindingRevision").GetInt32()
            .Should().Be(workflow.AmountBindingRevision);
    }

    [Fact]
    public async Task GetBusinessObjectRecord_WhenRecordBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        string ownerToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string otherToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        Workflow workflow = await CreateWorkflowAsync(ownerToken);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-records/{workflow.ObjectKey}",
            ownerToken,
            new
            {
                idempotencyKey = Guid.NewGuid().ToString("N"),
                values = new Dictionary<string, string[]>
                {
                    ["applicant_name"] = ["Alice Smith"],
                    ["requested_amount"] = ["25000"],
                },
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        Guid recordId = (await ReadJsonAsync(createResponse)).GetProperty("id").GetGuid();

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/business-object-records/{recordId}",
            otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Workflow> CreateWorkflowAsync(string accessToken)
    {
        string name = $"Loan Application {Guid.NewGuid():N}";
        HttpResponseMessage createDefinitionResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name });
        createDefinitionResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await ReadJsonAsync(createDefinitionResponse);
        Guid definitionId = created.GetProperty("id").GetGuid();
        string objectKey = created.GetProperty("objectKey").GetString()!;

        (Guid requiredBindingId, _) = await CreateRuleBindingAsync(
            accessToken,
            objectKey,
            "applicant_name",
            RuleDefinitionKeys.Required,
            new
            {
                value = new { kind = "Context", contextKey = "record.value", literalValues = Array.Empty<string>() },
            });
        (Guid amountBindingId, int amountBindingRevision) = await CreateRuleBindingAsync(
            accessToken,
            objectKey,
            "requested_amount",
            RuleDefinitionKeys.NumericRange,
            new
            {
                value = new { kind = "Context", contextKey = "record.value", literalValues = Array.Empty<string>() },
                min = new { kind = "Literal", contextKey = (string?)null, literalValues = new[] { "1000" } },
                max = new { kind = "Literal", contextKey = (string?)null, literalValues = new[] { "50000" } },
            });

        HttpResponseMessage saveResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/business-object-definitions/{definitionId}/unpublished",
            accessToken,
            new
            {
                expectedRevision = 1,
                name,
                fields = new object[]
                {
                    new
                    {
                        fieldKey = "applicant_name",
                        label = "Applicant name",
                        fieldType = "Text",
                        rules = new[] { new { bindingId = requiredBindingId } },
                    },
                    new
                    {
                        fieldKey = "requested_amount",
                        label = "Requested amount",
                        fieldType = "Integer",
                        rules = new[] { new { bindingId = amountBindingId } },
                    },
                },
            });
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage publishResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-definitions/{definitionId}/publish",
            accessToken,
            new { expectedRevision = 2 });
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return new Workflow(objectKey, amountBindingId, amountBindingRevision);
    }

    private async Task<(Guid Id, int Revision)> CreateRuleBindingAsync(
        string accessToken,
        string objectKey,
        string fieldKey,
        string definitionKey,
        object inputMappings)
    {
        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            accessToken,
            new
            {
                definitionKey,
                definitionVersion = 1,
                targetType = "business-object-field",
                targetId = $"{objectKey}.{fieldKey}",
                useCaseOrTrigger = "field-validation",
                inputMappings,
            });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await ReadJsonAsync(response);
        return (body.GetProperty("id").GetGuid(), body.GetProperty("revision").GetInt32());
    }

    private async Task<string> CreateVerifiedSessionTokenAsync(string email)
    {
        await RegisterAsync(email);
        HttpResponseMessage verifyResponse = await fixture.Client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new { token = fixture.EmailCapture.GetVerificationToken(email) },
            Json,
            TestContext.Current.CancellationToken);
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        string verifier = CreateCodeVerifier();
        string state = Guid.NewGuid().ToString("N");
        Dictionary<string, string?> authorizeQuery = new()
        {
            ["response_type"] = "code",
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = "openid email profile",
            ["state"] = state,
        };
        HttpResponseMessage authorizeResponse = await fixture.Client.GetAsync(
            QueryHelpers.AddQueryString("/connect/authorize", authorizeQuery),
            TestContext.Current.CancellationToken);
        authorizeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        Uri redirect = authorizeResponse.Headers.Location!;
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> callbackQuery =
            QueryHelpers.ParseQuery(redirect.Query);
        callbackQuery["state"].ToString().Should().Be(state);

        using FormUrlEncodedContent tokenRequest = new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
            ["code"] = callbackQuery["code"].ToString(),
            ["code_verifier"] = verifier,
        });
        HttpResponseMessage tokenResponse = await fixture.Client.PostAsync(
            "/connect/token",
            tokenRequest,
            TestContext.Current.CancellationToken);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await ReadJsonAsync(tokenResponse)).GetProperty("access_token").GetString()!;
    }

    private async Task<HttpResponseMessage> SendWithBearerAsync(
        HttpMethod method,
        string url,
        string accessToken,
        object? body = null)
    {
        using HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        return await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> RegisterAsync(string email)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(
                new
                {
                    fullName = "Alice Smith",
                    email,
                    password = Password,
                    passwordConfirmation = Password,
                    acceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
                    acceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
                },
                options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        HttpResponseMessage response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        return response;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);

    private static string CreateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string UniqueEmail() => $"records-{Guid.NewGuid():N}@example.com";

    private sealed record Workflow(string ObjectKey, Guid AmountBindingId, int AmountBindingRevision);
}
