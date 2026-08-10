using Axis.Api.Authorization;
using Axis.Api.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;

namespace Axis.Api.Tests.Authorization;

[Collection("Api")]
public sealed class ServiceProductEndpointBoundaryTests(ApiTestFixture fixture)
{
    [Fact]
    public void ServiceProductMetadata_WhenEnumerated_MarksOnlyExactPolicyGovernedRoutes()
    {
        using IServiceScope scope = fixture.CreateScope();
        EndpointDataSource endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();
        string[] actual = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ServiceProductEndpointMetadata>() is not null)
            .SelectMany(endpoint => endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods
                .Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "DELETE /api/rule-bindings/{bindingId:guid}",
            "DELETE /api/rules/{definitionKey}/active-version",
            "GET /api/business-object-definitions/",
            "GET /api/business-object-definitions/actions",
            "GET /api/business-object-definitions/{id:guid}",
            "GET /api/business-object-records/",
            "GET /api/business-object-records/{recordId:guid}",
            "GET /api/rule-bindings/{bindingId:guid}",
            "GET /api/rules/",
            "GET /api/rules/actions",
            "GET /api/rules/expression-language",
            "GET /api/rules/{definitionKey}",
            "GET /api/rules/{definitionKey}/bindings",
            "POST /api/business-object-definitions/",
            "POST /api/business-object-definitions/{id:guid}/publish",
            "POST /api/business-object-records/{objectKey}",
            "POST /api/business-object-records/{recordId:guid}/submit",
            "POST /api/rule-bindings/",
            "POST /api/rules/",
            "POST /api/rules/condition/project",
            "POST /api/rules/expression-language/guide",
            "POST /api/rules/{definitionKey}/archive",
            "POST /api/rules/{definitionKey}/draft/simulate",
            "POST /api/rules/{definitionKey}/versions",
            "POST /api/rules/{definitionKey}/versions/{version:int}/simulate",
            "PUT /api/business-object-definitions/{id:guid}/unpublished",
            "PUT /api/business-object-records/{recordId:guid}",
            "PUT /api/rule-bindings/{bindingId:guid}",
            "PUT /api/rules/{definitionKey}/active-version",
            "PUT /api/rules/{definitionKey}/draft",
        ];
        actual.Should().Equal(expected.Order(StringComparer.Ordinal));
    }
}
