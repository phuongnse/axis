using System.Text.Json;
using Axis.DataModeling.Domain.Enums;

namespace Axis.Api.Endpoints;

public record AddDataClassFieldRequest(
    string Name,
    string Label,
    FieldType Type,
    bool IsRequired,
    JsonElement Config);
