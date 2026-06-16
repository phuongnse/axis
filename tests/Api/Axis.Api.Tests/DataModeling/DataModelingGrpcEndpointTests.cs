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
        string accessToken = GetBearerToken(apiClient);

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

        DataModelCatalogService.DataModelCatalogServiceClient grpcClient = ResolveGrpcClient(out IServiceScope scope);
        using (scope)
        {
            GetModelSummaryResponse response = await grpcClient.GetModelSummaryAsync(
                new GetModelSummaryRequest { ModelId = modelId },
                headers: BuildAuthHeaders(accessToken)).ResponseAsync;

            response.Exists.Should().BeTrue();
            response.ModelName.Should().Be("Grpc Orders");
        }
    }

    [Fact]
    public async Task GetModelSummary_WhenModelIdIsInvalid_ReturnsInvalidArgument()
    {
        HttpClient apiClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm2");
        string accessToken = GetBearerToken(apiClient);

        DataModelCatalogService.DataModelCatalogServiceClient grpcClient = ResolveGrpcClient(out IServiceScope scope);
        using (scope)
        {
            Func<Task> act = async () => await grpcClient.GetModelSummaryAsync(
                new GetModelSummaryRequest { ModelId = "not-a-guid" },
                headers: BuildAuthHeaders(accessToken)).ResponseAsync;

            RpcException exception = await Assert.ThrowsAsync<RpcException>(act);
            exception.StatusCode.Should().Be(StatusCode.InvalidArgument);
        }
    }

    [Fact]
    public async Task GetModelSummary_WhenModelDoesNotExist_ReturnsExistsFalse()
    {
        HttpClient apiClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm4");
        string accessToken = GetBearerToken(apiClient);

        DataModelCatalogService.DataModelCatalogServiceClient grpcClient = ResolveGrpcClient(out IServiceScope scope);
        using (scope)
        {
            GetModelSummaryResponse response = await grpcClient.GetModelSummaryAsync(
                new GetModelSummaryRequest { ModelId = Guid.NewGuid().ToString() },
                headers: BuildAuthHeaders(accessToken)).ResponseAsync;

            response.Exists.Should().BeFalse();
            response.ModelName.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task GetModelSummary_WhenUnauthenticated_ReturnsUnauthenticated()
    {
        DataModelCatalogService.DataModelCatalogServiceClient grpcClient = ResolveGrpcClient(out IServiceScope scope);
        using (scope)
        {
            Func<Task> act = async () => await grpcClient.GetModelSummaryAsync(
                new GetModelSummaryRequest { ModelId = Guid.NewGuid().ToString() }).ResponseAsync;

            RpcException exception = await Assert.ThrowsAsync<RpcException>(act);
            exception.StatusCode.Should().Be(StatusCode.Unauthenticated);
        }
    }

    [Fact]
    public async Task GetModelSummary_WhenCrossWorkspaceRequest_ReturnsExistsFalse()
    {
        HttpClient workspaceAClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm6a");
        HttpClient workspaceBClient = await AuthHelper.CreateAdminClientAsync(fixture, "grpcdm6b");
        string workspaceBToken = GetBearerToken(workspaceBClient);

        HttpResponseMessage createResponse = await workspaceAClient.PostAsJsonAsync("/api/models", new
        {
            name = "Workspace A Internal Model",
            description = (string?)null,
            icon = (string?)null,
            color = (string?)null,
        }, Json);
        createResponse.EnsureSuccessStatusCode();
        JsonElement createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        string workspaceAModelId = createBody.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Create model response did not contain id.");

        DataModelCatalogService.DataModelCatalogServiceClient grpcClient = ResolveGrpcClient(out IServiceScope scope);
        using (scope)
        {
            GetModelSummaryResponse response = await grpcClient.GetModelSummaryAsync(
                new GetModelSummaryRequest { ModelId = workspaceAModelId },
                headers: BuildAuthHeaders(workspaceBToken)).ResponseAsync;

            response.Exists.Should().BeFalse();
            response.ModelName.Should().BeEmpty();
        }
    }

    private DataModelCatalogService.DataModelCatalogServiceClient ResolveGrpcClient(out IServiceScope scope)
    {
        scope = fixture.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DataModelCatalogService.DataModelCatalogServiceClient>();
    }

    private static string GetBearerToken(HttpClient client)
    {
        return client.DefaultRequestHeaders.Authorization?.Parameter
            ?? throw new InvalidOperationException("Missing bearer token on authenticated client.");
    }

    private static Metadata BuildAuthHeaders(string accessToken)
    {
        return new Metadata { { "authorization", $"Bearer {accessToken}" } };
    }
}
