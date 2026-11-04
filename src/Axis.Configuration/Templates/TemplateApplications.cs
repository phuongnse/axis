using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Templates;

/// <summary>
/// Makes the files of a new application from an installed template. Studio's create from a template and the publisher's create job
/// share it, so both give the copy the same names and new ids.
/// </summary>
public static class TemplateApplications
{
    private static readonly JsonWriterOptions _writer = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The <c>name</c> of the template's application manifest, or null when it has none.</summary>
    public static string? ApplicationName(InstalledTemplate template)
    {
        foreach (var file in template.Files)
        {
            if (JsonOrNull(file.Content) is { } resource
                && Text(resource, "kind") == ResourceKinds.Application
                && Text(resource, "name") is { } name)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// The files of a copy of <paramref name="template"/> named <paramref name="name"/>. A file that is a JSON object
    /// with a UUID <c>id</c> gets a new id, the manifest gets the new name and the site gets the new path. Any other
    /// file is copied byte for byte, so the copy has the template's diagnostics.
    /// </summary>
    public static IReadOnlyList<(string File, byte[] Bytes)> FromTemplate(InstalledTemplate template, string name)
    {
        var files = new List<(string File, byte[] Bytes)>(template.Files.Count);
        foreach (var file in template.Files)
        {
            var original = Encoding.UTF8.GetBytes(file.Content);
            files.Add((file.Path, Copy(file.Content, name) ?? original));
        }

        return files;
    }

    /// <summary>The kebab-case form of an application name, which is its folder and its site path.</summary>
    // A hyphen goes before a capital that follows a lower-case letter or a digit, or before the last capital
    // of an acronym that a lower-case letter follows. Everything is then lower-cased.
    public static string Kebab(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (index > 0 && char.IsUpper(character)
                && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])
                    || (char.IsUpper(name[index - 1]) && index + 1 < name.Length && char.IsLower(name[index + 1]))))
            {
                builder.Append('-');
            }

            builder.Append(character);
        }

        return builder.ToString().ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>The bytes of a resource file as Studio writes it: indented with two spaces, <c>\n</c> line ends and a final line end.</summary>
    public static byte[] Serialize(JsonObject content)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, _writer))
        {
            content.WriteTo(writer);
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>A new resource id.</summary>
    public static string NewId() => Guid.NewGuid().ToString("D");

    private static byte[]? Copy(string content, string name)
    {
        var resource = JsonOrNull(content);
        if (resource is null
            || resource["id"] is not JsonValue id
            || !id.TryGetValue<string>(out var text)
            || text.Length != 36
            || !Guid.TryParseExact(text, "D", out _))
        {
            return null;
        }

        resource["id"] = NewId();
        var kind = Text(resource, "kind");
        if (kind == ResourceKinds.Application)
        {
            resource["name"] = name;
        }
        else if (kind == ResourceKinds.Site)
        {
            resource["path"] = Kebab(name);
        }

        return Serialize(resource);
    }

    private static JsonObject? JsonOrNull(string content)
    {
        try
        {
            return JsonNode.Parse(content) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonObject resource, string property) =>
        resource[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
