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

namespace Axis.Api.Tests.Rules;

[Collection("Api")]
public sealed class RuleDefinitionEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task RuleDefinitionEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        HttpResponseMessage response = await fixture.Client.GetAsync("/api/rules", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RuleBindings_WhenCreatedUpdatedAndDeleted_PreserveIndependentRuleDefinition()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        object mappings = new
        {
            value = new
            {
                kind = "Literal",
                contextKey = (string?)null,
                literalValues = new[] { "Approved" },
            },
        };

        HttpResponseMessage firstCreateResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            accessToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-1",
                useCaseOrTrigger = "validate",
                inputMappings = mappings,
            });
        firstCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement firstBinding = await firstCreateResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        string firstBindingId = firstBinding.GetProperty("id").GetString()!;
        int firstRevision = firstBinding.GetProperty("revision").GetInt32();

        HttpResponseMessage secondCreateResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            accessToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-2",
                useCaseOrTrigger = "validate",
                inputMappings = mappings,
            });
        secondCreateResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        HttpResponseMessage updateResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/rule-bindings/{firstBindingId}",
            accessToken,
            new
            {
                expectedRevision = firstRevision,
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-1-updated",
                useCaseOrTrigger = "validate",
                inputMappings = mappings,
                enabled = false,
            });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement updatedBinding = await updateResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        updatedBinding.GetProperty("enabled").GetBoolean().Should().BeFalse();

        HttpResponseMessage usageResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/field.required/bindings?version=1",
            accessToken);
        usageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement[] usages = (await usageResponse.Content.ReadFromJsonAsync<JsonElement[]>(Json, TestContext.Current.CancellationToken))!;
        usages.Should().HaveCount(2);
        usages.Select(usage => usage.GetProperty("bindingId").GetString()).Should().Contain(firstBindingId);
        usages.Select(usage => usage.GetProperty("targetId").GetString()).Should().Contain("consumer-1-updated");

        HttpResponseMessage deleteResponse = await SendWithBearerAsync(
            HttpMethod.Delete,
            $"/api/rule-bindings/{firstBindingId}",
            accessToken);
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        HttpResponseMessage ruleResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/field.required",
            accessToken);
        ruleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        HttpResponseMessage remainingUsageResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/field.required/bindings?version=1",
            accessToken);
        JsonElement[] remainingUsages = (await remainingUsageResponse.Content.ReadFromJsonAsync<JsonElement[]>(Json, TestContext.Current.CancellationToken))!;
        remainingUsages.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListRuleDefinitions_WhenAuthenticated_ReturnsGeneralSystemCatalog()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules?page=1&pageSize=20",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        body.GetProperty("items").EnumerateArray()
            .Select(definition => definition.GetProperty("definitionKey").GetString())
            .Should().Contain([
                RuleDefinitionKeys.Required,
                RuleDefinitionKeys.NumericRange,
                RuleDefinitionKeys.DateTimeRange,
                RuleDefinitionKeys.ChoiceSelectionCount,
            ]);
        body.GetProperty("totalCount").GetInt32().Should().Be(9);

        HttpResponseMessage searchResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules?page=1&pageSize=20&query=required&language=en",
            accessToken);
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement search = await searchResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        search.GetProperty("items").EnumerateArray()
            .Select(definition => definition.GetProperty("definitionKey").GetString())
            .Should().Contain(RuleDefinitionKeys.Required)
            .And.NotContain(RuleDefinitionKeys.DateTimeRange);
        search.GetProperty("totalCount").GetInt32().Should().BeLessThan(9);
    }

    [Fact]
    public async Task WorkspaceRule_WhenAccessedFromAnotherWorkspace_ReturnsNotFoundWithoutMutation()
    {
        string ownerToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules",
            ownerToken,
            new
            {
                name = "Isolated credit threshold",
                description = "Proves workspace isolation.",
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        string definitionKey = created.GetProperty("definitionKey").GetString()!;

        string otherWorkspaceToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage disclosureResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rules/{definitionKey}",
            otherWorkspaceToken);
        HttpResponseMessage mutationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/archive",
            otherWorkspaceToken,
            new { expectedRevision = 1 });

        disclosureResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        mutationResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        HttpResponseMessage ownerResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rules/{definitionKey}",
            ownerToken);
        ownerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement ownerDefinition = await ownerResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        ownerDefinition.GetProperty("status").GetString().Should().Be("Draft");
        ownerDefinition.GetProperty("revision").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task RuleAuthoringContracts_WhenAuthenticated_ReturnCapabilitiesAndExecutableSystemDetail()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage languageResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/expression-language",
            accessToken);

        languageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement language = await languageResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        language.GetProperty("version").GetInt32().Should().Be(1);
        language.GetProperty("operators").EnumerateArray()
            .Select(definition => definition.GetProperty("operator").GetString())
            .Should().Contain("GreaterThan");
        JsonElement length = language.GetProperty("functions").EnumerateArray().Single(
            definition => definition.GetProperty("function").GetString() == "Length");
        length.GetProperty("parameters")[0].GetProperty("acceptedTypes")[0].GetString()
            .Should().Be("Text");
        length.GetProperty("returnType").GetString().Should().Be("Integer");
        length.GetProperty("documentation").GetProperty("locales")
            .GetProperty("en").GetProperty("summary").GetString()
            .Should().NotBeNullOrWhiteSpace();
        language.GetProperty("logicalOperators").GetArrayLength().Should().BeGreaterThan(0);
        language.GetProperty("operandKinds").GetArrayLength().Should().BeGreaterThan(0);
        language.GetProperty("valueTypes").GetArrayLength().Should().BeGreaterThan(0);
        language.GetProperty("cardinalities").GetArrayLength().Should().BeGreaterThan(0);
        language.GetProperty("limitDefinitions").EnumerateArray()
            .All(definition =>
                definition.GetProperty("documentation").GetProperty("locales")
                    .TryGetProperty("vi", out _))
            .Should().BeTrue();

        HttpResponseMessage guideResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules/expression-language/guide",
            accessToken,
            new
            {
                expressionLanguageVersion = 1,
                definitionKey = RuleDefinitionKeys.NumericRange,
                inputs = new[]
                {
                    new
                    {
                        key = "value",
                        label = "Value",
                        types = new[] { "Decimal" },
                        isRequired = true,
                        allowMultiple = false,
                        allowedValues = Array.Empty<string>(),
                    },
                    new
                    {
                        key = "threshold",
                        label = "Threshold",
                        types = new[] { "Decimal" },
                        isRequired = true,
                        allowMultiple = false,
                        allowedValues = Array.Empty<string>(),
                    },
                },
                query = "thresold",
                language = "en",
            });
        guideResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement guide = await guideResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        JsonElement threshold = guide.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("items").EnumerateArray())
            .Single(item => item.GetProperty("referenceKey").GetString() == "threshold");
        threshold.GetProperty("referenceKind").GetString().Should().Be("Input");
        threshold.GetProperty("displayName").GetProperty("segments").EnumerateArray()
            .Should().Contain(segment => segment.GetProperty("isMatch").GetBoolean());

        HttpResponseMessage detailResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rules/{RuleDefinitionKeys.Required}",
            accessToken);

        detailResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        detail.GetProperty("origin").GetString().Should().Be("System");
        detail.GetProperty("status").GetString().Should().Be("Published");
        detail.GetProperty("expressionLanguageVersion").GetInt32().Should().Be(1);
        detail.GetProperty("output").GetProperty("type").GetString().Should().Be("Boolean");
        detail.GetProperty("output").GetProperty("cardinality").GetString().Should().Be("Scalar");
        JsonElement valueInput = detail.GetProperty("inputs").EnumerateArray()
            .Single(input => input.GetProperty("key").GetString() == "value");
        valueInput.GetProperty("isRequired").GetBoolean().Should().BeFalse();
        detail.GetProperty("condition").GetProperty("left").GetProperty("function").GetString()
            .Should().Be("IsBlank");
        detail.GetProperty("condition").GetProperty("right").GetProperty("literal")
            .GetProperty("values").EnumerateArray().Single().GetString().Should().Be("false");
        detail.TryGetProperty("outcome", out _).Should().BeFalse();
        detail.TryGetProperty("contextKey", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ManageWorkspaceRule_WhenAuthenticated_SavesSimulatesAndPublishesExactVersion()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string name = $"Credit threshold {Guid.NewGuid():N}"[..32];

        HttpResponseMessage projectionResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules/condition/project",
            accessToken,
            new
            {
                expressionLanguageVersion = 1,
                inputs = new[]
                {
                    new
                    {
                        label = "Value",
                        types = new[] { "Decimal" },
                        isRequired = true,
                        allowMultiple = false,
                        allowedValues = Array.Empty<string>(),
                    },
                    new
                    {
                        label = "Threshold",
                        types = new[] { "Decimal" },
                        isRequired = true,
                        allowMultiple = false,
                        allowedValues = Array.Empty<string>(),
                    },
                },
                condition = new
                {
                    nodeId = "threshold_check",
                    logicalOperator = (string?)null,
                    predicateOperator = "GreaterThan",
                    left = new { kind = "Input", reference = "Value", literal = (object?)null },
                    right = new { kind = "Input", reference = "Threshold", literal = (object?)null },
                    children = Array.Empty<object>(),
                },
                language = "vi",
            });
        projectionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement projected = await projectionResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        projected.GetProperty("condition").GetProperty("predicateOperator").GetString()
            .Should().Be("GreaterThan");
        JsonElement[] displayTokens = projected.GetProperty("display").GetProperty("tokens")
            .EnumerateArray()
            .ToArray();
        displayTokens
            .Select(token => token.GetProperty("text").GetString())
            .Should().Contain("lớn hơn");
        displayTokens.Single(token => token.GetProperty("text").GetString() == "lớn hơn")
            .GetProperty("referenceKey").GetString()
            .Should().Be("GreaterThan");
        projected.GetProperty("condition").GetProperty("left").GetProperty("reference").GetString()
            .Should().NotBe("Value");

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules",
            accessToken,
            new
            {
                name,
                description = "Flags credit values above a workspace threshold.",
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        string definitionKey = created.GetProperty("definitionKey").GetString()!;
        created.GetProperty("status").GetString().Should().Be("Draft");
        created.GetProperty("expressionLanguageVersion").GetInt32().Should().Be(1);
        created.GetProperty("revision").GetInt32().Should().Be(1);
        created.GetProperty("output").GetProperty("type").GetString().Should().Be("Boolean");
        created.GetProperty("output").GetProperty("cardinality").GetString().Should().Be("Scalar");

        object inputs = new[]
        {
            new
            {
                label = "Value",
                types = new[] { "Decimal" },
                isRequired = true,
                allowMultiple = false,
                allowedValues = Array.Empty<string>(),
            },
            new
            {
                label = "Threshold",
                types = new[] { "Decimal" },
                isRequired = true,
                allowMultiple = false,
                allowedValues = Array.Empty<string>(),
            },
        };

        HttpResponseMessage saveResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/rules/{definitionKey}/draft",
            accessToken,
            new
            {
                expectedRevision = 1,
                name,
                description = "Flags credit values above a workspace threshold.",
                inputs,
                condition = new
                {
                    nodeId = "threshold_check",
                    logicalOperator = (string?)null,
                    predicateOperator = "GreaterThan",
                    left = new { kind = "Input", reference = "Value", literal = (object?)null },
                    right = new { kind = "Input", reference = "Threshold", literal = (object?)null },
                    children = Array.Empty<object>(),
                },
            });
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement saved = await saveResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        saved.GetProperty("revision").GetInt32().Should().Be(2);
        saved.GetProperty("output").GetProperty("type").GetString().Should().Be("Boolean");
        saved.GetProperty("output").GetProperty("cardinality").GetString().Should().Be("Scalar");

        Dictionary<string, string> inputKeys = saved.GetProperty("inputs").EnumerateArray()
            .ToDictionary(
                input => input.GetProperty("label").GetString()!,
                input => input.GetProperty("key").GetString()!);
        Dictionary<string, object?> simulationBody = new()
        {
            ["definitionVersion"] = null,
            ["inputs"] = new Dictionary<string, object?>
            {
                [inputKeys["Value"]] = new { type = "Decimal", values = new[] { "150" } },
                [inputKeys["Threshold"]] = new { type = "Decimal", values = new[] { "100" } },
            },
            ["correlationId"] = "rules-api-test",
        };

        HttpResponseMessage draftSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/simulate",
            accessToken,
            simulationBody);
        draftSimulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement draftSimulation = await draftSimulationResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        draftSimulation.GetProperty("isMatch").GetBoolean().Should().BeTrue();

        HttpResponseMessage publishResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/publish",
            accessToken,
            new { expectedRevision = 2 });
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement published = await publishResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        published.GetProperty("status").GetString().Should().Be("Published");
        published.GetProperty("latestPublishedVersion").GetInt32().Should().Be(1);

        simulationBody["definitionVersion"] = 1;
        HttpResponseMessage versionSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/simulate",
            accessToken,
            simulationBody);
        versionSimulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement versionSimulation = await versionSimulationResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        versionSimulation.GetProperty("definitionVersion").GetInt32().Should().Be(1);
        versionSimulation.GetProperty("isMatch").GetBoolean().Should().BeTrue();

        HttpResponseMessage startRevisionResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/draft",
            accessToken,
            new { expectedRevision = 3 });
        startRevisionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement revision = await startRevisionResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        revision.GetProperty("status").GetString().Should().Be("Draft");
        revision.GetProperty("revision").GetInt32().Should().Be(4);

        HttpResponseMessage archiveResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/archive",
            accessToken,
            new { expectedRevision = 4 });
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement archived = await archiveResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        archived.GetProperty("status").GetString().Should().Be("Archived");
        archived.GetProperty("versions").GetArrayLength().Should().Be(1);

        HttpResponseMessage archivedSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/simulate",
            accessToken,
            simulationBody);
        archivedSimulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        string otherWorkspaceToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage crossWorkspaceResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rules/{definitionKey}",
            otherWorkspaceToken);
        crossWorkspaceResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<string> CreateVerifiedSessionTokenAsync(string email)
    {
        await RegisterAsync(email);
        HttpResponseMessage verifyResponse = await VerifyEmailAsync(CapturedToken(email));
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        string verifier = CreateCodeVerifier();
        string state = Guid.NewGuid().ToString("N");
        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
            ["code_challenge"] = CreateCodeChallenge(verifier),
            ["code_challenge_method"] = "S256",
            ["scope"] = "openid email profile",
            ["state"] = state,
        });

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
            ["client_id"] = "axis_spa",
            ["redirect_uri"] = "https://localhost/callback",
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
        using HttpRequestMessage request = new(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> RegisterAsync(string email)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(ValidRegisterRequest(email), options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> VerifyEmailAsync(string token) =>
        await fixture.Client.PostAsJsonAsync(
            "/api/auth/verify-email",
            new { token },
            Json,
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

    private static string UniqueEmail() => $"rules-{Guid.NewGuid():N}@example.com";
}
