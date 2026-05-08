using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.FormBuilder.Infrastructure.Persistence.Converters;

internal static class FormJsonOptions
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new FormFieldConverter(), new JsonStringEnumConverter() }
    };
}
