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
        HttpResponseMessage response = await fixture.Client.GetAsync("/api/rules");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("items").EnumerateArray()
            .Select(definition => definition.GetProperty("definitionKey").GetString())
            .Should().Contain([
                RuleDefinitionKeys.Required,
                RuleDefinitionKeys.NumericRange,
                RuleDefinitionKeys.DateTimeRange,
                RuleDefinitionKeys.ChoiceSelectionCount,
            ]);
        body.GetProperty("totalCount").GetInt32().Should().Be(9);
    }

    [Fact]
    public async Task ManageWorkspaceRule_WhenAuthenticated_SavesSimulatesAndPublishesExactVersion()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string name = $"Credit threshold {Guid.NewGuid():N}"[..32];

        HttpResponseMessage schemasResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/context-schemas",
            accessToken);
        schemasResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement schemas = await schemasResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        schemas.EnumerateArray()
            .Select(schema => schema.GetProperty("contextKey").GetString())
            .Should().Contain([
                "business_objects.field.date",
                "business_objects.field.datetime",
                "business_objects.field.choice.single",
                "business_objects.field.choice.multiple",
            ]);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules",
            accessToken,
            new
            {
                name,
                description = "Flags credit values above a workspace threshold.",
                scope = "Field",
                contextKey = "business_objects.field.decimal",
                contextSchemaVersion = 1,
                outcomeKind = "Validation",
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        string definitionKey = created.GetProperty("definitionKey").GetString()!;
        created.GetProperty("status").GetString().Should().Be("Draft");
        created.GetProperty("revision").GetInt32().Should().Be(1);

        object parameters = new[]
        {
            new
            {
                key = "threshold",
                type = "Decimal",
                isRequired = true,
                allowMultiple = false,
                allowedValues = Array.Empty<string>(),
            },
        };
        object condition = new
        {
            nodeId = "credit-threshold",
            logicalOperator = (string?)null,
            predicateOperator = "GreaterThan",
            left = new
            {
                kind = "Context",
                reference = "field.value",
                literal = (object?)null,
            },
            right = new
            {
                kind = "Parameter",
                reference = "threshold",
                literal = (object?)null,
            },
            children = Array.Empty<object>(),
        };
        object outcome = new
        {
            kind = "Validation",
            violationCode = "credit.threshold.exceeded",
            severity = "Error",
            message = "Credit value exceeds the workspace threshold.",
            decision = (string?)null,
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
                scope = "Field",
                contextKey = "business_objects.field.decimal",
                contextSchemaVersion = 1,
                outcomeKind = "Validation",
                parameters,
                condition,
                outcome,
            });
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement saved = await saveResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        saved.GetProperty("revision").GetInt32().Should().Be(2);

        Dictionary<string, object?> simulationBody = new()
        {
            ["definitionVersion"] = null,
            ["parameters"] = new Dictionary<string, object?>
            {
                ["threshold"] = new { type = "Decimal", values = new[] { "100" } },
            },
            ["context"] = new Dictionary<string, object?>
            {
                ["field.value"] = new { type = "Decimal", values = new[] { "150" } },
            },
            ["correlationId"] = "rules-api-test",
        };

        HttpResponseMessage draftSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/simulate",
            accessToken,
            simulationBody);
        draftSimulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement draftSimulation = await draftSimulationResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        draftSimulation.GetProperty("isMatch").GetBoolean().Should().BeTrue();

        HttpResponseMessage publishResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/publish",
            accessToken,
            new { expectedRevision = 2 });
        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement published = await publishResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        published.GetProperty("status").GetString().Should().Be("Published");
        published.GetProperty("latestPublishedVersion").GetInt32().Should().Be(1);

        simulationBody["definitionVersion"] = 1;
        HttpResponseMessage versionSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/simulate",
            accessToken,
            simulationBody);
        versionSimulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement versionSimulation = await versionSimulationResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        versionSimulation.GetProperty("definitionVersion").GetInt32().Should().Be(1);
        versionSimulation.GetProperty("isMatch").GetBoolean().Should().BeTrue();

        HttpResponseMessage startRevisionResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/draft",
            accessToken,
            new { expectedRevision = 3 });
        startRevisionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement revision = await startRevisionResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        revision.GetProperty("status").GetString().Should().Be("Draft");
        revision.GetProperty("revision").GetInt32().Should().Be(4);

        HttpResponseMessage archiveResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/archive",
            accessToken,
            new { expectedRevision = 4 });
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement archived = await archiveResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
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

        HttpResponseMessage authorizeResponse = await fixture.Client.GetAsync(authorizeUrl);
        authorizeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        Uri redirect = authorizeResponse.Headers.Location
            ?? throw new InvalidOperationException("Authorization response did not include a redirect.");
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

        HttpResponseMessage tokenResponse = await fixture.Client.PostAsync("/connect/token", tokenRequest);
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement tokenBody = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
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
        return await fixture.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> RegisterAsync(string email)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/users/register")
        {
            Content = JsonContent.Create(ValidRegisterRequest(email), options: Json),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await fixture.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> VerifyEmailAsync(string token) =>
        await fixture.Client.PostAsJsonAsync("/api/auth/verify-email", new { token }, Json);

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
