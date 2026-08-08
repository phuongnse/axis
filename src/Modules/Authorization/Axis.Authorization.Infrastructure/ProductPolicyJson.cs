using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.Authorization.Infrastructure;

internal static class ProductPolicyJson
{
    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
