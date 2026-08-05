using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Axis.Api.Infrastructure;

internal sealed class ProblemDetailsSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.Type))
            return;
        OpenApiSchema concreteSchema = ResolveConcreteSchema(schema, context.SchemaRepository);
        concreteSchema.Properties ??= new Dictionary<string, IOpenApiSchema>();

        concreteSchema.Properties["code"] = new OpenApiSchema
        {
            Type = JsonSchemaType.String | JsonSchemaType.Null,
            Description = "Stable machine-readable problem code for client behavior and localization.",
        };
        concreteSchema.Properties["errorCodes"] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object | JsonSchemaType.Null,
            Description = "Optional field-level machine-readable validation codes keyed by JSON field name.",
            AdditionalProperties = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        };
    }

    private static OpenApiSchema ResolveConcreteSchema(
        IOpenApiSchema schema,
        SchemaRepository repository)
    {
        if (schema is OpenApiSchema concreteSchema)
            return concreteSchema;
        if (schema is OpenApiSchemaReference reference &&
            reference.Reference.Id is string schemaId &&
            repository.Schemas.TryGetValue(schemaId, out IOpenApiSchema? registeredSchema) &&
            registeredSchema is OpenApiSchema registeredConcreteSchema)
        {
            return registeredConcreteSchema;
        }

        throw new InvalidOperationException("Problem details schemas must resolve to mutable OpenAPI schemas.");
    }
}
