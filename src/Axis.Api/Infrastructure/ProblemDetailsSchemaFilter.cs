using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Axis.Api.Infrastructure;

internal sealed class ProblemDetailsSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.Type))
            return;

        schema.Properties["code"] = new OpenApiSchema
        {
            Type = "string",
            Nullable = true,
            Description = "Stable machine-readable problem code for client behavior and localization.",
        };
        schema.Properties["errorCodes"] = new OpenApiSchema
        {
            Type = "object",
            Nullable = true,
            Description = "Optional field-level machine-readable validation codes keyed by JSON field name.",
            AdditionalProperties = new OpenApiSchema
            {
                Type = "array",
                Items = new OpenApiSchema { Type = "string" },
            },
        };
    }
}
