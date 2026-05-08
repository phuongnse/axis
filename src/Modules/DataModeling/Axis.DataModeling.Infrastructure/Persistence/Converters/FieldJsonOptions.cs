using System.Text.Json;

namespace Axis.DataModeling.Infrastructure.Persistence.Converters;

internal static class FieldJsonOptions
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new FieldDefinitionConverter() }
    };
}
