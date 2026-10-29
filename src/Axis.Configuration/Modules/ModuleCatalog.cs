using System.Text.Json;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Json.Schema;

namespace Axis.Configuration.Modules;

/// <summary>
/// The modules installed under <c>Modules:Path</c>, one folder per module id and version:
/// <c>&lt;id&gt;/&lt;version&gt;/module.json</c>. A version whose manifest is valid and matches its
/// folder is loaded with every <c>*.json</c> file under its folder. A version with any problem is
/// left out and each problem is a diagnostic, whose file is relative to the module root. Loading
/// never stops at the first broken version.
/// </summary>
public sealed class ModuleCatalog
{
    public const string PathSetting = "Modules:Path";
    public const string ManifestFileName = "module.json";

    /// <summary>
    /// The <c>kind</c> of <c>module.json</c>. It is not a <see cref="Resources.ResourceKinds"/> value,
    /// because a <c>module</c> file in an application folder is an unknown kind.
    /// </summary>
    public const string ManifestKind = "module";

    private static readonly JsonDocumentOptions _documentOptions = new() { AllowDuplicateProperties = false };

    private static readonly JsonSchema _schema = LoadSchema();

    private ModuleCatalog(IReadOnlyList<InstalledModule> modules, IReadOnlyList<Diagnostic> diagnostics)
    {
        Modules = modules;
        Diagnostics = diagnostics;
    }

    public static ModuleCatalog Empty { get; } = new([], []);

    /// <summary>The loaded modules, by id and then by version.</summary>
    public IReadOnlyList<InstalledModule> Modules { get; }

    /// <summary>The problems of the module versions that were left out.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Finds and loads the modules under <paramref name="root"/>. A null or empty root gives an
    /// empty catalog.
    /// </summary>
    /// <exception cref="InvalidOperationException">The root folder does not exist or cannot be listed.</exception>
    public static ModuleCatalog Load(string? root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return Empty;
        }

        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"The module folder '{root}' set by '{PathSetting}' does not exist.");
        }

        var modules = new List<InstalledModule>();
        var diagnostics = new List<Diagnostic>();
        foreach (var idFolder in ListFolders(root) ?? throw new InvalidOperationException(
            $"The module folder '{root}' set by '{PathSetting}' cannot be listed."))
        {
            var idName = Path.GetFileName(idFolder);
            var versionFolders = ListFolders(idFolder);
            if (versionFolders is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ModuleManifestInvalid,
                    $"The folder of module '{idName}' cannot be listed.",
                    idName,
                    ""));
                continue;
            }

            foreach (var versionFolder in versionFolders)
            {
                if (LoadModule(idName, versionFolder, diagnostics) is { } module)
                {
                    modules.Add(module);
                }
            }
        }

        modules.Sort((left, right) =>
            string.CompareOrdinal(left.Manifest.Id, right.Manifest.Id) is var byId and not 0
                ? byId
                : PackageVersions.Compare(left.Manifest.Version, right.Manifest.Version));
        return new ModuleCatalog(modules, DiagnosticOrder.Sort(diagnostics));
    }

    /// <summary>
    /// Reads <paramref name="json"/> as a module manifest and checks it against the schema. Each
    /// problem is an <see cref="DiagnosticCodes.ModuleManifestInvalid"/> diagnostic in
    /// <paramref name="file"/>. Returns null when there is any.
    /// </summary>
    internal static ModuleManifest? ReadManifest(string json, string file, List<Diagnostic> problems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, _documentOptions);
        }
        catch (JsonException exception)
        {
            problems.Add(new Diagnostic(
                DiagnosticCodes.ModuleManifestInvalid,
                exception.LineNumber is { } line
                    ? $"The manifest is not valid JSON (line {line + 1}, byte {exception.BytePositionInLine + 1})."
                    : "The manifest is not valid JSON: a property appears more than once.",
                file,
                ""));
            return null;
        }

        using (document)
        {
            if (!SchemaDiagnostics.Validate(_schema, document.RootElement, DiagnosticCodes.ModuleManifestInvalid, file, null, problems))
            {
                return null;
            }

            var root = document.RootElement;
            var manifest = new ModuleManifest(
                root.GetProperty("id").GetString()!,
                root.GetProperty("version").GetString()!,
                root.GetProperty("label").GetString()!);
            if (!root.TryGetProperty("roles", out var rolesElement))
            {
                return manifest;
            }

            var slots = new List<ModuleRoleSlot>();
            var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var slotElement in rolesElement.EnumerateArray())
            {
                var name = slotElement.GetProperty("name").GetString()!;
                if (!firstIndexByName.TryAdd(name, slots.Count))
                {
                    problems.Add(new Diagnostic(
                        DiagnosticCodes.ModuleManifestInvalid,
                        $"The role slot '{name}' repeats the name of the slot at '/roles/{firstIndexByName[name]}', ignoring letter case. Name each slot once.",
                        file,
                        $"/roles/{slots.Count}/name"));
                }

                slots.Add(new ModuleRoleSlot(
                    name,
                    new TextReference(slotElement.GetProperty("label").GetProperty("textKey").GetString()!)));
            }

            return problems.Count > 0 ? null : manifest with { Roles = slots };
        }
    }

    private static InstalledModule? LoadModule(string idName, string versionFolder, List<Diagnostic> diagnostics)
    {
        var versionName = Path.GetFileName(versionFolder);
        var prefix = $"{idName}/{versionName}/";
        var file = prefix + ManifestFileName;
        var problems = new List<Diagnostic>();

        var manifestPath = Path.Combine(versionFolder, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ModuleManifestInvalid, $"The manifest '{ManifestFileName}' is missing.", file, ""));
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
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ModuleManifestInvalid, "The manifest could not be read.", file, ""));
            return null;
        }

        var manifest = ReadManifest(json, file, problems);
        if (manifest is null)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        if (!string.Equals(manifest.Id, idName, StringComparison.Ordinal))
        {
            problems.Add(new Diagnostic(
                DiagnosticCodes.ModuleFolderMismatch,
                $"The id '{manifest.Id}' differs from its folder name '{idName}'.",
                file,
                "/id"));
        }

        if (!string.Equals(manifest.Version, versionName, StringComparison.Ordinal))
        {
            problems.Add(new Diagnostic(
                DiagnosticCodes.ModuleFolderMismatch,
                $"The version '{manifest.Version}' differs from its folder name '{versionName}'.",
                file,
                "/version"));
        }

        if (problems.Count > 0)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        var files = ReadFiles(versionFolder, prefix, problems);
        if (files is null)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        return new InstalledModule(manifest, files);
    }

    /// <summary>Reads every <c>*.json</c> file under the version folder, or returns null with a diagnostic when one cannot be listed or read.</summary>
    private static List<ResourceContent>? ReadFiles(string versionFolder, string prefix, List<Diagnostic> problems)
    {
        // The same options as the application loader: hidden files are skipped and an inaccessible folder fails.
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = false };
        List<string> paths;
        try
        {
            // The enumeration is lazy, so it must run to the end inside the try.
            paths = [.. Directory.EnumerateFiles(versionFolder, "*.json", options)
                .Select(path => Path.GetRelativePath(versionFolder, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problems.Add(new Diagnostic(
                DiagnosticCodes.ModuleManifestInvalid, "The files of the module version cannot be listed.", prefix + ManifestFileName, ""));
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
                problems.Add(new Diagnostic(
                    DiagnosticCodes.ModuleManifestInvalid, "The file could not be read.", prefix + path, ""));
            }
        }

        return problems.Count > 0 ? null : files;
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
        const string resourceName = "Axis.Configuration.Modules.module.schema.json";
        using var stream = typeof(ModuleCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
