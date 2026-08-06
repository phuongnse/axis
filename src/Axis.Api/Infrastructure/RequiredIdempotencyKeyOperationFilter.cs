using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Axis.Api.Infrastructure;

internal sealed class RequiredIdempotencyKeyMetadata
{
    public const string HeaderName = "Idempotency-Key";
    public static RequiredIdempotencyKeyMetadata Instance { get; } = new();

    private RequiredIdempotencyKeyMetadata()
    {
    }
}

internal sealed class RequiredIdempotencyKeyOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<RequiredIdempotencyKeyMetadata>()
            .Any())
        {
            return;
        }

        IList<IOpenApiParameter> parameters = operation.Parameters
            ?? throw new InvalidOperationException("The idempotency header parameter is missing from the generated operation.");
        OpenApiParameter parameter = parameters
            .OfType<OpenApiParameter>()
            .Single(candidate =>
            candidate.In == ParameterLocation.Header
            && candidate.Name == RequiredIdempotencyKeyMetadata.HeaderName);
        parameter.Required = true;
    }
}
