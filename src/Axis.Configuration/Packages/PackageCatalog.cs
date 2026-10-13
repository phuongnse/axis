using System.Reflection;
using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Extensions;
using Json.Schema;

namespace Axis.Configuration.Packages;

/// <summary>
/// The extension packages installed under <c>Extensions:Path</c>, one folder per package id and
/// version: <c>&lt;id&gt;/&lt;version&gt;/axis-package.json</c>. Each version whose manifest matches
/// its folder and its assembly is loaded in its own <see cref="PackageLoadContext"/>. A package with
/// any problem is left out and each problem is a diagnostic, whose file is relative to the package
/// root. Loading never stops at the first broken package.
/// </summary>
public sealed class PackageCatalog
{
    public const string PathSetting = "Extensions:Path";
    public const string ManifestFileName = "axis-package.json";

    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromMinutes(2);

    private static readonly JsonDocumentOptions _documentOptions = new() { AllowDuplicateProperties = false };

    private static readonly JsonSchema _schema = LoadSchema();

    private PackageCatalog(IReadOnlyList<InstalledPackage> packages, IReadOnlyList<Diagnostic> diagnostics)
    {
        Packages = packages;
        Diagnostics = diagnostics;
    }

    public static PackageCatalog Empty { get; } = new([], []);

    /// <summary>The loaded packages, by id and then by version.</summary>
    public IReadOnlyList<InstalledPackage> Packages { get; }

    /// <summary>The problems of the packages that were left out.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public InstalledPackage? Find(string id, string version) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.Manifest.Id, id, StringComparison.Ordinal)
            && string.Equals(package.Manifest.Version, version, StringComparison.Ordinal));

    /// <summary>
    /// Finds and loads the packages under <paramref name="root"/>. A null or empty root gives an
    /// empty catalog.
    /// </summary>
    /// <exception cref="InvalidOperationException">The root folder does not exist or cannot be listed.</exception>
    public static PackageCatalog Load(string? root)
    {
        if (string.IsNullOrEmpty(root))
        {
            return Empty;
        }

        if (!Directory.Exists(root))
        {
            throw new InvalidOperationException($"The extension package folder '{root}' set by '{PathSetting}' does not exist.");
        }

        var packages = new List<InstalledPackage>();
        var diagnostics = new List<Diagnostic>();
        foreach (var idFolder in ListFolders(root) ?? throw new InvalidOperationException(
            $"The extension package folder '{root}' set by '{PathSetting}' cannot be listed."))
        {
            var idName = Path.GetFileName(idFolder);
            var versionFolders = ListFolders(idFolder);
            if (versionFolders is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PackageManifestInvalid,
                    $"The folder of package '{idName}' cannot be listed.",
                    idName,
                    ""));
                continue;
            }

            foreach (var versionFolder in versionFolders)
            {
                if (LoadPackage(idName, versionFolder, diagnostics) is { } package)
                {
                    packages.Add(package);
                }
            }
        }

        packages.Sort((left, right) =>
            string.CompareOrdinal(left.Manifest.Id, right.Manifest.Id) is var byId and not 0
                ? byId
                : CompareVersions(left.Manifest.Version, right.Manifest.Version));
        return new PackageCatalog(packages, DiagnosticOrder.Sort(diagnostics));
    }

    private static InstalledPackage? LoadPackage(string idName, string versionFolder, List<Diagnostic> diagnostics)
    {
        var versionName = Path.GetFileName(versionFolder);
        var file = $"{idName}/{versionName}/{ManifestFileName}";
        var label = $"Package '{idName}' {versionName}";
        var problems = new List<Diagnostic>();
        void Report(string code, string message, string path) =>
            problems.Add(new Diagnostic(code, $"{label}: {message}", file, path));

        var manifest = ReadManifest(Path.Combine(versionFolder, ManifestFileName), file, label, problems);
        if (manifest is null)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        CheckManifest(manifest, Report);
        if (!string.Equals(manifest.Id, idName, StringComparison.Ordinal))
        {
            Report(DiagnosticCodes.PackageFolderMismatch, $"The id '{manifest.Id}' differs from its folder name '{idName}'.", "/id");
        }

        if (!string.Equals(manifest.Version, versionName, StringComparison.Ordinal))
        {
            Report(DiagnosticCodes.PackageFolderMismatch, $"The version '{manifest.Version}' differs from its folder name '{versionName}'.", "/version");
        }

        if (problems.Count > 0)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        var assemblyPath = Path.Combine(versionFolder, manifest.Assembly);
        if (LoadAssembly($"{manifest.Id}/{manifest.Version}", assemblyPath) is not var (loadContext, assembly))
        {
            Report(DiagnosticCodes.PackageAssemblyMissing, $"The assembly '{manifest.Assembly}' is missing or cannot be loaded.", "/assembly");
            diagnostics.AddRange(problems);
            return null;
        }

        var informationalVersion = InformationalVersion(assembly);
        if (!string.Equals(informationalVersion, manifest.Version, StringComparison.Ordinal))
        {
            Report(
                DiagnosticCodes.PackageVersionMismatch,
                informationalVersion is null
                    ? $"The assembly '{manifest.Assembly}' has no informational version, but the manifest says '{manifest.Version}'."
                    : $"The assembly '{manifest.Assembly}' has version '{informationalVersion}', but the manifest says '{manifest.Version}'.",
                "/version");
        }

        var operationTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < manifest.Operations.Count; index++)
        {
            var operation = manifest.Operations[index];
            if (FindOperationType(assembly, operation.Type) is { } type)
            {
                operationTypes[operation.Name] = type;
            }
            else
            {
                Report(
                    DiagnosticCodes.InvalidPackageOperationType,
                    $"The type '{operation.Type}' of operation '{operation.Name}' is not a class in '{manifest.Assembly}' that implements '{nameof(IOperation)}' and has a public parameterless constructor.",
                    $"/operations/{index}/type");
            }
        }

        if (problems.Count > 0)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        return new InstalledPackage(manifest, loadContext, operationTypes);
    }

    private static PackageManifest? ReadManifest(string manifestPath, string file, string label, List<Diagnostic> problems)
    {
        void Report(string message, string path) =>
            problems.Add(new Diagnostic(DiagnosticCodes.PackageManifestInvalid, $"{label}: {message}", file, path));

        if (!File.Exists(manifestPath))
        {
            Report($"The manifest '{ManifestFileName}' is missing.", "");
            return null;
        }

        JsonDocument document;
        try
        {
            using var stream = File.OpenRead(manifestPath);
            document = JsonDocument.Parse(stream, _documentOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception message names the absolute path, so it is never passed on.
            Report("The manifest could not be read.", "");
            return null;
        }
        catch (JsonException exception)
        {
            Report(
                exception.LineNumber is { } line
                    ? $"The manifest is not valid JSON (line {line + 1}, byte {exception.BytePositionInLine + 1})."
                    : "The manifest is not valid JSON: a property appears more than once.",
                "");
            return null;
        }

        using (document)
        {
            var schemaProblems = new List<Diagnostic>();
            if (!SchemaDiagnostics.Validate(_schema, document.RootElement, DiagnosticCodes.PackageManifestInvalid, file, null, schemaProblems))
            {
                problems.AddRange(schemaProblems.Select(problem => problem with { Message = $"{label}: {problem.Message}" }));
                return null;
            }

            return ParseManifest(document.RootElement);
        }
    }

    /// <summary>Reads a manifest that is valid against its schema. A timeout that does not parse is kept as zero for <see cref="CheckManifest"/>.</summary>
    private static PackageManifest ParseManifest(JsonElement root) =>
        new(
            root.GetProperty("id").GetString()!,
            root.GetProperty("version").GetString()!,
            root.GetProperty("assembly").GetString()!,
            [.. root.GetProperty("operations").EnumerateArray().Select(ParseOperation)]);

    private static PackageOperation ParseOperation(JsonElement operation)
    {
        var timeout = _defaultTimeout;
        if (operation.TryGetProperty("timeout", out var timeoutElement))
        {
            timeout = IsoDuration.TryParse(timeoutElement.GetString()!, out var parsed) ? parsed : TimeSpan.Zero;
        }

        var capabilities = operation.TryGetProperty("capabilities", out var capabilitiesElement) ? capabilitiesElement : default;
        return new PackageOperation(
            operation.GetProperty("name").GetString()!,
            operation.GetProperty("type").GetString()!,
            operation.GetProperty("idempotent").GetBoolean(),
            timeout,
            ParseFields(operation, "input"),
            ParseFields(operation, "output"),
            new PackageCapabilities(Strings(capabilities, "urls"), Strings(capabilities, "secrets")));
    }

    private static List<PackageField> ParseFields(JsonElement operation, string property) =>
        operation.TryGetProperty(property, out var fields)
            ? [.. fields.EnumerateArray().Select(field => new PackageField(
                field.GetProperty("name").GetString()!,
                FieldTypes.Parse(field.GetProperty("type").GetString()!),
                field.TryGetProperty("required", out var required) && required.GetBoolean()))]
            : [];

    private static List<string> Strings(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var values)
            ? [.. values.EnumerateArray().Select(value => value.GetString()!)]
            : [];

    /// <summary>Checks what the schema cannot: unique names, ignoring letter case, and the timeout.</summary>
    private static void CheckManifest(PackageManifest manifest, Action<string, string, string> report)
    {
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < manifest.Operations.Count; index++)
        {
            var operation = manifest.Operations[index];
            var path = $"/operations/{index}";
            if (!firstIndexByName.TryAdd(operation.Name, index))
            {
                report(
                    DiagnosticCodes.DuplicatePackageName,
                    $"The operation name '{operation.Name}' is already used by operation '{manifest.Operations[firstIndexByName[operation.Name]].Name}'.",
                    $"{path}/name");
            }

            if (operation.Timeout <= TimeSpan.Zero || operation.Timeout > _maxTimeout)
            {
                report(
                    DiagnosticCodes.InvalidPackageTimeout,
                    $"The timeout of operation '{operation.Name}' is not allowed. Use a positive ISO 8601 duration of at most PT2M, such as 'PT30S'.",
                    $"{path}/timeout");
            }

            CheckFieldNames(operation, operation.Input, $"{path}/input", "input", report);
            CheckFieldNames(operation, operation.Output, $"{path}/output", "output", report);
        }
    }

    private static void CheckFieldNames(
        PackageOperation operation, IReadOnlyList<PackageField> fields, string path, string list, Action<string, string, string> report)
    {
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < fields.Count; index++)
        {
            if (!firstIndexByName.TryAdd(fields[index].Name, index))
            {
                report(
                    DiagnosticCodes.DuplicatePackageName,
                    $"The {list} field name '{fields[index].Name}' of operation '{operation.Name}' is already used by field '{fields[firstIndexByName[fields[index].Name]].Name}'.",
                    $"{path}/{index}/name");
            }
        }
    }

    /// <summary>Loads the main assembly in a new load context, or returns null when it is missing or cannot be loaded.</summary>
    private static (PackageLoadContext LoadContext, Assembly Assembly)? LoadAssembly(string name, string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            return null;
        }

        try
        {
            // The dependency resolver throws InvalidOperationException for a .deps.json it cannot read.
            var loadContext = new PackageLoadContext(name, assemblyPath);
            return (loadContext, loadContext.LoadFromAssemblyPath(assemblyPath));
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The exception message names the absolute path, so it is never passed on.
            return null;
        }
    }

    /// <summary>The class named <paramref name="typeName"/> in <paramref name="assembly"/>, when the host can create it as an operation.</summary>
    private static Type? FindOperationType(Assembly assembly, string typeName)
    {
        Type? type;
        try
        {
            type = assembly.GetType(typeName, throwOnError: false);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or BadImageFormatException or TypeLoadException)
        {
            return null;
        }

        return type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
            && typeof(IOperation).IsAssignableFrom(type)
            && type.GetConstructor(Type.EmptyTypes) is not null
                ? type
                : null;
    }

    /// <summary>The informational version of <paramref name="assembly"/> without the build metadata after <c>+</c>.</summary>
    private static string? InformationalVersion(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return version?.Split('+')[0];
    }

    /// <summary>Compares two <c>major.minor.patch</c> versions without leading zeros, part by part.</summary>
    private static int CompareVersions(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < leftParts.Length; index++)
        {
            var byLength = leftParts[index].Length.CompareTo(rightParts[index].Length);
            var byPart = byLength != 0 ? byLength : string.CompareOrdinal(leftParts[index], rightParts[index]);
            if (byPart != 0)
            {
                return byPart;
            }
        }

        return 0;
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
        const string resourceName = "Axis.Configuration.Packages.axis-package.schema.json";
        using var stream = typeof(PackageCatalog).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded schema '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return JsonSchema.FromText(reader.ReadToEnd());
    }
}
