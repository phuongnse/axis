using System.Net.Http.Json;
using System.Text.Json;
using Axis.Api.Tests.Helpers;
using Axis.DataModeling.Contracts.Grpc;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Api.Tests.DataModeling;

[Collection("Api")]
public sealed class DataModelingGrpcEndpointTests(ApiTestFixture fixture)
{
    private static readonly JsonSerializerOptions Json = ApiTestFixture.JsonOptions;

    [Fact]
    public async Task GetModelSummary_WhenModelExists_ReturnsExistsAndModelName()
    {
        HttpClient apiClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm1");
        string accessToken = apiClient.DefaultRequestHeaders.Authorization?.Parameter
            ?? throw new InvalidOperationException("Missing bearer token on authenticated client.");

        HttpResponseMessage createResponse = await apiClient.PostAsJsonAsync("/api/models", new
        {
            name = "Grpc Orders",
            description = "Created for gRPC lookup test",
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResponse.EnsureSuccessStatusCode();

        JsonElement createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        string modelId = createBody.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Create model response did not contain id.");

        HttpResponseMessage meResponse = await apiClient.GetAsync("/api/users/me");
        meResponse.EnsureSuccessStatusCode();
        JsonElement meBody = await meResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        string organizationId = meBody.GetProperty("org_id").GetString()
            ?? throw new InvalidOperationException("Users/me response did not contain org_id.");

        using IServiceScope scope = fixture.CreateScope();
        DataModelCatalogService.DataModelCatalogServiceClient grpcClient =
            scope.ServiceProvider.GetRequiredService<DataModelCatalogService.DataModelCatalogServiceClient>();

        Metadata headers = new()
        {
            { "authorization", $"Bearer {accessToken}" },
        };

        GetModelSummaryResponse response = await grpcClient.GetModelSummaryAsync(
            new GetModelSummaryRequest
            {
                ModelId = modelId,
                OrganizationId = organizationId,
            },
            headers: headers).ResponseAsync;

        response.Exists.Should().BeTrue();
        response.ModelName.Should().Be("Grpc Orders");
    }

    [Fact]
    public async Task GetModelSummary_WhenModelIdIsInvalid_ReturnsInvalidArgument()
    {
        HttpClient apiClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm2");
        string accessToken = apiClient.DefaultRequestHeaders.Authorization?.Parameter
            ?? throw new InvalidOperationException("Missing bearer token on authenticated client.");

        HttpResponseMessage meResponse = await apiClient.GetAsync("/api/users/me");
        meResponse.EnsureSuccessStatusCode();
        JsonElement meBody = await meResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        string organizationId = meBody.GetProperty("org_id").GetString()
            ?? throw new InvalidOperationException("Users/me response did not contain org_id.");

        using IServiceScope scope = fixture.CreateScope();
        DataModelCatalogService.DataModelCatalogServiceClient grpcClient =
            scope.ServiceProvider.GetRequiredService<DataModelCatalogService.DataModelCatalogServiceClient>();

        Metadata headers = new()
        {
            { "authorization", $"Bearer {accessToken}" },
        };

        Func<Task> act = async () => await grpcClient.GetModelSummaryAsync(
            new GetModelSummaryRequest
            {
                ModelId = "not-a-guid",
                OrganizationId = organizationId,
            },
            headers: headers).ResponseAsync;

        RpcException exception = await Assert.ThrowsAsync<RpcException>(act);
        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetModelSummary_WhenOrganizationIdIsInvalid_ReturnsInvalidArgument()
    {
        HttpClient apiClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm3");
        string accessToken = apiClient.DefaultRequestHeaders.Authorization?.Parameter
            ?? throw new InvalidOperationException("Missing bearer token on authenticated client.");

        using IServiceScope scope = fixture.CreateScope();
        DataModelCatalogService.DataModelCatalogServiceClient grpcClient =
            scope.ServiceProvider.GetRequiredService<DataModelCatalogService.DataModelCatalogServiceClient>();

        Metadata headers = new()
        {
            { "authorization", $"Bearer {accessToken}" },
        };

        Func<Task> act = async () => await grpcClient.GetModelSummaryAsync(
            new GetModelSummaryRequest
            {
                ModelId = Guid.NewGuid().ToString(),
                OrganizationId = "not-a-guid",
            },
            headers: headers).ResponseAsync;

        RpcException exception = await Assert.ThrowsAsync<RpcException>(act);
        exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
    }

    [Fact]
    public async Task GetModelSummary_WhenModelDoesNotExist_ReturnsExistsFalse()
    {
        HttpClient apiClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm4");
        string accessToken = apiClient.DefaultRequestHeaders.Authorization?.Parameter
            ?? throw new InvalidOperationException("Missing bearer token on authenticated client.");
        string organizationId = await GetOrganizationIdAsync(apiClient);

        using IServiceScope scope = fixture.CreateScope();
        DataModelCatalogService.DataModelCatalogServiceClient grpcClient =
            scope.ServiceProvider.GetRequiredService<DataModelCatalogService.DataModelCatalogServiceClient>();

        Metadata headers = new()
        {
            { "authorization", $"Bearer {accessToken}" },
        };

        GetModelSummaryResponse response = await grpcClient.GetModelSummaryAsync(
            new GetModelSummaryRequest
            {
                ModelId = Guid.NewGuid().ToString(),
                OrganizationId = organizationId,
            },
            headers: headers).ResponseAsync;

        response.Exists.Should().BeFalse();
        response.ModelName.Should().BeEmpty();
    }

    [Fact]
    public async Task GetModelSummary_WhenUnauthenticated_ReturnsUnauthenticated()
    {
        HttpClient apiClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm5");
        string organizationId = await GetOrganizationIdAsync(apiClient);

        using IServiceScope scope = fixture.CreateScope();
        DataModelCatalogService.DataModelCatalogServiceClient grpcClient =
            scope.ServiceProvider.GetRequiredService<DataModelCatalogService.DataModelCatalogServiceClient>();

        Func<Task> act = async () => await grpcClient.GetModelSummaryAsync(
            new GetModelSummaryRequest
            {
                ModelId = Guid.NewGuid().ToString(),
                OrganizationId = organizationId,
            }).ResponseAsync;

        RpcException exception = await Assert.ThrowsAsync<RpcException>(act);
        exception.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task GetModelSummary_WhenCrossTenantRequest_ReturnsExistsFalse()
    {
        HttpClient tenantAClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm6a");
        HttpClient tenantBClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm6b");

        string tenantAToken = tenantAClient.DefaultRequestHeaders.Authorization?.Parameter
            ?? throw new InvalidOperationException("Missing bearer token for tenant A.");
        string tenantBOrganizationId = await GetOrganizationIdAsync(tenantBClient);

        HttpResponseMessage createResponse = await tenantAClient.PostAsJsonAsync("/api/models", new
        {
            name = "Tenant A Internal Model",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResponse.EnsureSuccessStatusCode();
        JsonElement createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        string modelId = createBody.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Create model response did not contain id.");

        using IServiceScope scope = fixture.CreateScope();
        DataModelCatalogService.DataModelCatalogServiceClient grpcClient =
            scope.ServiceProvider.GetRequiredService<DataModelCatalogService.DataModelCatalogServiceClient>();

        Metadata headers = new()
        {
            { "authorization", $"Bearer {tenantAToken}" },
        };

        GetModelSummaryResponse response = await grpcClient.GetModelSummaryAsync(
            new GetModelSummaryRequest
            {
                ModelId = modelId,
                OrganizationId = tenantBOrganizationId,
            },
            headers: headers).ResponseAsync;

        response.Exists.Should().BeFalse();
        response.ModelName.Should().BeEmpty();
    }

    private static async Task<string> GetOrganizationIdAsync(HttpClient client)
    {
        HttpResponseMessage meResponse = await client.GetAsync("/api/users/me");
        meResponse.EnsureSuccessStatusCode();
        JsonElement meBody = await meResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        return meBody.GetProperty("org_id").GetString()
            ?? throw new InvalidOperationException("Users/me response did not contain org_id.");
    }
}
