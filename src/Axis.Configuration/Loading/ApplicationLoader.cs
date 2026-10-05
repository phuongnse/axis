using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Json.Schema;

namespace Axis.Configuration.Loading;

/// <summary>
/// Loads an application folder: every <c>*.json</c> file is parsed, validated against the schema
/// for its <c>kind</c> and turned into a typed resource. Every problem is reported; loading never
/// stops at the first one.
/// </summary>
public static class ApplicationLoader
{
    public const string ManifestFileName = "application.json";

    private static readonly JsonDocumentOptions _documentOptions = new() { AllowDuplicateProperties = false };

    private static readonly EvaluationOptions _schemaOptions = new()
    {
        OutputFormat = OutputFormat.List,
        IncludeApplicatorErrors = false,
    };

    private static readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        Converters = { new IntegralNumberConverter() },
    };

    public static ApplicationLoadResult Load(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var diagnostics = new List<Diagnostic>();
        ApplicationManifest? application = null;
        var entities = new List<EntityResource>();
        var resources = new List<ResourceContent>();
        var manifestFiles = new List<(string File, Guid? ResourceId)>();
        var firstFileById = new Dictionary<string, string>(StringComparer.Ordinal);
        var firstFileByKindAndName = new Dictionary<(string Kind, string Name), string>();

        // Set when the root application.json is already reported as unreadable or as the wrong kind,
        // so the folder is not also told that its manifest is missing.
        var manifestFileReported = false;

        foreach (var file in EnumerateResourceFiles(folderPath))
        {
            using var document = Parse(folderPath, file, diagnostics);
            if (document is null)
            {
                manifestFileReported |= file == ManifestFileName;
                continue;
            }

            var root = document.RootElement;
            var resourceId = ReadResourceId(root);
            var kind = ReadKind(file, root, resourceId, diagnostics);
            if (file == ManifestFileName)
            {
                manifestFileReported |= kind is null || !CheckManifestFileKind(file, kind, resourceId, diagnostics);
            }

            var schemaValid = false;
            if (kind is not null)
            {
                schemaValid = Validate(file, root, kind, resourceId, diagnostics);
                if (kind == ResourceKinds.Application)
                {
                    manifestFiles.Add((file, resourceId));
                }
            }

            CheckDuplicates(file, root, kind, resourceId, firstFileById, firstFileByKindAndName, diagnostics);

            if (schemaValid)
            {
                // Files are enumerated in path order, so the contents are too. A file that fails
                // validation is already an error, so it never becomes part of a release.
                resources.Add(new ResourceContent(file, JsonCanonicalizer.Canonicalize(root)));
                switch (kind)
                {
                    case ResourceKinds.Application when file == ManifestFileName:
                        application = root.Deserialize<ApplicationManifest>(_serializerOptions)! with { File = file };
                        break;
                    case ResourceKinds.Entity:
                        entities.Add(root.Deserialize<EntityResource>(_serializerOptions)! with { File = file });
                        break;
                }
            }
        }

        CheckManifests(manifestFiles, manifestFileReported, diagnostics);

        return new ApplicationLoadResult(application, entities, resources, DiagnosticOrder.Sort(diagnostics));
    }

    private static List<string> EnumerateResourceFiles(string folderPath)
    {
        // Hidden files and folders (such as .git) are skipped by the default enumeration options.
        var options = new EnumerationOptions { RecurseSubdirectories = true };
        return Directory.EnumerateFiles(folderPath, "*.json", options)
            .Select(path => Path.GetRelativePath(folderPath, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static JsonDocument? Parse(string folderPath, string file, List<Diagnostic> diagnostics)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(folderPath, file));
            return JsonDocument.Parse(stream, _documentOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnreadableFile,
                $"The file could not be read: {exception.Message}",
                file,
                ""));
            return null;
        }
        catch (JsonException exception)
        {
            // Syntax errors carry a zero-based position; other errors, such as a duplicate property, do not.
            var message = exception.LineNumber is { } line
                ? $"The file is not valid JSON (line {line + 1}, byte {exception.BytePositionInLine + 1})."
                : $"The file is not valid JSON: {exception.Message}";
            diagnostics.Add(new Diagnostic(DiagnosticCodes.InvalidJson, message, file, ""));
            return null;
        }
    }

    private static Guid? ReadResourceId(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), out var value)
            ? value
            : null;

    private static string? ReadKind(string file, JsonElement root, Guid? resourceId, List<Diagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kindElement))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MissingKind,
                "A resource must be a JSON object with a 'kind' property.",
                file,
                "",
                resourceId));
            return null;
        }

        if (kindElement.ValueKind != JsonValueKind.String)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MissingKind,
                "The 'kind' property must be a string.",
                file,
                "/kind",
                resourceId));
            return null;
        }

        var kind = kindElement.GetString()!;
        if (!ResourceSchemas.TryGet(kind, out _))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.UnknownKind,
                $"Unknown resource kind '{kind}'. Expected '{ResourceKinds.Application}' or '{ResourceKinds.Entity}'.",
                file,
                "/kind",
                resourceId));
            return null;
        }

        return kind;
    }

    private static bool CheckManifestFileKind(string file, string kind, Guid? resourceId, List<Diagnostic> diagnostics)
    {
        if (kind == ResourceKinds.Application)
        {
            return true;
        }

        diagnostics.Add(new Diagnostic(
            DiagnosticCodes.MisplacedManifest,
            $"'{ManifestFileName}' at the folder root is reserved for the '{ResourceKinds.Application}' manifest, but its kind is '{kind}'.",
            file,
            "/kind",
            resourceId));
        return false;
    }

    private static bool Validate(string file, JsonElement root, string kind, Guid? resourceId, List<Diagnostic> diagnostics)
    {
        ResourceSchemas.TryGet(kind, out var schema);
        var results = schema.Evaluate(root, _schemaOptions);
        if (results.IsValid)
        {
            return true;
        }

        var reported = new HashSet<(string Path, string Message)>();
        foreach (var node in results.Details ?? [])
        {
            if (node.IsValid || node.Errors is null)
            {
                continue;
            }

            var path = node.InstanceLocation.ToString();
            foreach (var (keyword, error) in node.Errors)
            {
                var message = DescribeSchemaError(node, path, keyword, error);
                if (reported.Add((path, message)))
                {
                    diagnostics.Add(new Diagnostic(DiagnosticCodes.SchemaViolation, message, file, path, resourceId));
                }
            }
        }

        return false;
    }

    private static string DescribeSchemaError(EvaluationResults node, string path, string keyword, string error)
    {
        // A property rejected by "additionalProperties": false fails against the false schema,
        // whose own message does not say which property is the problem.
        if (node.EvaluationPath.ToString().EndsWith("/additionalProperties", StringComparison.Ordinal))
        {
            var property = path[(path.LastIndexOf('/') + 1)..].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            return $"Property '{property}' is not allowed here.";
        }

        return string.IsNullOrEmpty(keyword) ? error : $"{error} ({keyword})";
    }

    private static void CheckDuplicates(
        string file,
        JsonElement root,
        string? kind,
        Guid? resourceId,
        Dictionary<string, string> firstFileById,
        Dictionary<(string Kind, string Name), string> firstFileByKindAndName,
        List<Diagnostic> diagnostics)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // Ids are compared as UUIDs when they parse, so letter case does not hide a duplicate.
        if (root.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            var id = resourceId?.ToString("D") ?? idElement.GetString()!;
            if (!firstFileById.TryAdd(id, file))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.DuplicateId,
                    $"The id '{idElement.GetString()}' is already used by '{firstFileById[id]}'.",
                    file,
                    "/id",
                    resourceId));
            }
        }

        // Names are unique per kind, ignoring letter case.
        if (kind is not null && root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
        {
            var name = nameElement.GetString()!;
            var key = (kind, name.ToUpperInvariant());
            if (!firstFileByKindAndName.TryAdd(key, file))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.DuplicateName,
                    $"The {kind} name '{name}' is already used by '{firstFileByKindAndName[key]}'.",
                    file,
                    "/name",
                    resourceId));
            }
        }
    }

    private static void CheckManifests(
        List<(string File, Guid? ResourceId)> manifestFiles,
        bool manifestFileReported,
        List<Diagnostic> diagnostics)
    {
        // Only the root application.json is the manifest. Another application resource is extra when
        // that file exists, and misplaced when it does not.
        var hasManifest = manifestFiles.Exists(manifest => manifest.File == ManifestFileName);
        if (!hasManifest && !manifestFileReported && manifestFiles.Count == 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ManifestMissing,
                $"The application folder has no '{ResourceKinds.Application}' manifest. Add '{ManifestFileName}' at the folder root.",
                ManifestFileName,
                ""));
            return;
        }

        foreach (var (file, resourceId) in manifestFiles.Where(manifest => manifest.File != ManifestFileName))
        {
            diagnostics.Add(hasManifest
                ? new Diagnostic(
                    DiagnosticCodes.MultipleManifests,
                    $"Only one '{ResourceKinds.Application}' manifest is allowed; '{ManifestFileName}' is already the manifest.",
                    file,
                    "/kind",
                    resourceId)
                : new Diagnostic(
                    DiagnosticCodes.MisplacedManifest,
                    $"The '{ResourceKinds.Application}' manifest must be '{ManifestFileName}' at the folder root, not '{file}'.",
                    file,
                    "/kind",
                    resourceId));
        }
    }
}
