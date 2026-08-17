using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Identity.Domain.Legal;
using Axis.Rules.Application;
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
        using HttpClient anonymousClient = fixture.CreateAnonymousClient();
        HttpResponseMessage response = await anonymousClient.GetAsync("/api/rules", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RuleAuthoring_WhenNonBuilder_ReturnsForbidden()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Denied,
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/actions",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules",
            accessToken,
            new { name = "Denied Rule", description = "Product Builder authority is required." });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        HttpResponseMessage authoringResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules/authoring/complete",
            accessToken,
            new
            {
                text = "gre",
                cursor = 3,
                inputs = Array.Empty<object>(),
                expressionLanguageVersion = 1,
            });
        authoringResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RuleActions_WhenBuilderAuthorizationUnavailable_ReturnsServiceUnavailable()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Unavailable,
            TestContext.Current.CancellationToken);

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/actions",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(
            Json,
            TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be(RulesProblemCodes.AuthorizationUnavailable);
    }

    [Fact]
    public async Task RuleAuthoring_WhenBuilderHasNoProductGrant_AllowsProjectionAndCreate()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        await fixture.SetProductAuthorizationTestDecisionAsync(
            _ => ProductAuthorizationDecision.Denied,
            TestContext.Current.CancellationToken);

        HttpResponseMessage actionsResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules/actions",
            accessToken);
        actionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement actions = await actionsResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        actions.GetProperty("canStartCreate").GetBoolean().Should().BeTrue();

        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules",
            accessToken,
            new { name = $"Builder Rule {Guid.NewGuid():N}", description = "Builder authority is sufficient." });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task RuleBindingDetail_WhenAnonymous_ReturnsUnauthorized()
    {
        using HttpClient anonymousClient = fixture.CreateAnonymousClient();
        HttpResponseMessage response = await anonymousClient.GetAsync(
            $"/api/rule-bindings/{Guid.NewGuid():D}",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RuleBindingDetail_WhenOwned_ReturnsFullBinding()
    {
        string ownerToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            ownerToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-1",
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
                priority = 4,
                enabled = false,
                failureBehavior = "FailOpen",
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        string bindingId = created.GetProperty("id").GetString()!;

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rule-bindings/{bindingId}",
            ownerToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement binding = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        binding.GetProperty("id").GetString().Should().Be(bindingId);
        binding.GetProperty("workspaceId").GetString().Should().NotBeNullOrWhiteSpace();
        binding.GetProperty("definitionKey").GetString().Should().Be(RuleDefinitionKeys.Required);
        binding.GetProperty("definitionVersion").GetInt32().Should().Be(1);
        binding.GetProperty("targetType").GetString().Should().Be("neutral-consumer");
        binding.GetProperty("targetId").GetString().Should().Be("consumer-1");
        binding.GetProperty("useCaseOrTrigger").GetString().Should().Be("validate");
        JsonElement mapping = binding.GetProperty("inputMappings").GetProperty("value");
        mapping.GetProperty("kind").GetString().Should().Be("Literal");
        mapping.GetProperty("contextKey").ValueKind.Should().Be(JsonValueKind.Null);
        mapping.GetProperty("literalValues").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("Approved");
        binding.GetProperty("priority").GetInt32().Should().Be(4);
        binding.GetProperty("enabled").GetBoolean().Should().BeFalse();
        binding.GetProperty("failureBehavior").GetString().Should().Be("FailOpen");
        binding.GetProperty("revision").GetInt32().Should().Be(1);
        binding.GetProperty("createdAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        binding.GetProperty("updatedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RuleBindingAuthorization_WhenRetargetDeniedOrUnavailable_DoesNotMutate()
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
        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            accessToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "authorization-stable",
                useCaseOrTrigger = "validate",
                inputMappings = mappings,
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        Guid bindingId = created.GetProperty("id").GetGuid();
        int revision = created.GetProperty("revision").GetInt32();

        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Denied,
            TestContext.Current.CancellationToken);
        HttpResponseMessage denied = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/rule-bindings/{bindingId:D}",
            accessToken,
            new
            {
                expectedRevision = revision,
                definitionKey = RuleDefinitionKeys.TextLength,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "denied-retarget",
                useCaseOrTrigger = "validate",
                inputMappings = mappings,
            });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Unavailable,
            TestContext.Current.CancellationToken);
        HttpResponseMessage unavailable = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/rule-bindings/{bindingId:D}",
            accessToken,
            new
            {
                expectedRevision = revision,
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "unavailable-update",
                useCaseOrTrigger = "validate",
                inputMappings = mappings,
            });
        unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        await fixture.SetWorkspaceProductBuilderTestDecisionAsync(
            WorkspaceProductBuilderDecision.Allowed,
            TestContext.Current.CancellationToken);
        HttpResponseMessage readResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rule-bindings/{bindingId:D}",
            accessToken);
        readResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement unchanged = await readResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        unchanged.GetProperty("definitionKey").GetString().Should().Be(RuleDefinitionKeys.Required);
        unchanged.GetProperty("targetId").GetString().Should().Be("authorization-stable");
        unchanged.GetProperty("revision").GetInt32().Should().Be(revision);
    }

    [Fact]
    public async Task RuleBindingDetail_WhenAccessedFromAnotherWorkspace_ReturnsNotFound()
    {
        string ownerToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            ownerToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-1",
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
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);

        string otherWorkspaceToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rule-bindings/{created.GetProperty("id").GetString()}",
            otherWorkspaceToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RuleBindingEvaluation_AuthenticatedRequest_DerivesWorkspaceAndCorrelation()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            accessToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-evaluate",
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
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement binding = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);

        using HttpRequestMessage request = new(HttpMethod.Post, $"/api/rule-bindings/{binding.GetProperty("id").GetString()}/evaluate")
        {
            Content = JsonContent.Create(new { context = new { values = new { } } }, options: Json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Correlation-Id", "rules-binding-evaluate");
        HttpResponseMessage response = await fixture.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement evaluation = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        evaluation.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        evaluation.GetProperty("correlationId").GetString().Should().Be("rules-binding-evaluate");
    }

    [Fact]
    public async Task RuleBindingDetail_WhenMissing_ReturnsNotFound()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rule-bindings/{Guid.NewGuid():D}",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
            accessToken,
            new { expectedRevision = updatedBinding.GetProperty("revision").GetInt32() });
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
    public async Task RuleBindingDelete_WhenExpectedRevisionIsStale_ReturnsConflictAndRetainsBinding()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());
        HttpResponseMessage createResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            accessToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-stale-delete",
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
            });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        JsonElement binding = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        string bindingId = binding.GetProperty("id").GetString()!;

        HttpResponseMessage deleteResponse = await SendWithBearerAsync(
            HttpMethod.Delete,
            $"/api/rule-bindings/{bindingId}",
            accessToken,
            new { expectedRevision = binding.GetProperty("revision").GetInt32() + 1 });

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonElement problem = await deleteResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be("common.conflict");

        HttpResponseMessage retainedResponse = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rule-bindings/{bindingId}",
            accessToken);
        retainedResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RuleBindingCreate_WhenMappingValueIsNull_ReturnsBoundedBadRequest()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rule-bindings",
            accessToken,
            new
            {
                definitionKey = RuleDefinitionKeys.Required,
                definitionVersion = 1,
                targetType = "neutral-consumer",
                targetId = "consumer-null-mapping",
                useCaseOrTrigger = "validate",
                inputMappings = new Dictionary<string, object?> { ["value"] = null },
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        problem.GetProperty("code").GetString().Should().Be("rules.definition_invalid");
        problem.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ListRuleDefinitions_WhenAuthenticated_ReturnsBuiltInCatalog()
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
    public async Task ListRuleDefinitions_WhenNameSortIsExplicit_SortsLocalizedWholeCatalogBeforePaging()
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            "/api/rules?page=1&pageSize=6&language=vi&sortBy=Name&sortDirection=Ascending",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        body.GetProperty("items").EnumerateArray()
            .Select(definition => definition.GetProperty("definitionKey").GetString())
            .Should().Equal(
                RuleDefinitionKeys.Required,
                RuleDefinitionKeys.DateRange,
                RuleDefinitionKeys.DateTimeRange,
                RuleDefinitionKeys.NumericRange,
                RuleDefinitionKeys.TextPattern,
                RuleDefinitionKeys.ChoiceSelectionCount);
    }

    [Theory]
    [InlineData("Origin")]
    [InlineData("Status")]
    public async Task ListRuleDefinitions_WhenScalarEnumSortIsExplicit_AcceptsThePublicContract(
        string sortBy)
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rules?page=1&pageSize=20&sortBy={sortBy}&sortDirection=Ascending",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("sortBy=Name")]
    [InlineData("sortDirection=Ascending")]
    [InlineData("sortBy=999&sortDirection=Ascending")]
    public async Task ListRuleDefinitions_WhenSortParametersAreIncompleteOrInvalid_ReturnsBadRequest(string sortQuery)
    {
        string accessToken = await CreateVerifiedSessionTokenAsync(UniqueEmail());

        HttpResponseMessage response = await SendWithBearerAsync(
            HttpMethod.Get,
            $"/api/rules?page=1&pageSize=20&{sortQuery}",
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
    public async Task RuleAuthoringContracts_WhenAuthenticated_ReturnCapabilitiesAndExecutableBuiltInDetail()
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

        HttpResponseMessage projectResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules/authoring/project",
            accessToken,
            new
            {
                source = new { text = "greaterThan(input(\"value\"), decimal(1))", ast = (object?)null },
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
                },
                expressionLanguageVersion = 1,
                language = "en",
            });
        projectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement projection = await projectResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        projection.GetProperty("isValid").GetBoolean().Should().BeTrue();

        HttpResponseMessage completionResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/rules/authoring/complete",
            accessToken,
            new
            {
                text = "gre",
                cursor = 3,
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
                },
                expressionLanguageVersion = 1,
            });
        completionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement[] completions = (await completionResponse.Content.ReadFromJsonAsync<JsonElement[]>(Json, TestContext.Current.CancellationToken))!;
        completions.Select(completion => completion.GetProperty("label").GetString()).Should().Contain("greaterThan");

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
        detail.GetProperty("origin").GetString().Should().Be("BuiltIn");
        detail.GetProperty("status").GetString().Should().Be("Active");
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
    }

    [Fact]
    public async Task ManageWorkspaceRule_WhenAuthenticated_SavesSimulatesVersionsAndActivatesExactVersion()
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
                condition = new
                {
                    nodeId = "threshold_check",
                    logicalOperator = (string?)null,
                    predicateOperator = "GreaterThan",
                    left = new { kind = "Input", reference = "value", literal = (object?)null },
                    right = new { kind = "Input", reference = "threshold", literal = (object?)null },
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
            .Should().Be("value");

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
                    left = new { kind = "Input", reference = "value", literal = (object?)null },
                    right = new { kind = "Input", reference = "threshold", literal = (object?)null },
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
        inputKeys["Value"].Should().Be("value");
        inputKeys["Threshold"].Should().Be("threshold");
        object simulationBody = new
        {
            inputs = new Dictionary<string, object?>
            {
                [inputKeys["Value"]] = new { type = "Decimal", values = new[] { "150" } },
                [inputKeys["Threshold"]] = new { type = "Decimal", values = new[] { "100" } },
            },
        };

        HttpResponseMessage draftSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/draft/simulate",
            accessToken,
            simulationBody);
        draftSimulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement draftSimulation = await draftSimulationResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        draftSimulation.GetProperty("isMatch").GetBoolean().Should().BeTrue();

        HttpResponseMessage createVersionResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/versions",
            accessToken,
            new { expectedRevision = 2 });
        createVersionResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement inactive = await createVersionResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        inactive.GetProperty("status").GetString().Should().Be("Inactive");
        inactive.GetProperty("latestVersion").GetInt32().Should().Be(1);

        HttpResponseMessage versionSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/versions/1/simulate",
            accessToken,
            simulationBody);
        versionSimulationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement versionSimulation = await versionSimulationResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        versionSimulation.GetProperty("definitionVersion").GetInt32().Should().Be(1);
        versionSimulation.GetProperty("isMatch").GetBoolean().Should().BeTrue();

        HttpResponseMessage activateResponse = await SendWithBearerAsync(
            HttpMethod.Put,
            $"/api/rules/{definitionKey}/active-version",
            accessToken,
            new { version = 1, expectedRevision = 3 });
        activateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement active = await activateResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        active.GetProperty("status").GetString().Should().Be("Active");
        active.GetProperty("activeVersion").GetInt32().Should().Be(1);

        HttpResponseMessage deactivateResponse = await SendWithBearerAsync(
            HttpMethod.Delete,
            $"/api/rules/{definitionKey}/active-version",
            accessToken,
            new { expectedRevision = 4 });
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement deactivated = await deactivateResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        deactivated.GetProperty("status").GetString().Should().Be("Inactive");

        HttpResponseMessage archiveResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/archive",
            accessToken,
            new { expectedRevision = 5 });
        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement archived = await archiveResponse.Content.ReadFromJsonAsync<JsonElement>(Json, TestContext.Current.CancellationToken);
        archived.GetProperty("status").GetString().Should().Be("Archived");
        archived.GetProperty("versions").GetArrayLength().Should().Be(1);

        HttpResponseMessage archivedSimulationResponse = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/rules/{definitionKey}/versions/1/simulate",
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
        await fixture.EnableProductAuthorizationTestAccessAsync(
            TestContext.Current.CancellationToken);

        string verifier = CreateCodeVerifier();
        string state = Guid.NewGuid().ToString("N");
        string authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = "axis_mcp",
            ["redirect_uri"] = "http://127.0.0.1:48123/callback",
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
