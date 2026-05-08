using System.Text.Json;
using System.Text.Json.Serialization;
using Axis.WorkflowBuilder.Domain.Entities;
using Axis.WorkflowBuilder.Domain.Enums;

namespace Axis.WorkflowBuilder.Infrastructure.Persistence.Converters;

internal sealed class WorkflowStepConverter : JsonConverter<WorkflowStep>
{
    public override WorkflowStep Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetGuid();
        var name = root.GetProperty("name").GetString()!;
        var type = Enum.Parse<StepType>(root.GetProperty("type").GetString()!, ignoreCase: true);

        IReadOnlyDictionary<string, object?>? config = null;
        if (root.TryGetProperty("config", out var configEl) && configEl.ValueKind != JsonValueKind.Null)
            config = JsonSerializer.Deserialize<Dictionary<string, object?>>(configEl.GetRawText(), options);

        return WorkflowStep.Reconstitute(id, name, type, config);
    }

    public override void Write(Utf8JsonWriter writer, WorkflowStep value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);
        writer.WriteString("type", value.Type.ToString());
        writer.WritePropertyName("config");
        if (value.Config == null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, value.Config, options);
        writer.WriteEndObject();
    }
}
