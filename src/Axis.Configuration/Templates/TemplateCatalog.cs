using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Json.Schema;

namespace Axis.Configuration.Templates;

/// <summary>
/// The application templates installed under <c>Templates:Path</c>, one folder per template id and version:
/// <c>&lt;id&gt;/&lt;version&gt;/template.json</c>. A version whose manifest is valid, matches its folder
/// and holds exactly one site is loaded with every <c>*.json</c> file under its folder except the manifest.
/// A version with any problem is left out and the problem names its <c>&lt;id&gt;/&lt;version&gt;</c> folder.
/// Loading never stops at the first broken version.
/// </summary>
public sealed class TemplateCatalog
{
    public const string PathSetting = "Templates:Path";
    public const string ManifestFileName = "template.json";

    private static readonly JsonDocumentOptions _documentOptions = new() { AllowDuplicateProperties = false };

    private static readonly JsonSchema _schema = LoadSchema();

    private TemplateCatalog(IReadOnlyList<InstalledTemplate> templates, IReadOnlyList<TemplateProblem> problems)
    {
        Problems = problems;
        Newest = [.. templates
            .GroupBy(template => template.Manifest.Id, StringComparer.Ordinal)
            .Select(group => group.MaxBy(template => template.Manifest.Version, Comparer<string>.Create(PackageVersions.Compare))!)
            .OrderBy(template => template.Manifest.Id, StringComparer.Ordinal)];
    }

    public static TemplateCatalog Empty { get; } = new([], []);

    /// <summary>The newest loaded version of each template, sorted by id.</summary>
    public IReadOnlyList<InstalledTemplate> Newest { get; }

    /// <summary>The problems of the template versions that were left out.</summary>
    public IReadOnlyList<TemplateProblem> Problems { get; }

    /// <summary>
    /// Finds and loads the templates under <paramref name="root"/>. A null or empty root gives an
    /// empty catalog.
    /// </summary>
    /// <exception cref="InvalidOperationException">The root folder does not exist or cannot be listed.</exception>
    public static TemplateCatalog Load(string? root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return Empty;
        }

        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"The template folder '{root}' set by '{PathSetting}' does not exist.");
        }

        var templates = new List<InstalledTemplate>();
        var problems = new List<TemplateProblem>();
        foreach (var idFolder in ListFolders(root) ?? throw new InvalidOperationException(
            $"The template folder '{root}' set by '{PathSetting}' cannot be listed."))
        {
            var idName = Path.GetFileName(idFolder);
            var versionFolders = ListFolders(idFolder);
            if (versionFolders is null)
            {
                problems.Add(new TemplateProblem(idName, "The folder of the template cannot be listed."));
                continue;
            }

            foreach (var versionFolder in versionFolders)
            {
                var folder = $"{idName}/{Path.GetFileName(versionFolder)}";
                var template = LoadTemplate(idName, versionFolder, out var problem);
                if (template is not null)
                {
                    templates.Add(template);
                }
                else
                {
                    problems.Add(new TemplateProblem(folder, problem!));
                }
            }
        }

        return new TemplateCatalog(templates, problems);
    }

    private static InstalledTemplate? LoadTemplate(string idName, string versionFolder, out string? problem)
    {
        var versionName = Path.GetFileName(versionFolder);
        problem = null;

        var manifestPath = Path.Combine(versionFolder, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            problem = $"The manifest '{ManifestFileName}' is missing.";
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception message names the absolute path, so it is never passed on.
            problem = "The manifest could not be read.";
            return null;
        }

        var manifest = ReadManifest(json, out problem);
        if (manifest is null)
        {
            return null;
        }

        if (!string.Equals(manifest.Id, idName, StringComparison.Ordinal))
        {
            problem = $"The id '{manifest.Id}' differs from its folder name '{idName}'.";
            return null;
        }

        if (!string.Equals(manifest.Version, versionName, StringComparison.Ordinal))
        {
            problem = $"The version '{manifest.Version}' differs from its folder name '{versionName}'.";
            return null;
        }

        var files = ReadFiles(versionFolder, out problem);
        if (files is null)
        {
            return null;
        }

        var sites = files.Count(file => IsSite(file.Content));
        if (sites != 1)
        {
            problem = $"The template must hold exactly one site, but it holds {sites}.";
            return null;
        }

        return new InstalledTemplate(manifest, files);
    }

    private static TemplateManifest? ReadManifest(string json, out string? problem)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, _documentOptions);
        }
        catch (JsonException exception)
        {
            problem = exception.LineNumber is { } line
                ? $"The manifest is not valid JSON (line {line + 1}, byte {exception.BytePositionInLine + 1})."
                : "The manifest is not valid JSON: a property appears more than once.";
            return null;
        }

        using (document)
        {
            var diagnostics = new List<Diagnostic>();
            if (!SchemaDiagnostics.Validate(_schema, document.RootElement, "template", ManifestFileName, null, diagnostics))
            {
                problem = "The manifest is invalid: " + string.Join(
                    " ", diagnostics.Select(diagnostic => $"{diagnostic.Message} (at '{diagnostic.Path}')"));
                return null;
            }

            problem = null;
            var root = document.RootElement;
            return new TemplateManifest(
                root.GetProperty("id").GetString()!,
                root.GetProperty("version").GetString()!,
                ReadText(root.GetProperty("title")),
                ReadText(root.GetProperty("description")));
        }
    }

    private static TemplateText ReadText(JsonElement element) =>
        new(element.GetProperty("en").GetString()!, element.GetProperty("vi").GetString()!);

    // A file that is not JSON, or whose root is not an object, is no site. The compile reports it.
    private static bool IsSite(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == "site";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Reads every <c>*.json</c> file under the version folder except the manifest, or returns null with a problem when one cannot be listed or read.</summary>
    private static List<ResourceContent>? ReadFiles(string versionFolder, out string? problem)
    {
        // The same options as the application loader: hidden files are skipped and an inaccessible folder fails.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false };
        List<string> paths;
        try
        {
            // The enumeration is lazy, so it must run to the end inside the try.
            paths = [.. Directory.EnumerateFiles(versionFolder, "*.json", options)
                .Select(path => Path.GetRelativePath(versionFolder, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(path => !string.Equals(path, ManifestFileName, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = "The files of the template version cannot be listed.";
            return null;
        }

        var files = new List<ResourceContent>(paths.Count);
        foreach (var path in paths)
        {
            try
            {
                files.Add(new ResourceContent(path, File.ReadAllText(Path.Combine(versionFolder, path))));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problem = $"The file '{path}' could not be read.";
                return null;
            }
        }

        problem = null;
        return files;
    }

    private static string[]? ListFolders(string folder)
    {
        try
        {
            var folders = Directory.GetDirectories(folder);
            Array.Sort(folders, StringComparer.Ordinal);
            return folders;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static JsonSchema LoadSchema()
    {
        const string resourceName = "Axis.Configuration.Templates.template.schema.json";
        using var stream = typeof(TemplateCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
