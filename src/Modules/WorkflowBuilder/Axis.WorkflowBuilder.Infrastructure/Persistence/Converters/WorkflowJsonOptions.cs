using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence.Converters;

internal static class WorkflowJsonOptions
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new WorkflowStepConverter(), new JsonStringEnumConverter() }
    };
}
