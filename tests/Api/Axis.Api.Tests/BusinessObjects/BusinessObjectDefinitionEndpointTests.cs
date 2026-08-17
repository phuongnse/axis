using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Domain.Aggregates;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Legal;
using Axis.Rules.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Api.Tests.BusinessObjects;

[Collection("Api")]
public sealed class BusinessObjectDefinitionEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task BusinessObjectDefinitionEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        using HttpClient anonymousClient = fixture.CreateAnonymousClient();
        HttpResponseMessage response = await anonymousClient.GetAsync("/api/business-object-definitions", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListDefinitions_WhenSortPairIsSpecified_OrdersTheWholeDataset()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string suffix = Guid.NewGuid().ToString("N");
        Guid alphaId = await CreateUnpublishedAsync(accessToken, $"Alpha {suffix}");
        Guid zuluId = await CreateUnpublishedAsync(accessToken, $"Zulu {suffix}");

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/business-object-definitions?page=1&pageSize=100&sortBy=Name&sortDirection=Descending",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Where(id => id == alphaId || id == zuluId)
            .Should().Equal(zuluId, alphaId);
    }

    [Fact]
    public async Task DefinitionAuthoring_WhenNonBuilder_ReturnsForbidden()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        await fixture.SetProductAuthorizationTestDecisionAsync(
            _ => ProductAuthorizationDecision.Denied,
            TestContext.Current.CancellationToken);
        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Denied,
            TestContext.Current.CancellationToken);

        HttpResponseMessage actionsResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/business-object-definitions/actions",
            accessToken);
        actionsResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name = "Ungranted Definition" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DefinitionActions_WhenAuthorizationUnavailable_ReturnsServiceUnavailable()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Unavailable,
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/business-object-definitions/actions",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        ApiProblem problem = await ReadProblemAsync(response);
        problem.Code.Should().Be(BusinessObjectsProblemCodes.AuthorizationUnavailable);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name = "Unavailable Definition" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task PublishedDefinition_WhenNonBuilderHasPublishedRead_ReturnsRuntimeOnlyProjection()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        Guid definitionId = await CreateUnpublishedAsync(
            accessToken,
            ObjectNameFromKey(UniqueKey("runtime")));
        HttpResponseMessage saveResponse = await SaveWithOneFieldAsync(
            accessToken,
            definitionId,
            expectedRevision: 1,
            fieldKey: "name");
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage publishResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-definitions/{definitionId:D}/publish",
            accessToken,
            new { expectedRevision = 2 });
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Denied,
            TestContext.Current.CancellationToken);
        await fixture.SetProductAuthorizationTestDecisionAsync(
            request => request.ActionKey == BusinessObjectProductActions.DefinitionReadPublished
                    ? new ProductAuthorizationDecision(true, ProductActionScope.None)
                    : ProductAuthorizationDecision.Denied,
            TestContext.Current.CancellationToken);

        HttpResponseMessage getResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/business-object-definitions/{definitionId:D}",
            accessToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement detail = await getResponse.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        detail.GetProperty("status").GetString().Should().Be(nameof(BusinessObjectDefinitionStatus.Published));
        detail.GetProperty("actions").GetProperty("canSave").GetBoolean().Should().BeFalse();
        detail.GetProperty("actions").GetProperty("canPublish").GetBoolean().Should().BeFalse();

        HttpResponseMessage listResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/business-object-definitions?page=1&pageSize=100",
            accessToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        list.GetProperty("items").EnumerateArray()
            .Should().Contain(item => item.GetProperty("id").GetGuid() == definitionId);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name = "Runtime Reader Cannot Author" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DefineBusinessObject_WhenAuthenticated_CreatesSavesPublishesGetsAndListsDefinition()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("customer");
        string objectName = ObjectNameFromKey(objectKey);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name = objectName });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location!.ToString().Should().StartWith($"/api/business-object-definitions/");
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        Guid definitionId = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().Be(nameof(BusinessObjectDefinitionStatus.Unpublished));
        created.GetProperty("objectKey").GetString().Should().Be(objectKey);
        created.GetProperty("revision").GetInt32().Should().Be(1);
        JsonElement createdMetadata = created.GetProperty("metadata");
        createdMetadata.GetProperty("createdBy").GetProperty("displayName").GetString()
            .Should().Be("Alice Smith");
        createdMetadata.GetProperty("modifiedBy").GetProperty("displayName").GetString()
            .Should().Be("Alice Smith");

        Guid requiredBindingId = await CreateRuleBindingAsync(
            accessToken,
            objectKey,
            "name",
            RuleDefinitionKeys.Required,
            new
            {
                value = new
                {
                    kind = "Context",
                    contextKey = "record.value",
                    literalValues = Array.Empty<string>(),
                },
            });
        Guid textLengthBindingId = await CreateRuleBindingAsync(
            accessToken,
            objectKey,
            "name",
            RuleDefinitionKeys.TextLength,
            new
            {
                value = new
                {
                    kind = "Context",
                    contextKey = "record.value",
                    literalValues = Array.Empty<string>(),
                },
                min = new
                {
                    kind = "Literal",
                    contextKey = (string?)null,
                    literalValues = new[] { "1" },
                },
                max = new
                {
                    kind = "Literal",
                    contextKey = (string?)null,
                    literalValues = new[] { "120" },
                },
            });

        HttpResponseMessage saveResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/business-object-definitions/{definitionId}/unpublished",
            accessToken,
            new
            {
                expectedRevision = 1,
                name = $"{objectName} renamed",
                fields = new object[]
                {
                    new
                    {
                        fieldKey = "name",
                        label = "Name",
                        fieldType = "Text",
                        rules = new object[]
                        {
                            new { bindingId = requiredBindingId },
                            new { bindingId = textLengthBindingId },
                        },
                    },
                    new
                    {
                        fieldKey = "status",
                        label = "Status",
                        fieldType = "Choice",
                        choiceConfiguration = new
                        {
                            selectionMode = "Single",
                            options = new[]
                            {
                                new { optionKey = "draft", label = "Draft" },
                                new { optionKey = "submitted", label = "Submitted" },
                                new { optionKey = "approved", label = "Approved" },
                            },
                        },
                        rules = Array.Empty<object>(),
                    },
                },
            });

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement saved = await saveResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        saved.GetProperty("objectKey").GetString().Should().Be(objectKey);
        saved.GetProperty("revision").GetInt32().Should().Be(2);
        saved.GetProperty("metadata").GetProperty("modifiedBy").GetProperty("displayName").GetString()
            .Should().Be("Alice Smith");
        saved.GetProperty("fields").GetArrayLength().Should().Be(2);
        JsonElement savedNameField = saved.GetProperty("fields")[0];
        savedNameField.GetProperty("fieldType").GetString().Should().Be("Text");
        savedNameField.GetProperty("rules").GetArrayLength().Should().Be(2);
        savedNameField.GetProperty("rules")[0].GetProperty("bindingId").GetGuid()
            .Should().Be(requiredBindingId);
        savedNameField.GetProperty("rules")[1].GetProperty("bindingId").GetGuid()
            .Should().Be(textLengthBindingId);
        JsonElement savedStatusField = saved.GetProperty("fields")[1];
        savedStatusField.GetProperty("fieldType").GetString().Should().Be("Choice");
        savedStatusField.GetProperty("choiceConfiguration").GetProperty("selectionMode").GetString()
            .Should().Be("Single");
        savedStatusField.GetProperty("choiceConfiguration")
            .GetProperty("options")
            .EnumerateArray()
            .Select(option => option.GetProperty("optionKey").GetString())
            .Should().Equal("draft", "submitted", "approved");

        HttpResponseMessage publishResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/business-object-definitions/{definitionId}/publish",
            accessToken,
            new { expectedRevision = 2 });

        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement published = await publishResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        published.GetProperty("status").GetString().Should().Be(nameof(BusinessObjectDefinitionStatus.Published));
        published.GetProperty("latestPublishedVersionNumber").GetInt32().Should().Be(1);
        published.GetProperty("latestPublishedVersion")
            .GetProperty("fields")
            .GetArrayLength()
            .Should().Be(2);
        JsonElement publishedStatusField = published.GetProperty("latestPublishedVersion")
            .GetProperty("fields")[1];
        publishedStatusField.GetProperty("fieldType").GetString().Should().Be("Choice");
        publishedStatusField.GetProperty("choiceConfiguration").GetProperty("selectionMode").GetString()
            .Should().Be("Single");
        publishedStatusField.GetProperty("choiceConfiguration")
            .GetProperty("options")
            .EnumerateArray()
            .Select(option => option.GetProperty("label").GetString())
            .Should().Equal("Draft", "Submitted", "Approved");

        HttpResponseMessage getResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/business-object-definitions/{definitionId}",
            accessToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement detail = await getResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        detail.GetProperty("objectKey").GetString().Should().Be(objectKey);

        HttpResponseMessage listResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/business-object-definitions?page=1&pageSize=20",
            accessToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        list.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        list.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain(definitionId);
    }

    [Fact]
    public async Task SaveUnpublished_WhenExpectedRevisionIsStale_ReturnsConflictProblemCode()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("invoice");
        Guid definitionId = await CreateUnpublishedAsync(accessToken, ObjectNameFromKey(objectKey));

        HttpResponseMessage firstSave = await SaveWithOneFieldAsync(
            accessToken,
            definitionId,
            expectedRevision: 1,
            fieldKey: "number");
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage staleSave = await SaveWithOneFieldAsync(
            accessToken,
            definitionId,
            expectedRevision: 1,
            fieldKey: "total");

        staleSave.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ApiProblem problem = await ReadProblemAsync(staleSave);
        problem.Code.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionConflict);
        problem.Type.Should().Be(ProblemType(BusinessObjectsProblemCodes.BusinessObjectDefinitionConflict));
    }

    [Fact]
    public async Task CreateUnpublished_WhenObjectKeyAlreadyExistsInWorkspace_ReturnsConflictProblemCode()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("account");
        string objectName = ObjectNameFromKey(objectKey);
        await CreateUnpublishedAsync(accessToken, objectName);

        HttpResponseMessage duplicateResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name = objectName });

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ApiProblem problem = await ReadProblemAsync(duplicateResponse);
        problem.Code.Should().Be(BusinessObjectsProblemCodes.ObjectKeyAlreadyExists);
        problem.Type.Should().Be(ProblemType(BusinessObjectsProblemCodes.ObjectKeyAlreadyExists));
    }

    [Fact]
    public async Task SaveUnpublished_WhenRuleBindingContextIsIncompatible_RejectsImmutableDefinitionWhileCompatibleBindingSucceeds()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("invoice");
        Guid definitionId = await CreateUnpublishedAsync(accessToken, ObjectNameFromKey(objectKey));
        Guid incompatibleBindingId = await CreateRuleBindingAsync(
            accessToken,
            objectKey,
            "amount",
            RuleDefinitionKeys.NumericRange,
            new
            {
                value = new
                {
                    kind = "Context",
                    contextKey = "record.value",
                    literalValues = Array.Empty<string>(),
                },
            });

        HttpResponseMessage incompatibleSave = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/business-object-definitions/{definitionId}/unpublished",
            accessToken,
            new
            {
                expectedRevision = 1,
                name = ObjectNameFromKey(objectKey),
                fields = new object[]
                {
                    new
                    {
                        fieldKey = "amount",
                        label = "Amount",
                        fieldType = "Text",
                        rules = new[] { new { bindingId = incompatibleBindingId } },
                    },
                },
            });

        incompatibleSave.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        ApiProblem problem = await ReadProblemAsync(incompatibleSave);
        problem.Code.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionInvalid);

        HttpResponseMessage detailResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/business-object-definitions/{definitionId}",
            accessToken);
        JsonElement detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        detail.GetProperty("status").GetString().Should().Be(nameof(BusinessObjectDefinitionStatus.Unpublished));
        detail.GetProperty("revision").GetInt32().Should().Be(1);
        detail.GetProperty("fields").GetArrayLength().Should().Be(0);

        Guid compatibleBindingId = await CreateRuleBindingAsync(
            accessToken,
            objectKey,
            "amount",
            RuleDefinitionKeys.TextLength,
            new
            {
                value = new
                {
                    kind = "Context",
                    contextKey = "record.value",
                    literalValues = Array.Empty<string>(),
                },
            });
        HttpResponseMessage compatibleSave = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/business-object-definitions/{definitionId}/unpublished",
            accessToken,
            new
            {
                expectedRevision = 1,
                name = ObjectNameFromKey(objectKey),
                fields = new object[]
                {
                    new
                    {
                        fieldKey = "amount",
                        label = "Amount",
                        fieldType = "Text",
                        rules = new[] { new { bindingId = compatibleBindingId } },
                    },
                },
            });

        compatibleSave.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetBusinessObjectDefinition_WhenDefinitionBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        string ownerToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string otherToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        Guid definitionId = await CreateUnpublishedAsync(
            ownerToken,
            ObjectNameFromKey(UniqueKey("private_object")));

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/business-object-definitions/{definitionId}",
            otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ApiProblem problem = await ReadProblemAsync(response);
        problem.Code.Should().Be(BusinessObjectsProblemCodes.BusinessObjectDefinitionNotFound);
    }

    [Fact]
    public async Task CreateUnpublished_WhenClientSendsObjectKey_IgnoresClientValueAndReturnsDerivedKey()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("client_owned");

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name = ObjectNameFromKey(objectKey), objectKey = "client_value" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        body.GetProperty("objectKey").GetString().Should().Be(objectKey);
    }

    private async Task<Guid> CreateUnpublishedAsync(string accessToken, string objectName)
    {
        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/business-object-definitions",
            accessToken,
            new { name = objectName });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateRuleBindingAsync(
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
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> SaveWithOneFieldAsync(
        string accessToken,
        Guid definitionId,
        int expectedRevision,
        string fieldKey) =>
        await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/business-object-definitions/{definitionId}/unpublished",
            accessToken,
            new
            {
                expectedRevision,
                name = "Business Object",
                fields = new object[]
                {
                    new
                    {
                        fieldKey,
                        label = "Name",
                    },
                },
            });

    private async Task<string> CreateVerifiedSessionTokenAsync(string email)
    {
        await RegisterAsync(email);
        HttpResponseMessage verifyResponse = await VerifyEmailAsync(CapturedToken(email));
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await fixture.EnableProductAuthorizationTestAccessAsync(
            TestContext.Current.CancellationToken);

        string verifier = CreateCodeVerifier();
        string state = Guid.NewGuid().ToString("N");
        Dictionary<string, string?> authorizeQuery = new()
        {
            ["response_type"] = "code",
            ["client_id"] = "axis_mcp",
            ["redirect_uri"] = "http://127.0.0.1:48123/callback",
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = "openid email profile",
            ["state"] = state,
        };

        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", authorizeQuery);
        HttpResponseMessage authorizeResponse = await fixture.Client.GetAsync(
            authorizeUrl,
            TestContext.Current.CancellationToken);
        authorizeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        Uri redirect = authorizeResponse.Headers.Location
            ?? throw new InvalidOperationException("Authorization response did not include a redirect.");
        if (!redirect.IsAbsoluteUri)
            redirect = new Uri(new Uri("https://localhost"), redirect);
        if (redirect.AbsolutePath == "/connect/authorize")
        {
            authorizeResponse = await fixture.Client.GetAsync(
                redirect.PathAndQuery,
                TestContext.Current.CancellationToken);
            authorizeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
            redirect = authorizeResponse.Headers.Location
                ?? throw new InvalidOperationException("Authorization callback did not include a redirect.");
            if (!redirect.IsAbsoluteUri)
                redirect = new Uri(new Uri("https://localhost"), redirect);
        }
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> callbackQuery =
            QueryHelpers.ParseQuery(redirect.Query);
        callbackQuery["state"].ToString().Should().Be(state);
        string code = callbackQuery["code"].ToString();
        code.Should().NotBeNullOrWhiteSpace();

        using FormUrlEncodedContent tokenRequest = new(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "axis_mcp",
            ["redirect_uri"] = "http://127.0.0.1:48123/callback",
            ["code"] = code,
            ["code_verifier"] = verifier,
        });

        HttpResponseMessage tokenResponse = await fixture.Client.PostAsync(
            "/connect/token",
            tokenRequest,
            TestContext.Current.CancellationToken);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        return tokenBody.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Token response did not include an access token.");
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
            Content = JsonContent.Create(ValidRegisterRequest(email), options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await fixture.SendBrowserMutationAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> VerifyEmailAsync(string token) =>
        await fixture.PostBrowserJsonAsync(
            "/api/auth/verify-email",
            new { token },
            TestContext.Current.CancellationToken);

    private string CapturedToken(string email) =>
        fixture.EmailCapture.GetVerificationToken(email)
        ?? throw new InvalidOperationException($"No verification token was captured for {email}.");

    private static object ValidRegisterRequest(string email) => new
    {
        FullName = "Alice Smith",
        Email = email,
        Password,
        PasswordConfirmation = Password,
        AcceptedTermsVersion = WellKnownLegalDocuments.TermsVersion,
        AcceptedPrivacyVersion = WellKnownLegalDocuments.PrivacyVersion,
    };

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        string? code = body.TryGetProperty("code", out JsonElement codeElement)
            ? codeElement.GetString()
            : null;
        string? type = body.TryGetProperty("type", out JsonElement typeElement)
            ? typeElement.GetString()
            : null;
        string? detail = body.TryGetProperty("detail", out JsonElement detailElement)
            ? detailElement.GetString()
            : null;
        return new ApiProblem(detail, code, type);
    }

    private static string CreateCodeVerifier() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier)
    {
        byte[] bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string UniqueEmail() => $"objects-{Guid.NewGuid():N}@example.com";

    private static string UniqueKey(string prefix) =>
        $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(63, prefix.Length + 9)];

    private static string ObjectNameFromKey(string key) => key.Replace('_', ' ');

    private static string ProblemType(string code) => $"urn:axis:problem:{code}";

    private sealed record ApiProblem(string? Detail, string? Code, string? Type);
}
