using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Identity.Domain.Legal;
using Axis.Objects.Application;
using Axis.Objects.Domain.Aggregates;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;

namespace Axis.Api.Tests.Objects;

[Collection("Api")]
public sealed class ObjectDefinitionEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;
    private const string Password = "maple river sunrise";

    [Fact]
    public async Task ObjectDefinitionEndpoints_WhenAnonymous_ReturnUnauthorized()
    {
        HttpResponseMessage response = await fixture.Client.GetAsync("/api/object-definitions");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DefineBusinessObject_WhenAuthenticated_CreatesSavesPublishesGetsAndListsDefinition()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("customer");
        string objectName = ObjectNameFromKey(objectKey);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/object-definitions",
            accessToken,
            new { name = objectName });

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        createResponse.Headers.Location!.ToString().Should().StartWith($"/api/object-definitions/");
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        Guid definitionId = created.GetProperty("id").GetGuid();
        created.GetProperty("status").GetString().Should().Be(nameof(ObjectDefinitionStatus.Draft));
        created.GetProperty("objectKey").GetString().Should().Be(objectKey);
        created.GetProperty("draftVersion").GetInt32().Should().Be(1);

        HttpResponseMessage saveResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/object-definitions/{definitionId}/draft",
            accessToken,
            new
            {
                expectedDraftVersion = 1,
                name = $"{objectName} renamed",
                fields = new object[]
                {
                    new
                    {
                        fieldKey = "name",
                        label = "Name",
                    },
                    new
                    {
                        fieldKey = "status",
                        label = "Status",
                    },
                },
            });

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement saved = await saveResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        saved.GetProperty("objectKey").GetString().Should().Be(objectKey);
        saved.GetProperty("draftVersion").GetInt32().Should().Be(2);
        saved.GetProperty("fields").GetArrayLength().Should().Be(2);

        HttpResponseMessage publishResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/object-definitions/{definitionId}/publish",
            accessToken,
            new { expectedDraftVersion = 2 });

        publishResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement published = await publishResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        published.GetProperty("status").GetString().Should().Be(nameof(ObjectDefinitionStatus.Published));
        published.GetProperty("latestPublishedVersionNumber").GetInt32().Should().Be(1);
        published.GetProperty("latestPublishedVersion")
            .GetProperty("fields")
            .GetArrayLength()
            .Should().Be(2);

        HttpResponseMessage getResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/object-definitions/{definitionId}",
            accessToken);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement detail = await getResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        detail.GetProperty("objectKey").GetString().Should().Be(objectKey);

        HttpResponseMessage listResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/object-definitions?page=1&pageSize=20",
            accessToken);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement list = await listResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        list.GetProperty("totalCount").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        list.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .Should().Contain(definitionId);
    }

    [Fact]
    public async Task SaveDraft_WhenExpectedVersionIsStale_ReturnsConflictProblemCode()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("invoice");
        Guid definitionId = await CreateDraftAsync(accessToken, ObjectNameFromKey(objectKey));

        HttpResponseMessage firstSave = await SaveWithOneFieldAsync(
            accessToken,
            definitionId,
            expectedDraftVersion: 1,
            fieldKey: "number");
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage staleSave = await SaveWithOneFieldAsync(
            accessToken,
            definitionId,
            expectedDraftVersion: 1,
            fieldKey: "total");

        staleSave.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ApiProblem problem = await ReadProblemAsync(staleSave);
        problem.Code.Should().Be(ObjectsProblemCodes.ObjectDefinitionConflict);
        problem.Type.Should().Be(ProblemType(ObjectsProblemCodes.ObjectDefinitionConflict));
    }

    [Fact]
    public async Task CreateDraft_WhenObjectKeyAlreadyExistsInWorkspace_ReturnsConflictProblemCode()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("account");
        string objectName = ObjectNameFromKey(objectKey);
        await CreateDraftAsync(accessToken, objectName);

        HttpResponseMessage duplicateResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/object-definitions",
            accessToken,
            new { name = objectName });

        duplicateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ApiProblem problem = await ReadProblemAsync(duplicateResponse);
        problem.Code.Should().Be(ObjectsProblemCodes.ObjectKeyAlreadyExists);
        problem.Type.Should().Be(ProblemType(ObjectsProblemCodes.ObjectKeyAlreadyExists));
    }

    [Fact]
    public async Task GetObjectDefinition_WhenDefinitionBelongsToAnotherWorkspace_ReturnsNotFound()
    {
        string ownerToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string otherToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        Guid definitionId = await CreateDraftAsync(
            ownerToken,
            ObjectNameFromKey(UniqueKey("private_object")));

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/object-definitions/{definitionId}",
            otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ApiProblem problem = await ReadProblemAsync(response);
        problem.Code.Should().Be(ObjectsProblemCodes.ObjectDefinitionNotFound);
    }

    [Fact]
    public async Task CreateDraft_WhenClientSendsObjectKey_IgnoresClientValueAndReturnsDerivedKey()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        string objectKey = UniqueKey("client_owned");

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/object-definitions",
            accessToken,
            new { name = ObjectNameFromKey(objectKey), objectKey = "client_value" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        body.GetProperty("objectKey").GetString().Should().Be(objectKey);
    }

    private async Task<Guid> CreateDraftAsync(string accessToken, string objectName)
    {
        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/object-definitions",
            accessToken,
            new { name = objectName });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetGuid();
    }

    private async Task<HttpResponseMessage> SaveWithOneFieldAsync(
        string accessToken,
        Guid definitionId,
        int expectedDraftVersion,
        string fieldKey) =>
        await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/object-definitions/{definitionId}/draft",
            accessToken,
            new
            {
                expectedDraftVersion,
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

        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", authorizeQuery);
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
        using HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);

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

    private static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
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
