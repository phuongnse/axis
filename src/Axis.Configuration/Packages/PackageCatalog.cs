using System.Reflection;
using System.Text;
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
/// its folder and its assembly is loaded in its own <see cref="PackageLoadContext"/>, or only read as
/// metadata, see <see cref="PackageLoadMode"/>. A <c>node</c> or <c>python</c> version is listed from
/// its manifest and adapter file alone, without loading code. A package with any problem is left out
/// and each problem is a diagnostic, whose file is relative to the package root. Loading never stops
/// at the first broken package.
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
        Versions = [.. packages.Select(package => new PackageVersion(package.Manifest.Id, package.Manifest.Version) { Operations = package.Manifest.Operations })];
    }

    public static PackageCatalog Empty { get; } = new([], []);

    /// <summary>The loaded packages, by id and then by version.</summary>
    public IReadOnlyList<InstalledPackage> Packages { get; }

    /// <summary>The id, version and operations of each loaded package, in the order of <see cref="Packages"/>.</summary>
    public IReadOnlyList<PackageVersion> Versions { get; }

    /// <summary>The problems of the packages that were left out.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public InstalledPackage? Find(string id, string version) =>
        Packages.FirstOrDefault(package =>
            string.Equals(package.Manifest.Id, id, StringComparison.Ordinal)
            && string.Equals(package.Manifest.Version, version, StringComparison.Ordinal));

    /// <summary>
    /// Finds and loads the packages under <paramref name="root"/>. A null or empty root gives an
    /// empty catalog. With <see cref="PackageLoadMode.MetadataOnly"/> no package code runs and the
    /// packages cannot create operations.
    /// </summary>
    /// <exception cref="InvalidOperationException">The root folder does not exist or cannot be listed.</exception>
    public static PackageCatalog Load(string? root, PackageLoadMode mode = PackageLoadMode.Run)
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
                var versionName = Path.GetFileName(versionFolder);
                if (LoadPackage(versionFolder, idName, versionName, $"{idName}/{versionName}/{ManifestFileName}", $"Package '{idName}' {versionName}", mode, diagnostics) is { } package)
                {
                    packages.Add(package);
                }
            }
        }

        packages.Sort((left, right) =>
            string.CompareOrdinal(left.Manifest.Id, right.Manifest.Id) is var byId and not 0
                ? byId
                : PackageVersions.Compare(left.Manifest.Version, right.Manifest.Version));
        return new PackageCatalog(packages, DiagnosticOrder.Sort(diagnostics));
    }

    /// <summary>
    /// Checks one package folder as <see cref="Load"/> checks each version folder, but without the folder name
    /// check, because the folder is not yet named by the package's id and version. The diagnostics' file is
    /// <c>axis-package.json</c>. The assembly of a C# package is loaded to check it.
    /// </summary>
    public static PackageFolderCheck CheckFolder(string folder)
    {
        var diagnostics = new List<Diagnostic>();
        var package = LoadPackage(folder, null, null, ManifestFileName, "Package source", PackageLoadMode.Run, diagnostics);
        return new PackageFolderCheck(package, DiagnosticOrder.Sort(diagnostics));
    }

    /// <summary>
    /// Loads the one version <c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;</c> with the checks of <see cref="Load"/>, so a package runner
    /// loads only the version it runs. The package is null when the version has any problem, and each problem is a diagnostic.
    /// </summary>
    public static (InstalledPackage? Package, IReadOnlyList<Diagnostic> Diagnostics) LoadVersion(string root, string id, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(version);

        var diagnostics = new List<Diagnostic>();
        var package = LoadPackage(
            Path.Combine(root, id, version), id, version, $"{id}/{version}/{ManifestFileName}", $"Package '{id}' {version}", PackageLoadMode.Run, diagnostics);
        return (package, DiagnosticOrder.Sort(diagnostics));
    }

    /// <summary>
    /// Reads the manifest of the installed version <c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;</c>, and loads no package code. The manifest is
    /// null when it is missing, cannot be read or does not match the manifest schema.
    /// </summary>
    public static PackageManifest? ReadInstalledManifest(string root, string id, string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(version);

        return ReadManifest(Path.Combine(root, id, version, ManifestFileName), $"{id}/{version}/{ManifestFileName}", $"Package '{id}' {version}", []);
    }

    /// <summary>Loads the package in <paramref name="versionFolder"/>. The folder names are checked against the manifest only when <paramref name="idName"/> and <paramref name="versionName"/> are given.</summary>
    private static InstalledPackage? LoadPackage(
        string versionFolder, string? idName, string? versionName, string file, string label, PackageLoadMode mode, List<Diagnostic> diagnostics)
    {
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
        if (idName is not null && !string.Equals(manifest.Id, idName, StringComparison.Ordinal))
        {
            Report(DiagnosticCodes.PackageFolderMismatch, $"The id '{manifest.Id}' differs from its folder name '{idName}'.", "/id");
        }

        if (versionName is not null && !string.Equals(manifest.Version, versionName, StringComparison.Ordinal))
        {
            Report(DiagnosticCodes.PackageFolderMismatch, $"The version '{manifest.Version}' differs from its folder name '{versionName}'.", "/version");
        }

        if (problems.Count > 0)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        // A node or python package is listed from its manifest alone, see docs/reference/extension-packages.md#loading.
        if (manifest.Language != PackageLanguage.CSharp)
        {
            if (!File.Exists(Path.Combine(versionFolder, manifest.Adapter!)))
            {
                Report(DiagnosticCodes.PackageAssemblyMissing, $"The adapter '{manifest.Adapter}' is missing.", "/adapter");
                diagnostics.AddRange(problems);
                return null;
            }

            return new InstalledPackage(manifest, null, []);
        }

        var assemblyPath = Path.Combine(versionFolder, manifest.Assembly!);
        var installed = mode == PackageLoadMode.Run
            ? LoadToRun(manifest, assemblyPath, Report)
            : ReadMetadata(manifest, assemblyPath, Report);
        if (problems.Count > 0)
        {
            diagnostics.AddRange(problems);
            return null;
        }

        return installed;
    }

    /// <summary>Loads the main assembly in its own load context, which can run its code.</summary>
    private static InstalledPackage? LoadToRun(PackageManifest manifest, string assemblyPath, Action<string, string, string> report)
    {
        if (LoadAssembly($"{manifest.Id}/{manifest.Version}", assemblyPath) is not var (loadContext, assembly))
        {
            ReportAssemblyMissing(manifest, report);
            return null;
        }

        var operationTypes = manifest.Operations.Select(operation => FindOperationType(assembly, operation.Type)).ToList();
        if (!CheckAssembly(manifest, InformationalVersion(assembly), operationTypes.Select(type => type is not null).ToList(), report))
        {
            return null;
        }

        return new InstalledPackage(
            manifest,
            loadContext,
            manifest.Operations.Zip(operationTypes).ToDictionary(pair => pair.First.Name, pair => pair.Second!, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads the main assembly as metadata, so no static constructor, module initializer or other
    /// package code runs in this process, see <c>docs/reference/extension-packages.md#loading</c>. The
    /// context is disposed before this returns, so the package keeps no types or file handles.
    /// </summary>
    private static InstalledPackage? ReadMetadata(PackageManifest manifest, string assemblyPath, Action<string, string, string> report)
    {
        string? informationalVersion;
        List<bool> operationTypes;
        MetadataLoadContext? context = null;
        try
        {
            // The dependency resolver throws InvalidOperationException for a .deps.json it cannot read.
            context = new MetadataLoadContext(new PackageMetadataResolver(assemblyPath), typeof(object).Assembly.GetName().Name);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);
            var contract = context.LoadFromAssemblyName(typeof(IOperation).Assembly.GetName()).GetType(typeof(IOperation).FullName!);
            informationalVersion = InformationalVersion(assembly.GetCustomAttributesData());
            operationTypes = [.. manifest.Operations.Select(operation => IsOperationType(assembly, contract, operation.Type))];
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The exception message names the absolute path, so it is never passed on.
            ReportAssemblyMissing(manifest, report);
            return null;
        }
        finally
        {
            context?.Dispose();
        }

        return CheckAssembly(manifest, informationalVersion, operationTypes, report)
            ? new InstalledPackage(manifest)
            : null;
    }

    private static void ReportAssemblyMissing(PackageManifest manifest, Action<string, string, string> report) =>
        report(DiagnosticCodes.PackageAssemblyMissing, $"The assembly '{manifest.Assembly}' is missing or cannot be loaded.", "/assembly");

    /// <summary>Reports the version and the operation types that do not match the manifest. Returns whether all match.</summary>
    private static bool CheckAssembly(
        PackageManifest manifest, string? informationalVersion, List<bool> operationTypeFound, Action<string, string, string> report)
    {
        var matches = true;
        if (!string.Equals(informationalVersion, manifest.Version, StringComparison.Ordinal))
        {
            matches = false;
            report(
                DiagnosticCodes.PackageVersionMismatch,
                informationalVersion is null
                    ? $"The assembly '{manifest.Assembly}' has no informational version, but the manifest says '{manifest.Version}'."
                    : $"The assembly '{manifest.Assembly}' has version '{informationalVersion}', but the manifest says '{manifest.Version}'.",
                "/version");
        }

        for (var index = 0; index < manifest.Operations.Count; index++)
        {
            if (!operationTypeFound[index])
            {
                matches = false;
                var operation = manifest.Operations[index];
                report(
                    DiagnosticCodes.InvalidPackageOperationType,
                    $"The type '{operation.Type}' of operation '{operation.Name}' is not a class in '{manifest.Assembly}' that implements '{nameof(IOperation)}' and has a public parameterless constructor.",
                    $"/operations/{index}/type");
            }
        }

        return matches;
    }

    private static PackageManifest? ReadManifest(string manifestPath, string file, string label, List<Diagnostic> problems)
    {
        if (!File.Exists(manifestPath))
        {
            problems.Add(new Diagnostic(DiagnosticCodes.PackageManifestInvalid, $"{label}: The manifest '{ManifestFileName}' is missing.", file, ""));
            return null;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            return ReadManifest(stream, file, label, problems);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception message names the absolute path, so it is never passed on.
            problems.Add(new Diagnostic(DiagnosticCodes.PackageManifestInvalid, $"{label}: The manifest could not be read.", file, ""));
            return null;
        }
    }

    /// <summary>
    /// Reads the manifest of an application's package from its text in a release or a folder, with the checks of
    /// <see cref="ReadInstalledManifest"/>. The manifest is null, with its problems in <paramref name="problems"/>, when it is not valid JSON or does not match the schema.
    /// </summary>
    internal static PackageManifest? ReadManifestContent(string content, string file, string label, List<Diagnostic> problems)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return ReadManifest(stream, file, label, problems);
    }

    /// <summary>Checks what the schema cannot, as <see cref="Load"/> does for an installed package: unique names and the timeout.</summary>
    internal static void CheckManifestRules(PackageManifest manifest, string file, string label, List<Diagnostic> problems) =>
        CheckManifest(manifest, (code, message, path) => problems.Add(new Diagnostic(code, $"{label}: {message}", file, path)));

    private static PackageManifest? ReadManifest(Stream stream, string file, string label, List<Diagnostic> problems)
    {
        void Report(string message, string path) =>
            problems.Add(new Diagnostic(DiagnosticCodes.PackageManifestInvalid, $"{label}: {message}", file, path));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, _documentOptions);
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
            root.GetProperty("label").GetString()!,
            root.TryGetProperty("language", out var language) ? ParseLanguage(language.GetString()!) : PackageLanguage.CSharp,
            root.TryGetProperty("assembly", out var assembly) ? assembly.GetString()! : null,
            root.TryGetProperty("adapter", out var adapter) ? adapter.GetString()! : null,
            root.GetProperty("lock").GetString()!,
            [.. root.GetProperty("operations").EnumerateArray().Select(ParseOperation)]);

    private static PackageLanguage ParseLanguage(string language) =>
        language switch
        {
            "node" => PackageLanguage.Node,
            "python" => PackageLanguage.Python,
            _ => PackageLanguage.CSharp,
        };

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

    /// <summary>Whether the class named <paramref name="typeName"/> in the metadata of <paramref name="assembly"/> is one the host could create as an operation.</summary>
    private static bool IsOperationType(Assembly assembly, Type? contract, string typeName)
    {
        try
        {
            return contract is not null
                && assembly.GetType(typeName, throwOnError: false) is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } type
                && contract.IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or BadImageFormatException or TypeLoadException)
        {
            return false;
        }
    }

    /// <summary>The informational version in the attribute data of a metadata-only assembly, without the build metadata after <c>+</c>.</summary>
    private static string? InformationalVersion(IList<CustomAttributeData> attributes) =>
        attributes
            .FirstOrDefault(attribute => attribute.AttributeType.FullName == typeof(AssemblyInformationalVersionAttribute).FullName)
            ?.ConstructorArguments[0].Value is string version
                ? version.Split('+')[0]
                : null;

    /// <summary>The informational version of <paramref name="assembly"/> without the build metadata after <c>+</c>.</summary>
    private static string? InformationalVersion(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return version?.Split('+')[0];
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
