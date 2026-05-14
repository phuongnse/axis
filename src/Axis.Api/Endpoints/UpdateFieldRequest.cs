using System.Text.Json;
using Axis.DataModeling.Domain.Enums;

namespace Axis.Api.Endpoints;

public record UpdateFieldRequest(
    FieldType Type,
    string Label,
    string? HelpText,
    bool IsRequired,
    JsonElement Config);
