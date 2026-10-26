using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Expressions;
using Axis.Expressions.Diagnostics;
using Axis.Expressions.Evaluation;
using Axis.Expressions.Parsing;
using Axis.Expressions.Sql;
using Axis.Expressions.Typing;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Compiles an application folder, or its resources held in memory: loads them, checks the texts
/// of every locale, checks the named rules and their calls, and the rules of each pinned module on their own, checks every entity's fields against
/// the field type rules and type-checks its computed fields and validations, checks forms, sites,
/// pages and seeds, checks data sources and their filters, checks role labels, checks processes,
/// their step graphs, their conditions, the roles their tasks name and the connector operations
/// their steps call and the pinned package operations they run, checks policies, their
/// roles, their grants and their filters, checks connectors, their timeouts, operations, paths and
/// lookups, checks events, their entities and their payload fields, checks endpoints, their
/// entities, processes, input fields, mappings, output and wait, checks triggers, their
/// processes, events and conditions, checks migrations, their entities, fields and values, checks
/// scenarios, their users, steps, values and stubs, checks
/// the extension packages of the manifest and pins each to the highest installed version in its
/// range, does the same for its modules, takes the files of each pinned module into the
/// resources and merges their texts into the application's, resolves references
/// between entities, forms, pages, sites and seeds, builds the reference index of the references it resolves, collects the names it loaded and builds the policy matrix of its roles and policies.
/// Every problem is reported, together with the loader's, in one
/// sorted list. An application without errors also gets its content hash, which covers its pins and the files of its modules.
/// Both inputs give the same result for the same resources and installed packages.
/// </summary>
public static partial class ApplicationCompiler
{
    /// <summary>The largest length PostgreSQL accepts for <c>varchar(n)</c>.</summary>
    public const int MaxTextLength = 10_485_760;

    /// <summary>The largest precision PostgreSQL accepts for <c>numeric(p, s)</c>.</summary>
    public const int MaxPrecision = 1000;

    /// <summary>The size limit of a <c>file</c> field's <c>maxSize</c> when the caller passes none: 25 MB, the default of <c>Files:MaxSize</c>.</summary>
    public const long DefaultMaxFileSize = 26_214_400;

    // A media type 'type/subtype' or 'type/*', with the name characters of RFC 6838 and no parameters.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,126}/([A-Za-z0-9][A-Za-z0-9!#$&^_.+-]{0,126}|\*)$", RegexOptions.CultureInvariant)]
    private static partial Regex AcceptEntryPattern();

    /// <summary>The precision of a money field that does not set one.</summary>
    public const int MoneyDefaultPrecision = 18;

    /// <summary>The scale of a money field with a <c>currencyField</c> that does not set one.</summary>
    public const int MoneyDefaultScale = 2;

    /// <summary>
    /// Compiles the folder at <paramref name="folderPath"/> against <paramref name="installedPackages"/>
    /// and <paramref name="installedModules"/>. Null means none is installed. A <c>file</c> field's <c>maxSize</c> may be at most
    /// <paramref name="maxFileSize"/> bytes.
    /// </summary>
    public static CompilationResult Compile(
        string folderPath,
        IReadOnlyCollection<PackageVersion>? installedPackages = null,
        IReadOnlyCollection<InstalledModule>? installedModules = null,
        long maxFileSize = DefaultMaxFileSize) =>
        Compile(folderPath, installedPackages, installedModules, collectReferences: true, maxFileSize);

    /// <summary>Lets the performance tests measure what the reference index adds to a compile.</summary>
    internal static CompilationResult Compile(
        string folderPath,
        IReadOnlyCollection<PackageVersion>? installedPackages,
        IReadOnlyCollection<InstalledModule>? installedModules,
        bool collectReferences,
        long maxFileSize = DefaultMaxFileSize) =>
        Compile(ApplicationLoader.Load(folderPath), installedPackages ?? [], installedModules ?? [], collectReferences, maxFileSize);

    /// <summary>
    /// Compiles the files of an application held in memory, such as a publish snapshot, as the folder overload compiles a
    /// folder: the paths are the application's own, relative with <c>/</c>, and the pinned modules come from
    /// <paramref name="installedModules"/>. Null means none is installed. Unlike the overload for the resources of a stored
    /// release, an entry under <c>modules/</c> is not a release copy of a module. A <c>file</c> field's <c>maxSize</c> may be at most
    /// <paramref name="maxFileSize"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentException">A path appears more than once.</exception>
    public static CompilationResult CompileFiles(
        IReadOnlyList<ResourceContent> files,
        IReadOnlyCollection<PackageVersion>? installedPackages,
        IReadOnlyCollection<InstalledModule>? installedModules,
        long maxFileSize = DefaultMaxFileSize)
    {
        ArgumentNullException.ThrowIfNull(files);

        return Compile(ApplicationLoader.Load(files), installedPackages ?? [], installedModules ?? [], collectReferences: true, maxFileSize);
    }

    /// <summary>
    /// Compiles the files and tells <paramref name="probe"/> what the compile saw at its expression place. It returns
    /// only the diagnostics: no model, reference index, names, policy matrix or content hash.
    /// </summary>
    /// <exception cref="ArgumentException">A path appears more than once.</exception>
    public static CompilationResult CompileFiles(
        IReadOnlyList<ResourceContent> files,
        IReadOnlyCollection<PackageVersion>? installedPackages,
        IReadOnlyCollection<InstalledModule>? installedModules,
        ExpressionProbe probe,
        long maxFileSize = DefaultMaxFileSize)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(probe);

        return Compile(ApplicationLoader.Load(files), installedPackages ?? [], installedModules ?? [], collectReferences: false, maxFileSize, probe);
    }

    /// <summary>
    /// Compiles resources held in memory, such as the stored resources of a release, against
    /// <paramref name="installedPackages"/>. Null means no package is installed. Entries under
    /// <c>modules/&lt;id&gt;/</c> are the release's own copies of its pinned modules, and the
    /// application compiles against those copies alone. A <c>file</c> field's <c>maxSize</c> may be at most
    /// <paramref name="maxFileSize"/> bytes.
    /// </summary>
    /// <exception cref="ArgumentException">A path appears more than once.</exception>
    public static CompilationResult Compile(
        IReadOnlyList<ResourceContent> resources,
        IReadOnlyCollection<PackageVersion>? installedPackages = null,
        long maxFileSize = DefaultMaxFileSize)
    {
        ArgumentNullException.ThrowIfNull(resources);

        static bool IsModuleCopy(ResourceContent resource) =>
            resource.Path.StartsWith(ApplicationLoader.ModulesFolderPrefix, StringComparison.Ordinal);

        // The loader only sees the application's own resources, so a repeated path among the copies is caught here.
        var moduleResources = resources.Where(IsModuleCopy).ToList();
        var copyPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in moduleResources)
        {
            if (!copyPaths.Add(resource.Path))
            {
                throw new ArgumentException($"The resource path '{resource.Path}' appears more than once.", nameof(resources));
            }
        }

        var copyDiagnostics = new List<Diagnostic>();
        var copies = ReadModuleCopies(moduleResources, copyDiagnostics);
        var applicationResources = resources.Where(resource => !IsModuleCopy(resource)).ToList();
        var result = Compile(ApplicationLoader.Load(applicationResources), installedPackages ?? [], copies, collectReferences: true, maxFileSize);
        if (copyDiagnostics.Count == 0)
        {
            return result;
        }

        return result with
        {
            Diagnostics = DiagnosticOrder.Sort([.. result.Diagnostics, .. copyDiagnostics]),
            Model = null,
            ContentHash = null,
            Resources = [],
        };
    }

    /// <summary>
    /// Groups the <c>modules/&lt;id&gt;/…</c> resources of a release into one installed module per
    /// id, each read from its own <c>module.json</c>. A copy without a valid manifest, or whose
    /// manifest id differs from its folder, is reported and left out.
    /// </summary>
    private static List<InstalledModule> ReadModuleCopies(IEnumerable<ResourceContent> copies, List<Diagnostic> diagnostics)
    {
        var modules = new List<InstalledModule>();
        foreach (var group in copies.GroupBy(copy => copy.Path.Split('/')[1], StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var prefix = $"{ApplicationLoader.ModulesFolderPrefix}{group.Key}/";
            var files = group
                .Where(copy => copy.Path.Length > prefix.Length)
                .Select(copy => new ResourceContent(copy.Path[prefix.Length..], copy.Content))
                .OrderBy(copy => copy.Path, StringComparer.Ordinal)
                .ToList();
            var manifestFile = prefix + ModuleCatalog.ManifestFileName;
            var manifestContent = files.FirstOrDefault(file => file.Path == ModuleCatalog.ManifestFileName);
            if (manifestContent is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ModuleManifestInvalid,
                    $"The manifest '{ModuleCatalog.ManifestFileName}' is missing.",
                    manifestFile,
                    ""));
                continue;
            }

            var problems = new List<Diagnostic>();
            var manifest = ModuleCatalog.ReadManifest(manifestContent.Content, manifestFile, problems);
            if (manifest is null)
            {
                diagnostics.AddRange(problems);
                continue;
            }

            if (!string.Equals(manifest.Id, group.Key, StringComparison.Ordinal))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ModuleFolderMismatch,
                    $"The id '{manifest.Id}' differs from its folder name '{group.Key}'.",
                    manifestFile,
                    "/id"));
                continue;
            }

            modules.Add(new InstalledModule(manifest, files));
        }

        return modules;
    }

    internal static bool IsModuleResource(Resource resource) =>
        resource.File.StartsWith(ApplicationLoader.ModulesFolderPrefix, StringComparison.Ordinal);

    private static CompilationResult Compile(
        ApplicationLoadResult loaded,
        IReadOnlyCollection<PackageVersion> installedPackages,
        IReadOnlyCollection<InstalledModule> installedModules,
        bool collectReferences,
        long maxFileSize,
        ExpressionProbe? probe = null)
    {
        var diagnostics = new List<Diagnostic>(loaded.Diagnostics);

        var textKeys = CheckTexts(loaded.Texts, diagnostics);
        var references = new ReferenceIndexBuilder(textKeys, loaded.Rules, collectReferences, probe);
        foreach (var text in loaded.Texts)
        {
            foreach (var key in text.Texts.Keys)
            {
                references.Add(text, $"/texts/{EscapePointerSegment(key)}", ReferenceRelations.TextEntry, new TextKeyNode(key));
            }
        }

        IReadOnlyList<PinnedPackageModel> packages = [];
        IReadOnlyList<PinnedModuleModel> modules = [];
        IReadOnlyList<ResourceContent> moduleFiles = [];
        IReadOnlySet<string>? declaredPackageIds = null;
        var timeZone = TimeZoneInfo.Utc;
        var moduleResources = new ModuleResources([], [], [], [], [], [], [], [], new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
        if (loaded.Application is { } manifest)
        {
            packages = PackageChecker.Check(manifest, installedPackages, diagnostics, references);
            (modules, moduleFiles, moduleResources) = ModuleChecker.Check(manifest, installedModules, diagnostics, references);
            declaredPackageIds = new HashSet<string>(manifest.Packages.Select(package => package.Id), StringComparer.Ordinal);
            if (manifest.TimeZone is { } timeZoneId && !IanaTimeZones.TryFind(timeZoneId, out timeZone))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownApplicationTimeZone,
                    $"The time zone '{timeZoneId}' is not known. Use an IANA id such as 'Asia/Ho_Chi_Minh', written with its exact letter case.",
                    manifest.File,
                    "/timeZone",
                    manifest.Id));
            }
        }

        // A pinned module's entities, sequences, seeds, events, data sources and forms come before the application's own, so a
        // step that depends on order sees each module target first. Application resources can name
        // them as '<module id>/<name>', but a module never names an application resource.
        var allEntities = new List<EntityResource>([.. moduleResources.Entities, .. loaded.Entities]);
        var allSequences = new List<SequenceResource>([.. moduleResources.Sequences, .. loaded.Sequences]);
        var allSeeds = new List<SeedResource>([.. moduleResources.Seeds, .. loaded.Seeds]);
        var allEvents = new List<EventResource>([.. moduleResources.Events, .. loaded.Events]);
        var unloadedEntityNames = new HashSet<string>(loaded.UnloadedEntityNames.Concat(moduleResources.UnloadedEntityNames), StringComparer.OrdinalIgnoreCase);
        var unloadedSequenceNames = new HashSet<string>(loaded.UnloadedSequenceNames.Concat(moduleResources.UnloadedSequenceNames), StringComparer.OrdinalIgnoreCase);
        var unloadedEventNames = new HashSet<string>(loaded.UnloadedEventNames.Concat(moduleResources.UnloadedEventNames), StringComparer.OrdinalIgnoreCase);
        var unloadedDataSourceNames = new HashSet<string>(loaded.UnloadedDataSourceNames.Concat(moduleResources.UnloadedDataSourceNames), StringComparer.OrdinalIgnoreCase);
        var unloadedFormNames = new HashSet<string>(loaded.UnloadedFormNames.Concat(moduleResources.UnloadedFormNames), StringComparer.OrdinalIgnoreCase);
        loaded = loaded with
        {
            DataSources = [.. moduleResources.DataSources, .. loaded.DataSources],
            Forms = [.. moduleResources.Forms, .. loaded.Forms],
            UnloadedEntityNames = unloadedEntityNames,
            UnloadedSequenceNames = unloadedSequenceNames,
            UnloadedEventNames = unloadedEventNames,
            UnloadedDataSourceNames = unloadedDataSourceNames,
            UnloadedFormNames = unloadedFormNames,
        };
        var declaredModules = new HashSet<string>(loaded.Application?.Modules.Select(module => module.Id) ?? [], StringComparer.Ordinal);
        var moduleReferences = new ModuleReferences(
            declaredModules,
            new HashSet<string>(modules.Select(pin => pin.Id), StringComparer.Ordinal));

        // Entity names are unique ignoring letter case; a second entity with the same name is
        // already reported by the loader, so references resolve to the first one.
        var entitiesByName = new Dictionary<string, EntityResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in allEntities)
        {
            entitiesByName.TryAdd(entity.Name, entity);
        }

        EntityResource? FindEntity(string name) => entitiesByName.GetValueOrDefault(name);

        var ownersByChildName = FindChildOwners(allEntities, FindEntity, diagnostics);

        // Page names follow the same rule as entity names.
        var pagesByName = new Dictionary<string, PageResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in loaded.Pages)
        {
            pagesByName.TryAdd(page.Name, page);
        }

        PageResource? FindPage(string name) => pagesByName.GetValueOrDefault(name);

        // Data source names follow the same rule too.
        var dataSourcesByName = new Dictionary<string, DataSourceResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataSource in loaded.DataSources)
        {
            dataSourcesByName.TryAdd(dataSource.Name, dataSource);
        }

        DataSourceResource? FindDataSource(string name) => dataSourcesByName.GetValueOrDefault(name);

        // Sequence names follow the same rule too.
        var sequencesByName = new Dictionary<string, SequenceResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var sequence in allSequences)
        {
            sequencesByName.TryAdd(sequence.Name, sequence);
        }

        SequenceResource? FindSequence(string name) => sequencesByName.GetValueOrDefault(name);

        // Form names follow the same rule too.
        var formsByName = new Dictionary<string, FormResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var form in loaded.Forms)
        {
            formsByName.TryAdd(form.Name, form);
        }

        FormResource? FindForm(string name) => formsByName.GetValueOrDefault(name);

        // Process names follow the same rule too.
        var processesByName = new Dictionary<string, ProcessResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in loaded.Processes)
        {
            processesByName.TryAdd(process.Name, process);
        }

        ProcessResource? FindProcess(string name) => processesByName.GetValueOrDefault(name);

        // Connector names follow the same rule too.
        var connectorsByName = new Dictionary<string, ConnectorResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var connector in loaded.Connectors)
        {
            connectorsByName.TryAdd(connector.Name, connector);
        }

        ConnectorResource? FindConnector(string name) => connectorsByName.GetValueOrDefault(name);

        // Event names follow the same rule too.
        var eventsByName = new Dictionary<string, EventResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var @event in allEvents)
        {
            eventsByName.TryAdd(@event.Name, @event);
        }

        EventResource? FindEvent(string name) => eventsByName.GetValueOrDefault(name);

        // Endpoint names follow the same rule too.
        var endpointsByName = new Dictionary<string, EndpointResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in loaded.Endpoints)
        {
            endpointsByName.TryAdd(endpoint.Name, endpoint);
        }

        EndpointResource? FindEndpoint(string name) => endpointsByName.GetValueOrDefault(name);

        // Role names are lower case only and are matched exactly.
        var rolesByName = new Dictionary<string, RoleResource>(StringComparer.Ordinal);
        foreach (var role in loaded.Roles)
        {
            rolesByName.TryAdd(role.Name, role);
        }

        RoleResource? FindRole(string name) => rolesByName.GetValueOrDefault(name);

        var texts = loaded.Texts;
        if (loaded.Application is { } application)
        {
            texts = ModuleTextChecker.Check(application, modules, loaded.Texts, moduleResources.Texts, diagnostics);
            textKeys.UnionWith(texts.SelectMany(text => text.Texts.Keys));
            CheckTextKey(application.Label, application, "/label", references, diagnostics);
        }

        // Package ids are matched exactly. Each pin is an installed version, so it has its manifest's operations.
        var pinnedPackages = packages.ToDictionary(
            pin => pin.Id,
            pin => new PinnedOperations(
                pin.Id,
                pin.Version,
                installedPackages.First(candidate => candidate.Id == pin.Id && candidate.Version == pin.Version).Operations),
            StringComparer.Ordinal);

        PinnedOperations? FindPackage(string id) => pinnedPackages.GetValueOrDefault(id);

        foreach (var role in loaded.Roles)
        {
            CheckTextKey(role.Label, role, "/label", references, diagnostics);
        }

        // A module rule sees only the rules of its own module. Application expressions call it
        // as '<module id>:<name>(…)', so the module rules are checked first.
        var moduleRules = RuleChecker.CheckModules(moduleResources.Rules, diagnostics).Values.ToList();

        // Validations, at their top level, data source filters, process conditions and policy
        // filters call rules, and module rules by their qualified name. Computed fields and
        // aggregate item expressions see no rules.
        var appRules = RuleChecker.Check(loaded.Rules, references, diagnostics, moduleRules, declaredModules);
        var rules = (IReadOnlyList<ExpressionRule>)[.. appRules.Values, .. moduleRules];

        // A module entity sees only the rules of its own module, by plain name. It sees no
        // application rule and no qualified call.
        IReadOnlyList<ExpressionRule> RulesFor(EntityResource entity) =>
            IsModuleResource(entity)
                ? [.. moduleRules.Where(rule => rule.Module == entity.Name[..entity.Name.IndexOf('/')]).Select(rule => rule with { Module = null })]
                : rules;

        foreach (var entity in allEntities)
        {
            CheckEntity(
                entity,
                FindEntity,
                ownersByChildName,
                loaded.UnloadedEntityNames,
                FindSequence,
                loaded.UnloadedSequenceNames,
                moduleReferences,
                references,
                RulesFor(entity),
                IsModuleResource(entity) ? null : declaredModules,
                maxFileSize,
                diagnostics);
        }

        PresentationChecker.Check(
            loaded,
            FindEntity,
            FindPage,
            FindDataSource,
            FindForm,
            FindProcess,
            (field, path, report) => CheckField(field, path, FindEntity, ownersByChildName, loaded.UnloadedEntityNames, moduleReferences, maxFileSize, report),
            moduleReferences,
            references,
            rules,
            declaredModules,
            diagnostics);
        CheckSeeds(allSeeds, loaded.UnloadedEntityNames, FindEntity, references, diagnostics);
        CheckDataSources(loaded, FindEntity, ownersByChildName, moduleReferences, references, rules, declaredModules, diagnostics);
        ProcessChecker.Check(
            loaded.Processes,
            FindEntity,
            name => ownersByChildName.TryGetValue(name, out var owner) ? owner.ToString() : null,
            loaded.UnloadedEntityNames,
            FindForm,
            loaded.UnloadedFormNames,
            FindRole,
            loaded.UnloadedRoleNames,
            FindConnector,
            loaded.UnloadedConnectorNames,
            FindEvent,
            loaded.UnloadedEventNames,
            FindPackage,
            declaredPackageIds,
            FindPage,
            loaded.UnloadedPageNames,
            key => [.. texts.Where(text => text.Texts.ContainsKey(key)).Select(text => (text.Locale, text.Texts[key]))],
            FindProcess,
            loaded.UnloadedProcessNames,
            references,
            rules,
            declaredModules,
            diagnostics);
        PolicyChecker.Check(
            loaded.Policies,
            FindRole,
            loaded.UnloadedRoleNames,
            FindEntity,
            name => ownersByChildName.TryGetValue(name, out var owner) ? owner.ToString() : null,
            loaded.UnloadedEntityNames,
            FindDataSource,
            loaded.UnloadedDataSourceNames,
            FindProcess,
            loaded.UnloadedProcessNames,
            FindEndpoint,
            loaded.UnloadedEndpointNames,
            references,
            rules,
            moduleReferences,
            declaredModules,
            diagnostics);
        ConnectorChecker.Check(loaded.Connectors, diagnostics);
        EventChecker.Check(
            allEvents,
            FindEntity,
            name => ownersByChildName.TryGetValue(name, out var owner) ? owner.ToString() : null,
            loaded.UnloadedEntityNames,
            references,
            diagnostics);
        EndpointChecker.Check(
            loaded.Endpoints,
            FindEntity,
            name => ownersByChildName.TryGetValue(name, out var owner) ? owner.ToString() : null,
            loaded.UnloadedEntityNames,
            FindProcess,
            loaded.UnloadedProcessNames,
            (field, path, report) => CheckField(field, path, FindEntity, ownersByChildName, loaded.UnloadedEntityNames, moduleReferences, maxFileSize, report),
            references,
            diagnostics);
        TriggerChecker.Check(
            loaded.Triggers,
            FindEntity,
            FindProcess,
            loaded.UnloadedProcessNames,
            FindEvent,
            loaded.UnloadedEventNames,
            loaded.UnloadedEntityNames,
            FindDataSource,
            loaded.UnloadedDataSourceNames,
            rules,
            declaredModules,
            references,
            diagnostics);
        MigrationChecker.Check(loaded.Migrations, FindEntity, loaded.UnloadedEntityNames, references, diagnostics);
        ScenarioChecker.Check(
            loaded.Scenarios,
            FindRole,
            loaded.UnloadedRoleNames,
            FindEntity,
            name => ownersByChildName.TryGetValue(name, out var owner) ? owner.ToString() : null,
            loaded.UnloadedEntityNames,
            loaded.Processes,
            loaded.UnloadedProcessNames,
            FindConnector,
            loaded.UnloadedConnectorNames,
            FindPackage,
            declaredPackageIds,
            references,
            diagnostics);

        if (probe is not null)
        {
            return new CompilationResult(null, DiagnosticOrder.Sort(diagnostics));
        }

        var index = references.Build();
        var result = new CompilationResult(null, DiagnosticOrder.Sort(diagnostics))
        {
            References = index,
            Names = DefinedNamesBuilder.Build(loaded, allEntities, allSequences, allEvents, moduleResources.Rules, texts, FindEntity, index),
            ModulePins = modules,
            PolicyMatrix = PolicyMatrixBuilder.Build(
                loaded.Roles,
                loaded.Policies,
                allEntities,
                ownersByChildName.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                loaded.DataSources,
                loaded.Processes,
                loaded.Endpoints),
        };
        if (result.HasErrors || loaded.Application is null)
        {
            return result;
        }

        // The derived ids apply only now, so a diagnostic in a module file keeps the id written in that file.
        var applicationId = loaded.Application.Id;
        foreach (var entity in moduleResources.Entities)
        {
            entitiesByName[entity.Name] = entity with { Id = ModuleResourceIds.Derive(applicationId, entity.Id) };
        }

        foreach (var sequence in moduleResources.Sequences)
        {
            sequencesByName[sequence.Name] = sequence with { Id = ModuleResourceIds.Derive(applicationId, sequence.Id) };
        }

        foreach (var @event in moduleResources.Events)
        {
            eventsByName[@event.Name] = @event with { Id = ModuleResourceIds.Derive(applicationId, @event.Id) };
        }

        foreach (var dataSource in moduleResources.DataSources)
        {
            dataSourcesByName[dataSource.Name] = dataSource with { Id = ModuleResourceIds.Derive(applicationId, dataSource.Id) };
        }

        foreach (var form in moduleResources.Forms)
        {
            formsByName[form.Name] = form with { Id = ModuleResourceIds.Derive(applicationId, form.Id) };
        }

        var entities = allEntities
            .Select(entity => BuildEntity(entitiesByName[entity.Name], entitiesByName, sequencesByName, RulesFor(entity), IsModuleResource(entity) ? null : declaredModules))
            .ToList();
        var model = new ApplicationModel
        {
            Manifest = loaded.Application,
            TimeZone = timeZone,
            Entities = entities,
            Sites = loaded.Sites.Select(site => BuildSite(site, pagesByName)).ToList(),
            Pages = loaded.Pages.Select(page => BuildPage(page, entitiesByName, pagesByName, dataSourcesByName, formsByName, processesByName)).ToList(),
            Texts = texts,
            Seeds = allSeeds.Select(seed => BuildSeed(seed, entitiesByName, applicationId)).ToList(),
            DataSources = loaded.DataSources.Select(dataSource => BuildDataSource(
                dataSourcesByName[dataSource.Name], entities, IsModuleResource(dataSource) ? [] : rules, IsModuleResource(dataSource) ? null : declaredModules)).ToList(),
            Processes = loaded.Processes.Select(process => ProcessChecker.Build(process, FindEntity, FindForm, FindConnector, FindEvent, FindPackage, FindPage, FindProcess, rules, declaredModules)).ToList(),
            Forms = loaded.Forms.Select(form => BuildForm(formsByName[form.Name], entitiesByName, IsModuleResource(form) ? [] : rules, IsModuleResource(form) ? null : declaredModules)).ToList(),
            Roles = loaded.Roles,
            Policies = loaded.Policies.Select(policy => PolicyChecker.Build(policy, entities, FindDataSource, FindProcess, FindEndpoint, rules, declaredModules)).ToList(),
            Connectors = loaded.Connectors.Select(ConnectorChecker.Build).ToList(),
            Events = allEvents.Select(@event => EventChecker.Build(eventsByName[@event.Name], FindEntity)).ToList(),
            Endpoints = loaded.Endpoints.Select(endpoint => EndpointChecker.Build(endpoint, FindEntity, FindProcess)).ToList(),
            Triggers = loaded.Triggers.Select(trigger => TriggerChecker.Build(trigger, FindEntity, FindEvent, FindProcess, FindDataSource, rules, declaredModules)).ToList(),
            Migrations = loaded.Migrations.Select(migration => MigrationChecker.Build(migration, loaded.Resources.Single(resource => resource.Path == migration.File).Content)).ToList(),
            Packages = packages,
            Modules = modules,
        };

        // The pinned modules' files are stored with the application's own, in ordinal path order.
        IReadOnlyList<ResourceContent> resources = moduleFiles.Count == 0
            ? loaded.Resources
            : [.. loaded.Resources.Concat(moduleFiles).OrderBy(resource => resource.Path, StringComparer.Ordinal)];
        return result with
        {
            Model = model,
            ContentHash = ContentHash.Compute(resources, packages),
            Resources = resources,
        };
    }

    /// <summary>
    /// Checks the text resources, which come in path order: each locale appears once, ignoring
    /// letter case, and every locale has the same keys. A file whose locale is already taken is
    /// left out of the key check, so its drift is not reported on top. Returns every key that
    /// some locale has.
    /// </summary>
    internal static HashSet<string> CheckTexts(IReadOnlyList<TextResource> texts, List<Diagnostic> diagnostics)
    {
        var firstFileByLocale = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var checkedTexts = new List<TextResource>();
        foreach (var text in texts)
        {
            if (firstFileByLocale.TryAdd(text.Locale, text.File))
            {
                checkedTexts.Add(text);
                continue;
            }

            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.DuplicateLocale,
                $"The locale '{text.Locale}' already has its texts in '{firstFileByLocale[text.Locale]}'.",
                text.File,
                "/locale",
                text.Id));
        }

        // Text keys are compared ordinally, as the SPA looks them up.
        var firstTextByKey = new Dictionary<string, TextResource>(StringComparer.Ordinal);
        foreach (var text in checkedTexts)
        {
            foreach (var key in text.Texts.Keys)
            {
                firstTextByKey.TryAdd(key, text);
            }
        }

        foreach (var text in checkedTexts)
        {
            foreach (var (key, holder) in firstTextByKey)
            {
                if (!text.Texts.ContainsKey(key))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LocaleDrift,
                        $"The text key '{key}' is missing; locale '{holder.Locale}' has it.",
                        text.File,
                        "/texts",
                        text.Id));
                }
            }
        }

        return new HashSet<string>(firstTextByKey.Keys, StringComparer.Ordinal);
    }

    /// <summary>Reports a label whose text key no locale has. Unused keys are allowed. A label that resolves gets a <c>label text</c> edge.</summary>
    internal static void CheckTextKey(
        TextReference? label,
        Resource owner,
        string pathPrefix,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        if (label is null)
        {
            return;
        }

        if (!references.TextKeys.Contains(label.TextKey))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MissingTextKey,
                $"No locale has the text key '{label.TextKey}'.",
                owner.File,
                $"{pathPrefix}/textKey",
                owner.Id));
            return;
        }

        references.Add(owner, $"{pathPrefix}/textKey", ReferenceRelations.LabelText, new TextKeyNode(label.TextKey));
    }

    /// <summary>
    /// Checks the <c>valueLabels</c> of an enum field or parameter: every value has a label, no label names
    /// an unknown value, and each label's text key is in some locale. Values are compared exactly. A field
    /// that is not an enum, or an enum without values, is already reported by <c>CheckField</c>.
    /// </summary>
    internal static void CheckValueLabels(
        FieldDefinition field,
        Resource owner,
        string path,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        if (FieldTypes.Parse(field.Type) != FieldType.Enum || field.Values is not { } values || field.ValueLabels is not { } labels)
        {
            return;
        }

        var labelsPath = $"{path}/valueLabels";
        var missing = values.Where(value => !labels.ContainsKey(value)).ToList();
        if (missing.Count > 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.InvalidValueLabels,
                $"'valueLabels' has no label for the {(missing.Count == 1 ? "value" : "values")} {string.Join(", ", missing.Select(value => $"'{value}'"))}.",
                owner.File,
                labelsPath,
                owner.Id));
        }

        var known = new HashSet<string>(values, StringComparer.Ordinal);
        foreach (var (value, label) in labels)
        {
            var valuePath = $"{labelsPath}/{EscapePointerSegment(value)}";
            if (!known.Contains(value))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.InvalidValueLabels,
                    $"'valueLabels' names the value '{value}', which 'values' does not have.",
                    owner.File,
                    valuePath,
                    owner.Id));
                continue;
            }

            CheckTextKey(label, owner, valuePath, references, diagnostics);
        }
    }

    /// <summary>Escapes a text key as an RFC 6901 JSON Pointer segment.</summary>
    private static string EscapePointerSegment(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>
    /// Finds the owner of each child entity: the first child collection field, in entity order and
    /// then field order, whose target names it. A later child collection with the same target is
    /// reported at its target. Returns the owners by child entity name, ignoring letter case.
    /// </summary>
    private static Dictionary<string, ChildOwner> FindChildOwners(
        IReadOnlyList<EntityResource> entities,
        Func<string, EntityResource?> findEntity,
        List<Diagnostic> diagnostics)
    {
        var ownersByChildName = new Dictionary<string, ChildOwner>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            for (var index = 0; index < entity.Fields.Count; index++)
            {
                var field = entity.Fields[index];
                if (FieldTypes.Parse(field.Type) != FieldType.ChildCollection
                    || field.Target is null
                    || findEntity(field.Target) is not { } child)
                {
                    continue;
                }

                if (!ownersByChildName.TryAdd(child.Name, new ChildOwner(entity, index)))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.ChildEntityOwnedTwice,
                        $"The child entity '{child.Name}' is already owned by '{ownersByChildName[child.Name]}'. A child entity has exactly one owner.",
                        entity.File,
                        $"/fields/{index}/target",
                        entity.Id));
                }
            }
        }

        return ownersByChildName;
    }

    private static void CheckEntity(
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        IReadOnlyDictionary<string, ChildOwner> ownersByChildName,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, SequenceResource?> findSequence,
        IReadOnlySet<string> unloadedSequenceNames,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string>? modules,
        long maxFileSize,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, entity.File, path, entity.Id));

        CheckTextKey(entity.Label, entity, "/label", references, diagnostics);

        var childOwner = ownersByChildName.GetValueOrDefault(entity.Name);
        var defaultScope = ExpressionScopes.ForDefault(rules, modules);
        var firstIndexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entity.Fields.Count; index++)
        {
            var field = entity.Fields[index];
            var path = $"/fields/{index}";

            if (!firstIndexByName.TryAdd(field.Name, index))
            {
                var firstIndex = firstIndexByName[field.Name];
                Report(
                    DiagnosticCodes.DuplicateFieldName,
                    $"The field name '{field.Name}' is already used by field '{entity.Fields[firstIndex].Name}' at '/fields/{firstIndex}'.",
                    $"{path}/name");
            }

            CheckTextKey(field.Label, entity, $"{path}/label", references, diagnostics);
            CheckValueLabels(field, entity, path, references, diagnostics);
            CheckField(field, path, findEntity, ownersByChildName, unloadedEntityNames, moduleReferences, maxFileSize, Report);
            AddFieldTarget(entity, field, path, findEntity, references);
            CheckSequence(entity, field, path, childOwner, findSequence, unloadedSequenceNames, moduleReferences, references, Report);
            CheckCreator(field, path, childOwner, Report);
            CheckSearchable(field, path, Report);
            CheckMoney(entity, field, path, Report);
            CheckDefault(entity, field, path, defaultScope, references, Report);

            // A child entity's rows are read and written only through its owner, so it has no collections of its own.
            var type = FieldTypes.Parse(field.Type);
            if (childOwner is not null && type is FieldType.References or FieldType.ChildCollection)
            {
                var kind = type == FieldType.References ? "a references field" : "a child collection";
                Report(
                    DiagnosticCodes.ChildEntityWithChildCollection,
                    $"The field '{field.Name}' is {kind}, but '{entity.Name}' is a child entity owned by '{childOwner}'. A child entity has no child collection or references fields.",
                    $"{path}/type");
            }
        }

        if (entity.DisplayField is { } displayField)
        {
            var field = FindField(entity, displayField);
            if (field is null)
            {
                Report(
                    DiagnosticCodes.InvalidDisplayField,
                    $"The display field '{displayField}' was not found. It must name a required text field of this entity.",
                    "/displayField");
            }
            else if (FieldTypes.Parse(field.Type) != FieldType.Text || field.Required != true)
            {
                var kind = field.Required == true ? $"a required {field.Type}" : $"an optional {field.Type}";
                Report(
                    DiagnosticCodes.InvalidDisplayField,
                    $"The display field must be a required text field, but '{field.Name}' is {kind} field.",
                    "/displayField");
            }
        }

        CheckComputedFields(entity, findEntity, references, Report);
        CheckValidations(entity, findEntity, references, rules, modules, diagnostics, Report);
        CheckIndexes(entity, Report);
    }

    /// <summary>
    /// Checks each declared index: every field names a field of the entity that an index can hold,
    /// and no earlier index lists the same fields in the same order, ignoring letter case.
    /// </summary>
    private static void CheckIndexes(EntityResource entity, Action<string, string, string> report)
    {
        var firstIndexByFields = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entity.Indexes.Count; index++)
        {
            var declared = entity.Indexes[index];
            var path = $"/indexes/{index}";

            for (var position = 0; position < declared.Fields.Count; position++)
            {
                var name = declared.Fields[position];
                var field = FindField(entity, name);
                if (field is null)
                {
                    report(
                        DiagnosticCodes.UnknownIndexField,
                        $"The index field '{name}' names no field of this entity.",
                        $"{path}/fields/{position}");
                }
                else if (!CanIndex(FieldTypes.Parse(field.Type)))
                {
                    report(
                        DiagnosticCodes.InvalidIndexField,
                        $"The index field '{field.Name}' has the type '{field.Type}', which an index cannot hold.",
                        $"{path}/fields/{position}");
                }
            }

            // The names are joined with a comma, which no field name holds.
            var key = string.Join(',', declared.Fields);
            if (!firstIndexByFields.TryAdd(key, index))
            {
                var firstIndex = firstIndexByFields[key];
                report(
                    DiagnosticCodes.DuplicateIndex,
                    $"The index lists the same fields in the same order as the index at '/indexes/{firstIndex}'.",
                    $"{path}/fields");
            }
        }
    }

    // A child collection and a references field have no column, and a file field holds a stored file.
    private static bool CanIndex(FieldType type) =>
        type is not (FieldType.ChildCollection or FieldType.References or FieldType.File);

    /// <summary>
    /// Checks the expression of each computed field of a scalar type: it parses and type-checks to
    /// the field's type over the entity's fields that are not computed and its child collections,
    /// which only aggregates accept. A computed field is not in the scope, so naming one, itself
    /// included, is an unknown name. An aggregate's item expression sees the child row's fields,
    /// computed ones included. A stored value cannot depend on the current time, so <c>now()</c> is
    /// refused. An expression problem is reported at the expression with its own code.
    /// </summary>
    private static void CheckComputedFields(
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        if (entity.Fields.All(field => field.Expression is null))
        {
            return;
        }

        var scope = ExpressionScopes.ForEntity(entity.Fields, includeComputed: false, findEntity: findEntity, references: false);
        for (var index = 0; index < entity.Fields.Count; index++)
        {
            var field = entity.Fields[index];

            // A reference, references field, child collection or file is reported by CheckField, and so is an enum without values.
            var type = FieldTypes.Parse(field.Type);
            if (field.Expression is null
                || type is FieldType.Reference or FieldType.References or FieldType.ChildCollection or FieldType.File
                || (type == FieldType.Enum && field.Values is null))
            {
                continue;
            }

            var parsed = ExpressionParser.Parse(field.Expression);
            var check = parsed.Succeeded
                ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionScopes.TypeOf(field)!, storedValue: true)
                : null;
            var problem = parsed.Succeeded ? check!.Diagnostic : parsed.Diagnostic;
            references.RecordExpression(entity, $"/fields/{index}/expression", check?.Type, problem);
            if (problem is not null)
            {
                report(problem.Code, problem.Message, $"/fields/{index}/expression");
            }
        }
    }

    /// <summary>
    /// Checks each validation: its message is a known text key, its <c>field</c> names a field of
    /// the entity, and its expression parses and type-checks as a boolean over the entity's fields,
    /// its child collections, which only aggregates accept, and the named rules. An aggregate's
    /// item expression sees only the child row's fields. An expression problem is reported at the
    /// expression with its own code.
    /// </summary>
    private static void CheckValidations(
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        ReferenceIndexBuilder references,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string>? modules,
        List<Diagnostic> diagnostics,
        Action<string, string, string> report)
    {
        if (entity.Validations.Count == 0)
        {
            return;
        }

        var scope = ExpressionScopes.ForEntity(entity.Fields, rules: rules, findEntity: findEntity, modules: modules);
        for (var index = 0; index < entity.Validations.Count; index++)
        {
            var validation = entity.Validations[index];
            var path = $"/validations/{index}";

            CheckTextKey(validation.Message, entity, $"{path}/message", references, diagnostics);

            if (FindField(entity, validation.Field) is null)
            {
                report(
                    DiagnosticCodes.UnknownValidationField,
                    $"The validation field '{validation.Field}' names no field of this entity.",
                    $"{path}/field");
            }

            var parsed = ExpressionParser.Parse(validation.Expression);
            ExpressionDiagnostic? problem = parsed.Succeeded ? null : parsed.Diagnostic;
            ExpressionType? type = null;
            if (parsed.Succeeded)
            {
                var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean);
                // Module rule calls add no edges, as for module rules.
                if (check.Succeeded && !IsModuleResource(entity))
                {
                    references.AddRuleCalls(entity, $"{path}/expression", check);
                }

                problem = check.Diagnostic;
                type = check.Type;
            }

            references.RecordExpression(entity, $"{path}/expression", type, problem);

            if (problem is not null)
            {
                report(problem.Code, problem.Message, $"{path}/expression");
            }
        }
    }

    /// <summary>
    /// Checks the field's <c>sequence</c>: only an optional text field that clients would otherwise
    /// write, on an entity that is not a child entity, can have one, and it names a loaded sequence.
    /// All four reasons share one path, so only the first is reported. A field that is not text gets
    /// only that diagnostic, as with any type-specific property.
    /// </summary>
    private static void CheckSequence(
        Resource owner,
        FieldDefinition field,
        string path,
        ChildOwner? childOwner,
        Func<string, SequenceResource?> findSequence,
        IReadOnlySet<string> unloadedSequenceNames,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        if (field.Sequence is not { } sequence)
        {
            return;
        }

        var isText = FieldTypes.Parse(field.Type) == FieldType.Text;
        var problem = !isText ? $"'sequence' applies only to text fields, not to {field.Type} fields."
            : field.Required == true ? "A field with a 'sequence' cannot be required: the server fills it when the record is created."
            : field.Expression is not null ? "A field with a 'sequence' cannot have an 'expression': it is either numbered or computed."
            : childOwner is not null ? $"A field with a 'sequence' cannot belong to a child entity, but this entity is owned by '{childOwner}'."
            : null;
        if (problem is not null)
        {
            report(DiagnosticCodes.InvalidConstraint, problem, $"{path}/sequence");
        }

        if (isText && findSequence(sequence) is { } found)
        {
            references.Add(owner, $"{path}/sequence", ReferenceRelations.FieldSequence, ResourceNode.Of(found));
        }

        // A sequence file that is in the folder but was not loaded is not reported again; that
        // file's own diagnostics already are.
        if (isText
            && findSequence(sequence) is null
            && !unloadedSequenceNames.Contains(sequence)
            && !moduleReferences.ReportUndeclared(sequence, $"{path}/sequence", report))
        {
            report(
                DiagnosticCodes.UnknownSequence,
                $"The sequence '{sequence}' was not found. No loaded sequence has that name.",
                $"{path}/sequence");
        }
    }

    /// <summary>
    /// Checks a field's <c>creator</c> property: it applies only to text fields, and a creator
    /// field is optional, neither computed nor numbered, and not on a child entity. Only the first
    /// problem is reported.
    /// </summary>
    private static void CheckCreator(
        FieldDefinition field, string path, ChildOwner? childOwner, Action<string, string, string> report)
    {
        if (field.Creator is not { } creator)
        {
            return;
        }

        var problem = FieldTypes.Parse(field.Type) != FieldType.Text ? $"'creator' applies only to text fields, not to {field.Type} fields."
            : !creator ? null
            : field.Required == true ? "A 'creator' field cannot be required: the server fills it when a signed-in user creates the record."
            : field.Expression is not null ? "A 'creator' field cannot have an 'expression': the server sets it to the user who created the record."
            : field.Sequence is not null ? "A 'creator' field cannot have a 'sequence': the server sets it to the user who created the record."
            : childOwner is not null ? $"A 'creator' field cannot belong to a child entity, but this entity is owned by '{childOwner}'."
            : null;
        if (problem is not null)
        {
            report(DiagnosticCodes.InvalidConstraint, problem, $"{path}/creator");
        }
    }

    /// <summary>Checks a field's <c>searchable</c> property: it applies only to text fields, whatever its value.</summary>
    private static void CheckSearchable(FieldDefinition field, string path, Action<string, string, string> report)
    {
        if (field.Searchable is not null && FieldTypes.Parse(field.Type) != FieldType.Text)
        {
            report(
                DiagnosticCodes.InvalidConstraint,
                $"'searchable' applies only to text fields, not to {field.Type} fields.",
                $"{path}/searchable");
        }
    }

    /// <summary>
    /// Checks a money field's currency: exactly one of <c>currency</c>, a known ISO 4217 code, or
    /// <c>currencyField</c>, a text or enum field of the entity. The defaults must also fit together:
    /// the scale from the currency's minor units cannot exceed the precision.
    /// </summary>
    private static void CheckMoney(
        EntityResource entity, FieldDefinition field, string path, Action<string, string, string> report)
    {
        if (FieldTypes.Parse(field.Type) != FieldType.Money)
        {
            return;
        }

        if (field.Currency is null && field.CurrencyField is null)
        {
            report(
                DiagnosticCodes.InvalidMoneyCurrency,
                "A money field must name its currency in 'currency' or 'currencyField'.",
                path);
            return;
        }

        if (field.Currency is not null && field.CurrencyField is not null)
        {
            report(
                DiagnosticCodes.InvalidMoneyCurrency,
                "A money field names its currency in 'currency' or in 'currencyField', not in both.",
                $"{path}/currencyField");
            return;
        }

        int minorUnits;
        if (field.Currency is { } code)
        {
            if (!Currencies.TryGetMinorUnits(code, out minorUnits))
            {
                report(
                    DiagnosticCodes.InvalidMoneyCurrency,
                    $"'{code}' is not a known ISO 4217 currency code. A code is three upper case letters, such as 'VND'.",
                    $"{path}/currency");
                return;
            }
        }
        else
        {
            var currencyFieldName = field.CurrencyField!;
            var currencyField = FindField(entity, currencyFieldName);
            if (currencyField is null
                || FieldTypes.Parse(currencyField.Type) is not (FieldType.Text or FieldType.Enum))
            {
                report(
                    DiagnosticCodes.InvalidMoneyCurrency,
                    currencyField is null
                        ? $"'currencyField' names '{currencyFieldName}', but '{entity.Name}' has no field with that name."
                        : $"'currencyField' names the {currencyField.Type} field '{currencyField.Name}'. It must be a text or an enum field.",
                    $"{path}/currencyField");
                return;
            }

            minorUnits = MoneyDefaultScale;
        }

        // An explicit scale is compared with the precision by CheckField.
        if (field.Scale is null && field.Precision is { } precision and >= 1 and <= MaxPrecision && minorUnits > precision)
        {
            report(
                DiagnosticCodes.InvalidConstraint,
                $"The default 'scale' is {minorUnits}, the minor units of the currency, but 'precision' is {precision}. Set 'precision' to at least {minorUnits} or set 'scale'.",
                $"{path}/precision");
        }
    }

    /// <summary>
    /// Checks a field's <c>default</c>: it applies only to text, integer, decimal, money, boolean,
    /// date, date-time and enum fields, and not together with an <c>expression</c>, a <c>sequence</c> or a
    /// <c>creator</c>. Only the first problem is reported, and then the expression is not checked.
    /// Otherwise the expression parses and type-checks to the field's type with no record in scope,
    /// so it may use literals, the named rules and <c>now()</c>. An expression problem is reported
    /// at the expression with its own code.
    /// </summary>
    private static void CheckDefault(
        EntityResource entity,
        FieldDefinition field,
        string path,
        ExpressionScope scope,
        ReferenceIndexBuilder references,
        Action<string, string, string> report)
    {
        if (field.Default is not { } defaultExpression)
        {
            return;
        }

        var type = FieldTypes.Parse(field.Type);
        var problem = type is FieldType.Reference or FieldType.References or FieldType.ChildCollection or FieldType.File
                ? $"'default' applies only to text, integer, decimal, money, boolean, date, date-time and enum fields, not to {field.Type} fields."
            : field.Expression is not null ? "A field with a 'default' cannot have an 'expression': it is either computed or has a default."
            : field.Sequence is not null ? "A field with a 'default' cannot have a 'sequence': the server fills it from the sequence."
            : field.Creator == true ? "A field with a 'default' cannot be a 'creator' field: the server sets it to the user who created the record."
            : null;
        if (problem is not null)
        {
            report(DiagnosticCodes.InvalidConstraint, problem, $"{path}/default");
            return;
        }

        // CheckField already reports an enum without values.
        if (type == FieldType.Enum && field.Values is null)
        {
            return;
        }

        var parsed = ExpressionParser.Parse(defaultExpression);
        ExpressionDiagnostic? expressionProblem = parsed.Succeeded ? null : parsed.Diagnostic;
        ExpressionType? checkedType = null;
        if (parsed.Succeeded)
        {
            var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionScopes.TypeOf(field)!);
            // Module rule calls add no edges, as for module rules.
            if (check.Succeeded && !IsModuleResource(entity))
            {
                references.AddRuleCalls(entity, $"{path}/default", check);
            }

            expressionProblem = check.Diagnostic;
            checkedType = check.Type;
        }

        references.RecordExpression(entity, $"{path}/default", checkedType, expressionProblem);

        if (expressionProblem is not null)
        {
            report(expressionProblem.Code, expressionProblem.Message, $"{path}/default");
        }
    }

    /// <summary>Finds a field by name, ignoring letter case, as the model does.</summary>
    internal static FieldDefinition? FindField(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the <c>field target</c> edge of a reference, references or child collection field whose target entity resolves.</summary>
    internal static void AddFieldTarget(
        Resource owner, FieldDefinition field, string path, Func<string, EntityResource?> findEntity, ReferenceIndexBuilder references)
    {
        if (field.Target is { } target
            && FieldTypes.Parse(field.Type) is FieldType.Reference or FieldType.References or FieldType.ChildCollection
            && findEntity(target) is { } targetEntity)
        {
            references.Add(owner, $"{path}/target", ReferenceRelations.FieldTarget, ResourceNode.Of(targetEntity));
        }
    }

    /// <summary>
    /// Says why <paramref name="limit"/> is not a literal of the limited field type
    /// <paramref name="type"/>, in the record API's forms, or returns null when it is one. Integer
    /// and decimal limits are JSON numbers, and a decimal limit must fit a .NET decimal.
    /// </summary>
    private static string? LimitProblem(FieldType type, JsonElement limit) => type switch
    {
        FieldType.Integer when limit.ValueKind != JsonValueKind.Number || !limit.TryGetInt64(out _) =>
            "must be an integral number within the signed 64-bit range.",
        FieldType.Decimal or FieldType.Money when limit.ValueKind != JsonValueKind.Number || !TryReadDecimalLimit(limit, out _) =>
            "must be a number that fits a decimal of at most 28 digits.",
        FieldType.Date when limit.ValueKind != JsonValueKind.String || !DateLiterals.TryParseDate(limit.GetString()!, out _) =>
            "must be a date in the form yyyy-MM-dd.",
        FieldType.DateTime when limit.ValueKind != JsonValueKind.String || !DateLiterals.TryParseDateTime(limit.GetString()!, out _) =>
            "must be an RFC 3339 date-time with an offset and at most 6 fraction digits.",
        _ => null,
    };

    /// <summary>The limit as the model keeps it: the text of a date or date-time, and the number text as written.</summary>
    private static string LimitText(JsonElement limit) =>
        limit.ValueKind == JsonValueKind.String ? limit.GetString()! : limit.GetRawText();

    private static bool TryReadDecimalLimit(JsonElement limit, out decimal value) =>
        decimal.TryParse(limit.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>Compares two limits that <see cref="LimitProblem"/> accepted for <paramref name="type"/>.</summary>
    private static int CompareLimits(FieldType type, JsonElement left, JsonElement right) => type switch
    {
        FieldType.Integer => left.GetInt64().CompareTo(right.GetInt64()),
        FieldType.Decimal or FieldType.Money => ReadDecimalLimit(left).CompareTo(ReadDecimalLimit(right)),
        FieldType.Date => DateOnly.ParseExact(left.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            .CompareTo(DateOnly.ParseExact(right.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
        _ => ReadDateTimeLimit(left).CompareTo(ReadDateTimeLimit(right)),
    };

    private static decimal ReadDecimalLimit(JsonElement limit) =>
        TryReadDecimalLimit(limit, out var value) ? value : throw new InvalidOperationException("The limit is not a decimal.");

    private static DateTimeOffset ReadDateTimeLimit(JsonElement limit) =>
        DateLiterals.TryParseDateTime(limit.GetString()!, out var value) ? value : throw new InvalidOperationException("The limit is not a date-time.");

    private static void CheckField(
        FieldDefinition field,
        string path,
        Func<string, EntityResource?> findEntity,
        IReadOnlyDictionary<string, ChildOwner> ownersByChildName,
        IReadOnlySet<string> unloadedEntityNames,
        ModuleReferences moduleReferences,
        long maxFileSize,
        Action<string, string, string> report)
    {
        var type = FieldTypes.Parse(field.Type);

        // A child collection has no column, and a missing collection already means no rows.
        if (type == FieldType.ChildCollection)
        {
            if (field.Required is not null)
            {
                report(DiagnosticCodes.InvalidConstraint, "'required' does not apply to child-collection fields.", $"{path}/required");
            }

            if (field.Unique is not null)
            {
                report(DiagnosticCodes.InvalidConstraint, "'unique' does not apply to child-collection fields.", $"{path}/unique");
            }

            if (field.Sensitive is not null)
            {
                report(DiagnosticCodes.InvalidConstraint, "'sensitive' does not apply to child-collection fields.", $"{path}/sensitive");
            }
        }

        // A references field has no column, and it is not a single value to make unique.
        if (type == FieldType.References && field.Unique is not null)
        {
            report(DiagnosticCodes.InvalidConstraint, "'unique' does not apply to references fields.", $"{path}/unique");
        }

        // Reports a type-specific property on a field of another type. Returns whether it fits.
        bool Fits(string property, string fittingTypeNames, params FieldType[] fittingTypes)
        {
            if (fittingTypes.Contains(type))
            {
                return true;
            }

            report(
                DiagnosticCodes.InvalidConstraint,
                $"'{property}' applies only to {fittingTypeNames} fields, not to {field.Type} fields.",
                $"{path}/{property}");
            return false;
        }

        if (field.MaxLength is { } maxLength
            && Fits("maxLength", "text", FieldType.Text)
            && maxLength is < 1 or > MaxTextLength)
        {
            report(
                DiagnosticCodes.InvalidConstraint,
                $"'maxLength' must be between 1 and {MaxTextLength}, but is {maxLength}.",
                $"{path}/maxLength");
        }

        if (field.MaxSize is { } maxSize
            && Fits("maxSize", "file", FieldType.File)
            && (maxSize < 1 || maxSize > maxFileSize))
        {
            report(
                DiagnosticCodes.InvalidFileMaxSize,
                $"'maxSize' must be between 1 and {maxFileSize} bytes, the file size limit, but is {maxSize}.",
                $"{path}/maxSize");
        }

        if (field.Accept is { } accept && Fits("accept", "file", FieldType.File))
        {
            for (var index = 0; index < accept.Count; index++)
            {
                if (!AcceptEntryPattern().IsMatch(accept[index]))
                {
                    report(
                        DiagnosticCodes.InvalidFileAccept,
                        $"The 'accept' entry '{accept[index]}' must be a media type 'type/subtype' or 'type/*'. '*/*' and parameters such as ';charset=' are not allowed.",
                        $"{path}/accept/{index}");
                }
            }
        }

        if (type == FieldType.File && field.Unique is not null)
        {
            report(DiagnosticCodes.FileFieldNotAllowed, "'unique' does not apply to file fields.", $"{path}/unique");
        }

        var precisionValid = false;
        if (field.Precision is { } precision && Fits("precision", "decimal and money", FieldType.Decimal, FieldType.Money))
        {
            precisionValid = precision is >= 1 and <= MaxPrecision;
            if (!precisionValid)
            {
                report(
                    DiagnosticCodes.InvalidConstraint,
                    $"'precision' must be between 1 and {MaxPrecision}, but is {precision}.",
                    $"{path}/precision");
            }
        }

        if (field.Scale is { } scale && Fits("scale", "decimal and money", FieldType.Decimal, FieldType.Money))
        {
            if (type == FieldType.Money)
            {
                // A money field's precision has a default, so scale can stand alone.
                var moneyPrecision = field.Precision ?? MoneyDefaultPrecision;
                if ((field.Precision is null || precisionValid) && scale > moneyPrecision)
                {
                    report(
                        DiagnosticCodes.InvalidConstraint,
                        $"'scale' must be between 0 and the precision ({moneyPrecision}), but is {scale}.",
                        $"{path}/scale");
                }
            }
            else if (field.Precision is null)
            {
                report(
                    DiagnosticCodes.InvalidConstraint,
                    "'scale' can only be set together with 'precision'.",
                    $"{path}/scale");
            }
            else if (precisionValid && scale > field.Precision)
            {
                // An invalid precision is reported on its own; comparing against it would only add noise.
                report(
                    DiagnosticCodes.InvalidConstraint,
                    $"'scale' must be between 0 and the precision ({field.Precision}), but is {scale}.",
                    $"{path}/scale");
            }
        }

        // A limit on a field the server sets would do nothing, because a request never carries its value.
        bool FitsLimit(string property, string fittingTypeNames, params FieldType[] fittingTypes)
        {
            if (!Fits(property, fittingTypeNames, fittingTypes))
            {
                return false;
            }

            if (field.Expression is null && field.Sequence is null && field.Creator != true)
            {
                return true;
            }

            report(
                DiagnosticCodes.InvalidConstraint,
                $"'{property}' cannot be set on a field the server sets.",
                $"{path}/{property}");
            return false;
        }

        const string LimitedTypeNames = "integer, decimal, money, date and date-time";
        FieldType[] limitedTypes = [FieldType.Integer, FieldType.Decimal, FieldType.Money, FieldType.Date, FieldType.DateTime];
        var minValid = field.Min is { } min && FitsLimit("min", LimitedTypeNames, limitedTypes) && CheckLimit("min", min);
        var maxValid = field.Max is { } max && FitsLimit("max", LimitedTypeNames, limitedTypes) && CheckLimit("max", max);
        if (minValid && maxValid && CompareLimits(type, field.Min!.Value, field.Max!.Value) > 0)
        {
            report(
                DiagnosticCodes.InvalidConstraint,
                $"'max' must be at least 'min' ({LimitText(field.Min.Value)}), but is {LimitText(field.Max.Value)}.",
                $"{path}/max");
        }

        if (field.MinLength is { } minLength && FitsLimit("minLength", "text", FieldType.Text))
        {
            if (minLength is < 1 or > MaxTextLength)
            {
                report(
                    DiagnosticCodes.InvalidConstraint,
                    $"'minLength' must be between 1 and {MaxTextLength}, but is {minLength}.",
                    $"{path}/minLength");
            }
            else if (field.MaxLength is { } declaredMaxLength && minLength > declaredMaxLength)
            {
                report(
                    DiagnosticCodes.InvalidConstraint,
                    $"'minLength' must be at most the 'maxLength' ({declaredMaxLength}), but is {minLength}.",
                    $"{path}/minLength");
            }
        }

        // Reports a limit that is not a literal of the field's type. Returns whether it is one.
        bool CheckLimit(string property, JsonElement limit)
        {
            var problem = LimitProblem(type, limit);
            if (problem is null)
            {
                return true;
            }

            report(DiagnosticCodes.InvalidConstraint, $"'{property}' {problem}", $"{path}/{property}");
            return false;
        }

        if (field.Currency is not null)
        {
            Fits("currency", "money", FieldType.Money);
        }

        if (field.CurrencyField is not null)
        {
            Fits("currencyField", "money", FieldType.Money);
        }

        if (field.Target is { } target)
        {
            if (Fits("target", "reference, references and child-collection", FieldType.Reference, FieldType.References, FieldType.ChildCollection))
            {
                if (findEntity(target) is { } targetEntity)
                {
                    // A child collection's target is checked against the other owners by FindChildOwners.
                    if (type is FieldType.Reference or FieldType.References)
                    {
                        var kind = type == FieldType.Reference ? "referenced" : "the target of a references field";
                        if (ownersByChildName.TryGetValue(targetEntity.Name, out var owner))
                        {
                            report(
                                DiagnosticCodes.ReferenceToChildEntity,
                                $"The target entity '{targetEntity.Name}' is a child entity owned by '{owner}'. A child entity cannot be {kind}.",
                                $"{path}/target");
                        }
                        else if (targetEntity.DisplayField is null)
                        {
                            // Every record a reference points to is shown by its name.
                            report(
                                DiagnosticCodes.ReferenceTargetWithoutDisplayField,
                                $"The target entity '{targetEntity.Name}' has no 'displayField', so its records have no name to show.",
                                $"{path}/target");
                        }
                    }
                }
                else if (!unloadedEntityNames.Contains(target)
                    && !moduleReferences.ReportUndeclared(target, $"{path}/target", report))
                {
                    // A target naming an entity file that is in the folder but was not loaded is not
                    // reported again; that file's own diagnostics already are.
                    report(
                        DiagnosticCodes.UnknownReferenceTarget,
                        $"The target entity '{target}' was not found. No loaded entity has that name.",
                        $"{path}/target");
                }
            }
        }
        else if (type == FieldType.Reference)
        {
            report(DiagnosticCodes.MissingTypeProperty, "A reference field must name its target entity in 'target'.", path);
        }
        else if (type == FieldType.References)
        {
            report(DiagnosticCodes.MissingTypeProperty, "A references field must name its target entity in 'target'.", path);
        }
        else if (type == FieldType.ChildCollection)
        {
            report(DiagnosticCodes.MissingTypeProperty, "A child collection field must name its child entity in 'target'.", path);
        }

        if (field.Values is not null)
        {
            Fits("values", "enum", FieldType.Enum);
        }
        else if (type == FieldType.Enum)
        {
            report(DiagnosticCodes.MissingTypeProperty, "An enum field must list its 'values'.", path);
        }

        if (field.ValueLabels is not null)
        {
            Fits("valueLabels", "enum", FieldType.Enum);
        }

        // The expression itself is checked by CheckComputedFields, against the other fields.
        if (field.Expression is not null)
        {
            Fits(
                "expression",
                "text, integer, decimal, money, boolean, date, date-time and enum",
                FieldType.Text,
                FieldType.Integer,
                FieldType.Decimal,
                FieldType.Money,
                FieldType.Boolean,
                FieldType.Date,
                FieldType.DateTime,
                FieldType.Enum);

            if (field.Required == true)
            {
                report(DiagnosticCodes.InvalidConstraint, "'required' cannot be set on a computed field.", $"{path}/required");
            }
        }
    }

    /// <summary>
    /// Checks the seeds, which come in path order: each names a loaded entity, and every seed record
    /// id is used once across all seeds, compared as a UUID. A record cannot set a file field, in its
    /// own values or in a child row's. The other values are not checked here; the startup step
    /// checks them with the record API's rules before it inserts any record.
    /// </summary>
    private static void CheckSeeds(
        IReadOnlyList<SeedResource> seeds,
        IReadOnlySet<string> unloadedEntityNames,
        Func<string, EntityResource?> findEntity,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        var firstFileById = new Dictionary<Guid, string>();
        foreach (var seed in seeds)
        {
            var entity = findEntity(seed.Entity);
            if (entity is not null)
            {
                references.Add(seed, "/entity", ReferenceRelations.SeedEntity, ResourceNode.Of(entity));
            }
            // An entity file that was not loaded because of its own errors is not reported again.
            else if (!unloadedEntityNames.Contains(seed.Entity))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownSeedEntity,
                    $"The entity '{seed.Entity}' was not found. No loaded entity has that name.",
                    seed.File,
                    "/entity",
                    seed.Id));
            }

            for (var index = 0; index < seed.Records.Count; index++)
            {
                if (entity is not null)
                {
                    CheckSeedFiles(seed, entity, seed.Records[index].Values, $"/records/{index}/values", findEntity, diagnostics);
                }

                var id = seed.Records[index].Id;
                if (!firstFileById.TryAdd(id, seed.File))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.DuplicateSeedRecordId,
                        $"The seed record id '{id:D}' is already used in '{firstFileById[id]}'.",
                        seed.File,
                        $"/records/{index}/id",
                        seed.Id));
                }
            }
        }
    }

    /// <summary>
    /// Reports each property of a seed record's <paramref name="values"/> that names a file field of <paramref name="entity"/>, and
    /// does the same for the rows of a child collection among them. A name resolves ignoring letter case.
    /// </summary>
    private static void CheckSeedFiles(
        SeedResource seed,
        EntityResource entity,
        JsonElement values,
        string path,
        Func<string, EntityResource?> findEntity,
        List<Diagnostic> diagnostics)
    {
        if (values.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in values.EnumerateObject())
        {
            if (FindField(entity, property.Name) is not { } field)
            {
                continue;
            }

            var type = FieldTypes.Parse(field.Type);
            if (type == FieldType.File)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.FileFieldNotAllowed,
                    $"The field '{field.Name}' is a file field. A seed record cannot set a file.",
                    seed.File,
                    $"{path}/{property.Name}",
                    seed.Id));
            }
            else if (type == FieldType.ChildCollection
                && property.Value.ValueKind == JsonValueKind.Array
                && field.Target is { } target
                && findEntity(target) is { } child)
            {
                var row = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    CheckSeedFiles(seed, child, item, $"{path}/{property.Name}/{row}", findEntity, diagnostics);
                    row++;
                }
            }
        }
    }

    /// <summary>
    /// Checks the data sources: each names a loaded entity, each projected <c>path</c> resolves as
    /// <see cref="ResolvePath"/> describes, projected names are unique, and the default <c>sort</c>
    /// names a projected field that does not end at a reference. An entity that is a child entity
    /// is reported at <c>/entity</c>, naming its owner. Entity and field names
    /// resolve ignoring letter case; projected names compare exactly, as the query <c>sort</c> does.
    /// Each parameter name differs from the entity's fields, from <c>page</c>, <c>pageSize</c>,
    /// <c>sort</c> and <c>search</c> and from earlier parameters, ignoring letter case. Its type properties and label
    /// are checked as an entity field's. The <c>filter</c> parses, type-checks as a boolean over the
    /// entity's fields, the parameters, paths through reference fields and the named rules, and
    /// translates to SQL, with each rule call inlined. Its child collections are in the type check's scope, so an aggregate
    /// type-checks and is then reported as outside the SQL subset. Its first problem
    /// is reported at <c>/filter</c> with its expression code. It is not checked while a parameter
    /// has a diagnostic. An <c>aggregate</c> groups by projected names, its measure names differ
    /// from the group fields and from each other, and each measure is valid as
    /// <see cref="CheckMeasure"/> describes. A grouped data source's sort names a group field that
    /// is not a reference, or a measure. No group field or sort names a file field. A data source with
    /// <c>search</c> projects at least one field that <see cref="IsSearched"/> accepts, unless a projected path
    /// is already reported.
    /// </summary>
    private static void CheckDataSources(
        ApplicationLoadResult loaded,
        Func<string, EntityResource?> findEntity,
        IReadOnlyDictionary<string, ChildOwner> ownersByChildName,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string>? modules,
        List<Diagnostic> diagnostics)
    {
        foreach (var dataSource in loaded.DataSources)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, dataSource.File, path, dataSource.Id));

            var entity = findEntity(dataSource.Entity);
            if (entity is null)
            {
                // An entity file that was not loaded because of its own errors is not reported again.
                if (!loaded.UnloadedEntityNames.Contains(dataSource.Entity)
                    && !moduleReferences.ReportUndeclared(dataSource.Entity, "/entity", Report))
                {
                    Report(
                        DiagnosticCodes.UnknownDataSourceEntity,
                        $"The entity '{dataSource.Entity}' was not found. No loaded entity has that name.",
                        "/entity");
                }

                // The paths and the sort cannot be checked without the entity.
                continue;
            }

            references.Add(dataSource, "/entity", ReferenceRelations.DataSourceEntity, ResourceNode.Of(entity));

            // A child entity's rows are read only through its owner's child collection. The rest of
            // the data source is still checked against the entity.
            if (ownersByChildName.TryGetValue(entity.Name, out var owner))
            {
                Report(
                    DiagnosticCodes.DataSourceOverChildEntity,
                    $"The entity '{entity.Name}' is a child entity owned by '{owner}'. A data source queries a root entity. Child rows are read through the owner's child collection.",
                    "/entity");
            }

            var fieldsByName = new Dictionary<string, FieldDefinition?>(StringComparer.Ordinal);
            var hopsByName = new Dictionary<string, IReadOnlyList<FieldDefinition>>(StringComparer.Ordinal);
            var searchable = false;
            var unresolvedPath = false;
            for (var index = 0; index < dataSource.Fields.Count; index++)
            {
                var projected = dataSource.Fields[index];
                var pathProblem = ResolvePath(
                    entity,
                    projected.Path,
                    findEntity,
                    target => references.Add(dataSource, $"/fields/{index}/path", ReferenceRelations.FieldPath, ResourceNode.Of(target)),
                    out var hops);

                // A path that does not resolve, or ends at a child collection or a references field, has no column. It is
                // stored as an invalid path for the sort check.
                var field = pathProblem is null && hops.Count > 0 ? hops[^1] : null;
                if (field is not null)
                {
                    hopsByName.TryAdd(projected.Name, hops);
                    searchable |= IsSearched(hops, entity);
                }
                else
                {
                    unresolvedPath = true;
                }

                if (!fieldsByName.TryAdd(projected.Name, field))
                {
                    Report(
                        DiagnosticCodes.DuplicateDataSourceFieldName,
                        $"The name '{projected.Name}' is already used by an earlier projected field.",
                        $"/fields/{index}/name");
                }

                if (pathProblem is not null)
                {
                    Report(DiagnosticCodes.InvalidDataSourceFieldPath, pathProblem, $"/fields/{index}/path");
                }
            }

            // A broken path may have been the searchable one, so it is the only diagnostic then.
            if (dataSource.Search == true && !unresolvedPath && !searchable)
            {
                Report(
                    DiagnosticCodes.DataSourceSearchWithoutField,
                    "The data source takes a search, but no projected field is searchable. Project a field marked 'searchable', or the entity's display field directly.",
                    "/search");
            }

            // Group field names compare exactly, as projected names do. An unknown one is still kept,
            // so a measure or the sort that repeats it is not reported again.
            var groupFields = new HashSet<string>(StringComparer.Ordinal);
            var measureNames = new HashSet<string>(StringComparer.Ordinal);
            if (dataSource.Aggregate is { } aggregate)
            {
                for (var index = 0; index < aggregate.GroupBy.Count; index++)
                {
                    var name = aggregate.GroupBy[index];
                    groupFields.Add(name);
                    if (!fieldsByName.ContainsKey(name))
                    {
                        Report(
                            DiagnosticCodes.UnknownDataSourceGroupField,
                            $"The group field '{name}' must name a projected field.",
                            $"/aggregate/groupBy/{index}");
                    }
                    else if (fieldsByName[name] is { } groupField && FieldTypes.Parse(groupField.Type) == FieldType.File)
                    {
                        Report(
                            DiagnosticCodes.FileFieldNotAllowed,
                            $"The group field '{name}' is a file field. A data source cannot group by a file.",
                            $"/aggregate/groupBy/{index}");
                    }
                }

                for (var index = 0; index < aggregate.Measures.Count; index++)
                {
                    var measure = aggregate.Measures[index];
                    var path = $"/aggregate/measures/{index}";
                    if (groupFields.Contains(measure.Name) || !measureNames.Add(measure.Name))
                    {
                        Report(
                            DiagnosticCodes.DuplicateDataSourceMeasureName,
                            $"The measure name '{measure.Name}' is already used by a group field or an earlier measure. Both are keys of one grouped row.",
                            $"{path}/name");
                    }

                    if (CheckMeasure(measure, fieldsByName) is ({ } message, { } problemPath))
                    {
                        Report(DiagnosticCodes.InvalidDataSourceMeasure, message, path + problemPath);
                    }
                    else if (CheckMeasureCurrency(measure, aggregate.GroupBy, hopsByName, entity, findEntity) is { } currencyMessage)
                    {
                        Report(DiagnosticCodes.UngroupedMoneyCurrency, currencyMessage, $"{path}/field");
                    }
                }
            }

            if (dataSource.Sort is { } sort)
            {
                // A grouped data source sorts by its group fields and measures, any other by its
                // projected fields.
                var name = sort.StartsWith('-') ? sort[1..] : sort;
                var grouped = dataSource.Aggregate is not null;
                var isMeasure = measureNames.Contains(name);
                if (grouped ? !groupFields.Contains(name) && !isMeasure : !fieldsByName.ContainsKey(name))
                {
                    Report(
                        DiagnosticCodes.InvalidDataSourceSort,
                        grouped
                            ? $"The sort '{sort}' must name a group field or a measure, optionally preceded by '-'."
                            : $"The sort '{sort}' must name a projected field, optionally preceded by '-'.",
                        "/sort");
                }
                else if (!isMeasure
                    && fieldsByName.TryGetValue(name, out var field)
                    && field is not null
                    && FieldTypes.Parse(field.Type) == FieldType.Reference)
                {
                    Report(
                        DiagnosticCodes.InvalidDataSourceSort,
                        $"The sort '{sort}' names the reference field '{name}'. A data source cannot sort by a reference.",
                        "/sort");
                }
                else if (!isMeasure
                    && fieldsByName.TryGetValue(name, out var fileField)
                    && fileField is not null
                    && FieldTypes.Parse(fileField.Type) == FieldType.File)
                {
                    Report(
                        DiagnosticCodes.FileFieldNotAllowed,
                        $"The sort '{sort}' names the file field '{name}'. A data source cannot sort by a file.",
                        "/sort");
                }
            }

            var countBeforeParameters = diagnostics.Count;
            var parameterNames = new HashSet<string>(["page", "pageSize", "sort", "search"], StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < dataSource.Parameters.Count; index++)
            {
                var parameter = dataSource.Parameters[index];
                var path = $"/parameters/{index}";
                if (FindField(entity, parameter.Name) is not null)
                {
                    Report(
                        DiagnosticCodes.InvalidDataSourceParameterName,
                        $"The parameter name '{parameter.Name}' is also a field of the entity '{entity.Name}'. The filter reads both as plain names.",
                        $"{path}/name");
                }
                else if (!parameterNames.Add(parameter.Name))
                {
                    Report(
                        DiagnosticCodes.InvalidDataSourceParameterName,
                        $"The parameter name '{parameter.Name}' is reserved or already used by an earlier parameter. 'page', 'pageSize', 'sort' and 'search' are reserved, ignoring letter case.",
                        $"{path}/name");
                }

                CheckTextKey(parameter.Label, dataSource, $"{path}/label", references, diagnostics);
                var parameterField = new FieldDefinition
                {
                    Name = parameter.Name,
                    Type = parameter.Type,
                    Required = parameter.Required,
                    Label = parameter.Label,
                    Values = parameter.Values,
                    ValueLabels = parameter.ValueLabels,
                    Target = parameter.Target,
                };
                CheckField(
                    parameterField,
                    path,
                    findEntity,
                    ownersByChildName,
                    loaded.UnloadedEntityNames,
                    moduleReferences,
                    long.MaxValue,
                    Report);
                CheckValueLabels(parameterField, dataSource, path, references, diagnostics);
                AddFieldTarget(dataSource, parameterField, path, findEntity, references);
            }

            // A parameter without a usable type would only add noise to the filter's diagnostics.
            if (diagnostics.Count == countBeforeParameters && dataSource.Filter is { } filter)
            {
                var problem = CheckFilter(
                    filter,
                    // Module rules are a separate step, so a module data source sees none and cannot call an application rule.
                    ExpressionScopes.ForDataSource(
                        entity.Fields,
                        dataSource.Parameters,
                        findEntity,
                        IsModuleResource(dataSource) ? [] : rules,
                        IsModuleResource(dataSource) ? null : modules),
                    policyFilter: false,
                    out var check);
                if (check is not null)
                {
                    references.AddRuleCalls(dataSource, "/filter", check);
                }

                references.RecordExpression(dataSource, "/filter", check?.Type, problem);
                if (problem is not null)
                {
                    Report(problem.Code, problem.Message, "/filter");
                }
            }
        }
    }

    /// <summary>
    /// The problem of a measure and its path below the measure, or null when it is valid.
    /// <c>count</c> takes no field. Every other function needs a projected field: <c>sum</c> and <c>avg</c>
    /// of an integer, a decimal or a money field, <c>min</c> and <c>max</c> of an integer, a decimal, a
    /// money field, a date or a date-time, and <c>countDistinct</c> of any field except a file. A
    /// projected field with an invalid path is not reported again.
    /// </summary>
    private static (string Message, string Path)? CheckMeasure(
        DataSourceMeasureDefinition measure, Dictionary<string, FieldDefinition?> fieldsByName)
    {
        var function = ParseAggregateFunction(measure.Function);
        if (function == AggregateFunction.Count)
        {
            return measure.Field is null ? null : ("The measure function 'count' counts rows and takes no 'field'.", "");
        }

        if (measure.Field is not { } name)
        {
            return ($"The measure function '{measure.Function}' needs a 'field' that names a projected field.", "");
        }

        if (!fieldsByName.TryGetValue(name, out var field))
        {
            return ($"The measure field '{name}' must name a projected field.", "/field");
        }

        if (field is null)
        {
            return null;
        }

        var type = FieldTypes.Parse(field.Type);
        var accepted = function switch
        {
            AggregateFunction.Sum or AggregateFunction.Avg => type == FieldType.Integer || FieldTypes.HoldsDecimal(type),
            AggregateFunction.CountDistinct => type != FieldType.File,
            _ => type == FieldType.Integer || FieldTypes.HoldsDecimal(type) || type is FieldType.Date or FieldType.DateTime,
        };
        return accepted
            ? null
            : (
                $"The measure function '{measure.Function}' cannot take the {field.Type} field '{name}'. "
                    + "'sum' and 'avg' take an integer, a decimal or a money field. 'min' and 'max' take an integer, a decimal, a money field, a date or a date-time. "
                    + "'countDistinct' takes any field except a file.",
                "/field");
    }

    /// <summary>
    /// The functions that add, average or compare amounts, so a money field with a <c>currencyField</c>
    /// must be grouped by that currency. A <c>countDistinct</c> counts values and mixes no currencies.
    /// </summary>
    private static readonly HashSet<AggregateFunction> _currencyMixingFunctions =
        [AggregateFunction.Sum, AggregateFunction.Avg, AggregateFunction.Min, AggregateFunction.Max];

    /// <summary>
    /// The message of a <c>sum</c>, <c>avg</c>, <c>min</c> or <c>max</c> over a money field with a
    /// <c>currencyField</c> when no group field reaches that currency field on the same record,
    /// through the same reference fields. Null when the measure needs no currency group, or when the
    /// currency field is itself invalid, which <see cref="CheckMoney"/> already reports.
    /// </summary>
    private static string? CheckMeasureCurrency(
        DataSourceMeasureDefinition measure,
        IReadOnlyList<string> groupBy,
        Dictionary<string, IReadOnlyList<FieldDefinition>> hopsByName,
        EntityResource entity,
        Func<string, EntityResource?> findEntity)
    {
        if (!_currencyMixingFunctions.Contains(ParseAggregateFunction(measure.Function))
            || measure.Field is not { } name
            || !hopsByName.TryGetValue(name, out var hops)
            || FieldTypes.Parse(hops[^1].Type) != FieldType.Money
            || hops[^1].CurrencyField is not { } currencyFieldName)
        {
            return null;
        }

        var owner = hops.Count == 1 ? entity : hops[^2].Target is { } target ? findEntity(target) : null;
        if (owner is null
            || FindField(owner, currencyFieldName) is not { } currencyField
            || FieldTypes.Parse(currencyField.Type) is not (FieldType.Text or FieldType.Enum))
        {
            return null;
        }

        if (groupBy.Any(group => hopsByName.TryGetValue(group, out var groupHops)
            && ReachesCurrency(groupHops, hops, currencyField)))
        {
            return null;
        }

        var prefix = string.Concat(hops.Take(hops.Count - 1).Select(hop => hop.Name + "."));
        return $"The measure '{measure.Name}' adds, averages or compares the money field '{name}', whose currency comes from '{currencyFieldName}'. "
            + $"Group by a projected field with the path '{prefix}{currencyFieldName}' so that each total has one currency.";
    }

    private static bool ReachesCurrency(
        IReadOnlyList<FieldDefinition> group, IReadOnlyList<FieldDefinition> money, FieldDefinition currencyField)
    {
        if (group.Count != money.Count || !string.Equals(group[^1].Name, currencyField.Name, StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 0; index < money.Count - 1; index++)
        {
            if (!ReferenceEquals(group[index], money[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static AggregateFunction ParseAggregateFunction(string function) =>
        function switch
        {
            "count" => AggregateFunction.Count,
            "sum" => AggregateFunction.Sum,
            "min" => AggregateFunction.Min,
            "max" => AggregateFunction.Max,
            "avg" => AggregateFunction.Avg,
            "countDistinct" => AggregateFunction.CountDistinct,
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "The JSON Schema allows only count, sum, min, max, avg and countDistinct."),
        };

    /// <summary>
    /// The first problem of a data source or policy filter: in parsing, in type checking over the
    /// entity's fields, its child collections, a data source's parameters, paths through reference
    /// fields and the named rules, or outside the SQL subset with each rule call inlined. Only a
    /// policy filter can call <c>currentUser()</c>. A filter that calls a rule without a checked
    /// expression, directly or through the rules it calls, is not translated: that rule already has
    /// its own diagnostic, as for a validation. <paramref name="check"/> is the type check when it
    /// succeeds, and null otherwise.
    /// </summary>
    internal static ExpressionDiagnostic? CheckFilter(
        string filter, ExpressionScope scope, bool policyFilter, out ExpressionCheckResult? check)
    {
        check = null;
        var parsed = ExpressionParser.Parse(filter);
        if (!parsed.Succeeded)
        {
            return parsed.Diagnostic;
        }

        var typeCheck = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean, policyFilter: policyFilter);
        if (!typeCheck.Succeeded)
        {
            return typeCheck.Diagnostic;
        }

        check = typeCheck;
        return CallsRuleWithoutBody(typeCheck, new HashSet<ExpressionRule>(ReferenceEqualityComparer.Instance))
            ? null
            : SqlTranslator.Translate(parsed.Expression, path => string.Join('.', path), typeCheck).Diagnostic;
    }

    /// <summary>
    /// Whether a checked expression calls a rule without a checked body, directly or through the
    /// rules it calls. Each rule is visited once, so a rule called many times does not multiply the walk.
    /// </summary>
    private static bool CallsRuleWithoutBody(ExpressionCheckResult check, HashSet<ExpressionRule> visited) =>
        check.RuleCalls.Values.Any(rule =>
            visited.Add(rule) && (rule.BodyCheck is null || CallsRuleWithoutBody(rule.BodyCheck, visited)));

    /// <summary>
    /// Resolves a projected <c>path</c> from the entity <paramref name="root"/>, ignoring letter
    /// case. Every name before the last is a reference field, the path takes at most
    /// <see cref="ExpressionLimits.MaxHops"/> hops, and the last name is a field that is not a child
    /// collection or a references field. Returns the problem's message, or null when the path
    /// resolves or reaches an entity that was not loaded, which is reported at that entity's own
    /// file. When the path resolves, <paramref name="hops"/> holds one field per name. It is empty when an entity was
    /// not loaded. Each reference field of the path whose target resolves, the last name included,
    /// is passed to <paramref name="reach"/>, even when the path fails further on.
    /// </summary>
    internal static string? ResolvePath(
        EntityResource root,
        string path,
        Func<string, EntityResource?> findEntity,
        Action<EntityResource> reach,
        out IReadOnlyList<FieldDefinition> hops)
    {
        var found = new List<FieldDefinition>();
        hops = found;
        var names = path.Split('.');
        if (names.Length - 1 > ExpressionLimits.MaxHops)
        {
            return $"The path '{path}' takes {names.Length - 1} hops, at most {ExpressionLimits.MaxHops} are allowed.";
        }

        var entity = root;
        for (var index = 0; index < names.Length; index++)
        {
            var field = FindField(entity, names[index]);
            if (field is null)
            {
                return $"The path '{path}' must name a field of the entity '{entity.Name}'. '{names[index]}' is not one.";
            }

            found.Add(field);
            var type = FieldTypes.Parse(field.Type);
            if (index == names.Length - 1)
            {
                if (type == FieldType.ChildCollection)
                {
                    return $"The path '{path}' names the child collection '{field.Name}'. A data source cannot project a child collection.";
                }

                if (type == FieldType.References)
                {
                    return $"The path '{path}' names the references field '{field.Name}'. A data source cannot project a references field.";
                }

                if (type == FieldType.Reference && field.Target is not null && findEntity(field.Target) is { } lastTarget)
                {
                    reach(lastTarget);
                }

                return null;
            }

            if (type != FieldType.Reference)
            {
                return $"The path '{path}' goes through '{field.Name}', which is not a reference field of '{entity.Name}'. A path can only go through reference fields.";
            }

            // A target that was not loaded because of its own errors is not reported again.
            if (field.Target is null || findEntity(field.Target) is not { } target)
            {
                hops = [];
                return null;
            }

            reach(target);
            entity = target;
        }

        return null;
    }

    private static EntityModel BuildEntity(
        EntityResource entity,
        Dictionary<string, EntityResource> entitiesByName,
        Dictionary<string, SequenceResource> sequencesByName,
        IEnumerable<ExpressionRule> rules,
        IReadOnlySet<string>? modules)
    {
        // The child models are not built yet, so both scopes come from the definitions.
        EntityResource? FindEntity(string name) => entitiesByName.GetValueOrDefault(name);
        var inputScope = ExpressionScopes.ForEntity(entity.Fields, includeComputed: false, findEntity: FindEntity, references: false);
        var defaultScope = ExpressionScopes.ForDefault(rules, modules);
        var fields = entity.Fields.Select(field => BuildField(entity, field, inputScope, defaultScope, entitiesByName, sequencesByName)).ToList();
        var scope = ExpressionScopes.ForEntity(entity.Fields, rules: rules, findEntity: FindEntity, modules: modules);
        return new EntityModel
        {
            Id = entity.Id,
            Name = entity.Name,
            Label = entity.Label,
            File = entity.File,
            Fields = fields,
            DisplayField = entity.DisplayField is null ? null : FindField(entity, entity.DisplayField)!.Name,
            Validations = entity.Validations
                .Select(validation => ValidationModel.Compile(
                    validation.Expression, scope, validation.Message, FindField(entity, validation.Field)!.Name))
                .ToList(),
            Indexes = entity.Indexes
                .Select(declared => new EntityIndexModel(
                    [.. declared.Fields.Select(name => FindField(entity, name)!.Name)],
                    declared.Unique ?? false))
                .ToList(),
        };
    }

    private static FieldModel BuildField(
        EntityResource entity,
        FieldDefinition field,
        ExpressionScope inputScope,
        ExpressionScope defaultScope,
        Dictionary<string, EntityResource> entitiesByName,
        Dictionary<string, SequenceResource> sequencesByName)
    {
        var type = FieldTypes.Parse(field.Type);
        var target = field.Target is null ? null : entitiesByName[field.Target];
        var sequence = field.Sequence is null ? null : sequencesByName[field.Sequence];
        return new FieldModel
        {
            Name = field.Name,
            Type = type,
            Label = field.Label,
            Required = field.Required ?? false,
            Unique = field.Unique ?? false,
            MaxLength = field.MaxLength,
            MinLength = field.MinLength,
            Min = field.Min is { } min ? LimitText(min) : null,
            Max = field.Max is { } max ? LimitText(max) : null,
            Precision = type == FieldType.Money ? field.Precision ?? MoneyDefaultPrecision : field.Precision,
            Scale = type == FieldType.Money ? field.Scale ?? MoneyDefaultScaleOf(field) : field.Scale,
            MaxSize = field.MaxSize,
            Accept = field.Accept,
            Currency = field.Currency,
            CurrencyField = field.CurrencyField is null ? null : FindField(entity, field.CurrencyField)!.Name,
            Values = field.Values,
            ValueLabels = field.ValueLabels,
            Target = target is null ? null : new EntityReference(target.Id, target.Name),
            TargetDisplayField = type is FieldType.Reference or FieldType.References && target?.DisplayField is { } displayField ? FindField(target, displayField)?.Name : null,
            Computed = field.Expression is null
                ? null
                : ComputedFieldModel.Compile(field.Expression, inputScope, ExpressionScopes.TypeOf(field)!),
            Sequence = sequence is null ? null : new SequenceModel(sequence.Id, sequence.Name, sequence.Format),
            Creator = field.Creator == true,
            Searchable = field.Searchable == true,
            Default = field.Default is null
                ? null
                : ExpressionModel.Compile(field.Default, defaultScope, ExpressionScopes.TypeOf(field)!),
            Sensitive = field.Sensitive == true,
        };
    }

    /// <summary>The scale of a money field that does not set one: the currency's minor units, or 2 with a currency field.</summary>
    private static int MoneyDefaultScaleOf(FieldDefinition field) =>
        field.Currency is { } code && Currencies.TryGetMinorUnits(code, out var minorUnits) ? minorUnits : MoneyDefaultScale;

    private static SiteModel BuildSite(SiteResource site, Dictionary<string, PageResource> pagesByName) =>
        new()
        {
            Id = site.Id,
            Name = site.Name,
            Path = site.Path,
            Title = site.Title,
            Locales = site.Locales,
            File = site.File,
            Navigation = site.Navigation
                .Select(entry => new NavigationModel(PageReferenceTo(pagesByName[entry.Page]), entry.Label))
                .ToList(),
        };

    private static PageModel BuildPage(
        PageResource page,
        Dictionary<string, EntityResource> entitiesByName,
        Dictionary<string, PageResource> pagesByName,
        Dictionary<string, DataSourceResource> dataSourcesByName,
        Dictionary<string, FormResource> formsByName,
        Dictionary<string, ProcessResource> processesByName)
    {
        List<WidgetModel> BuildWidgets(IReadOnlyList<WidgetDefinition> widgets) => [.. widgets.Select(BuildWidget)];

        // The checker has resolved every process and page an action names, so the lookups always succeed.
        // A navigate action reads paths from the entity of its widget, which is null over a data source, and for a toolbar and a kpi.
        IReadOnlyList<WidgetActionModel> BuildActions(IReadOnlyList<WidgetActionDefinition>? actions, EntityResource? root) =>
            actions is null || actions.Count == 0 ? [] : [.. actions.Select(action => BuildAction(action, root))];

        WidgetActionModel BuildAction(WidgetActionDefinition action, EntityResource? root)
        {
            if (action.Type == "delete")
            {
                return new DeleteActionModel(action.Label) { VisibleWhen = action.VisibleWhen };
            }

            if (action.Type == "startProcess")
            {
                var process = processesByName[action.Process!];
                return new StartProcessActionModel(new ProcessReference(process.Id, process.Name), action.Label)
                {
                    VisibleWhen = action.VisibleWhen,
                };
            }

            // The checker has matched each key to a parameter of the target, ignoring letter case. The model
            // keys them by the declared name, so a client never fixes the case itself.
            var target = pagesByName[action.Page!];
            var parameters = action.Parameters is not { Count: > 0 }
                ? NavigateActionModel.NoParameters
                : action.Parameters.ToDictionary(
                    pair => target.Parameters.First(parameter => string.Equals(parameter.Name, pair.Key, StringComparison.OrdinalIgnoreCase)).Name,
                    pair => pair.Value);
            var paths = root is null
                ? NavigateActionModel.NoPaths
                : (IReadOnlyList<string>)[.. (action.Parameters?.Values ?? []).Concat(action.Record is null ? [] : [action.Record])
                    .SelectMany(expression => NavigatePaths.Of(expression, root, entitiesByName.GetValueOrDefault))
                    .Distinct(StringComparer.OrdinalIgnoreCase)];
            return new NavigateActionModel(PageReferenceTo(target), action.Label)
            {
                Parameters = parameters,
                Paths = paths.Count == 0 ? NavigateActionModel.NoPaths : paths,
                Record = action.Record,
                VisibleWhen = action.VisibleWhen,
            };
        }

        // The checker has matched each key to a parameter of the data source and each value to a parameter of the
        // page, ignoring letter case. The model uses the declared names, so a client never fixes the case itself.
        IReadOnlyDictionary<string, string> BuildParameters(WidgetDefinition widget)
        {
            if (widget.Parameters is not { Count: > 0 } || widget.DataSource is null)
            {
                return NavigateActionModel.NoParameters;
            }

            var source = dataSourcesByName[widget.DataSource];
            return widget.Parameters.ToDictionary(
                pair => source.Parameters.First(parameter => string.Equals(parameter.Name, pair.Key, StringComparison.OrdinalIgnoreCase)).Name,
                pair => page.Parameters.First(parameter => string.Equals(parameter.Name, pair.Value, StringComparison.OrdinalIgnoreCase)).Name);
        }

        WidgetModel BuildWidget(WidgetDefinition widget)
        {
            // A container holds widgets and no reference. A leaf keeps the shared empty lists, so equal leaves stay equal.
            switch (WidgetTypes.Parse(widget.Type))
            {
                case WidgetType.Columns:
                    return new WidgetModel(WidgetType.Columns, null)
                    {
                        Columns = [.. widget.Columns!.Select(column => new WidgetColumnModel(column.Width, BuildWidgets(column.Widgets)))],
                    };
                case WidgetType.Tabs:
                    return new WidgetModel(WidgetType.Tabs, null)
                    {
                        Tabs = [.. widget.Tabs!.Select(tab => new WidgetTabModel(tab.Label, BuildWidgets(tab.Widgets)))],
                    };
                case WidgetType.Section:
                    return new WidgetModel(WidgetType.Section, null)
                    {
                        Title = widget.Title,
                        Collapsed = widget.Collapsed ?? false,
                        Widgets = BuildWidgets(widget.Widgets!),
                    };
            }

            // A checked widget names exactly one of an entity, a data source and a form. A
            // form widget over a form shows the form's entity.
            var form = widget.Form is null ? null : formsByName[widget.Form];
            var entityName = widget.Entity ?? form?.Entity;
            var entity = entityName is null ? null : entitiesByName[entityName];
            var dataSource = widget.DataSource is null ? null : dataSourcesByName[widget.DataSource];
            var inspected = widget.Process is null ? null : processesByName[widget.Process];
            return new WidgetModel(
                WidgetTypes.Parse(widget.Type),
                entity is null ? null : new EntityReference(entity.Id, entity.Name),
                dataSource is null ? null : new DataSourceReference(dataSource.Id, dataSource.Name),
                form is null ? null : new FormReference(form.Id, form.Name),
                inspected is null ? null : new ProcessReference(inspected.Id, inspected.Name))
            {
                // Each action is named as its process or page declares it. A widget without
                // actions keeps the shared empty list, so equal widgets stay equal.
                Actions = BuildActions(widget.Actions, entity),
                RowActions = BuildActions(widget.RowActions, entity),
                ToolbarActions = BuildActions(widget.ToolbarActions, null),
                Parameters = BuildParameters(widget),
                Kpi = BuildKpi(widget, dataSource),
                Chart = BuildChart(widget, dataSource),
                Detail = BuildDetail(widget),
            };
        }

        // The checker has matched the record to a parameter of the page, ignoring letter case. The model keeps the
        // declared name, so a client never fixes the case itself.
        DetailModel? BuildDetail(WidgetDefinition widget) =>
            widget.Record is null
                ? null
                : new DetailModel(page.Parameters.First(parameter => string.Equals(parameter.Name, widget.Record, StringComparison.OrdinalIgnoreCase)).Name);

        // The checker has matched each measure to one of the data source's, ignoring letter case. The model
        // keeps the declared name, so a client never fixes the case itself.
        KpiModel? BuildKpi(WidgetDefinition widget, DataSourceResource? source)
        {
            if (widget.Measure is null || source is null)
            {
                return null;
            }

            string Declared(string measure) =>
                source.Aggregate!.Measures.First(candidate => string.Equals(candidate.Name, measure, StringComparison.OrdinalIgnoreCase)).Name;

            return new KpiModel(
                Declared(widget.Measure),
                widget.Label!,
                widget.Comparison is { } comparison ? new KpiComparisonModel(Declared(comparison.Measure), comparison.Label) : null,
                widget.Navigate is { } navigate ? (NavigateActionModel)BuildAction(navigate, null) : null);
        }

        // The checker has matched the category and each measure to the data source's, ignoring letter case. The model
        // keeps the declared names, so a client never fixes the case itself.
        ChartModel? BuildChart(WidgetDefinition widget, DataSourceResource? source)
        {
            if (widget.Measures is not { } measures || source is null)
            {
                return null;
            }

            var aggregate = source.Aggregate!;
            return new ChartModel(
                Enum.Parse<ChartKind>(widget.Kind!, ignoreCase: true),
                widget.Title!,
                aggregate.GroupBy[0],
                [.. measures.Select(measure => new ChartMeasureModel(
                    aggregate.Measures.First(candidate => string.Equals(candidate.Name, measure.Name, StringComparison.OrdinalIgnoreCase)).Name,
                    measure.Label))]);
        }

        return new()
        {
            Id = page.Id,
            Name = page.Name,
            Title = page.Title,
            File = page.File,
            Parameters = [.. page.Parameters.Select(parameter => new PageParameterModel(
                parameter.Name,
                FieldTypes.Parse(parameter.Type),
                parameter.Required ?? false,
                parameter.Values,
                parameter.Target is not null && entitiesByName.GetValueOrDefault(parameter.Target) is { } target ? new EntityReference(target.Id, target.Name) : null))],
            Widgets = BuildWidgets(page.Widgets),
        };
    }

    /// <summary>
    /// Builds a checked form, with each field named as its entity declares it. Each condition is
    /// compiled over the entity's fields and the named rules. A step names its sections as the
    /// sections declare them.
    /// </summary>
    private static FormModel BuildForm(
        FormResource form, Dictionary<string, EntityResource> entitiesByName, IEnumerable<ExpressionRule> rules, IReadOnlySet<string>? modules)
    {
        var entity = entitiesByName[form.Entity];
        var scope = ExpressionScopes.ForEntity(entity.Fields, rules: rules, findEntity: name => entitiesByName.GetValueOrDefault(name), modules: modules);

        ExpressionModel? Compile(string? condition) =>
            condition is null ? null : ExpressionModel.Compile(condition, scope, ExpressionType.Boolean);

        // A checked step names only sections of the form.
        string SectionName(string name) =>
            form.Sections.First(section => string.Equals(section.Name, name, StringComparison.OrdinalIgnoreCase)).Name!;

        return new FormModel
        {
            Id = form.Id,
            Name = form.Name,
            File = form.File,
            Entity = new EntityReference(entity.Id, entity.Name),
            Sections = form.Sections
                .Select(section => new FormSectionModel(
                    section.Title,
                    section.Fields
                        .Select(field => new FormFieldModel(
                            FindField(entity, field.Field)!.Name,
                            field.ReadOnly ?? false,
                            Compile(field.VisibleWhen),
                            Compile(field.RequiredWhen),
                            Compile(field.ReadOnlyWhen)))
                        .ToList(),
                    section.Name,
                    Compile(section.VisibleWhen)))
                .ToList(),
            Steps = form.Steps is null
                ? []
                : [.. form.Steps.Select(step => new FormStepModel(step.Title, [.. step.Sections.Select(SectionName)]))],
        };
    }

    private static SeedModel BuildSeed(SeedResource seed, Dictionary<string, EntityResource> entitiesByName, Guid applicationId)
    {
        var entity = entitiesByName[seed.Entity];
        return new SeedModel
        {
            Id = IsModuleResource(seed) ? ModuleResourceIds.Derive(applicationId, seed.Id) : seed.Id,
            Name = seed.Name,
            File = seed.File,
            Entity = new EntityReference(entity.Id, entity.Name),
            Sync = seed.Sync,
            Records = seed.Records,
        };
    }

    /// <summary>
    /// Whether a data source's search matches a projected path: it ends at a <c>searchable</c> text field,
    /// or it is the root's display field projected directly.
    /// </summary>
    private static bool IsSearched(IReadOnlyList<FieldDefinition> hops, EntityResource entity) =>
        FieldTypes.Parse(hops[^1].Type) == FieldType.Text
        && (hops[^1].Searchable == true
            || (hops.Count == 1 && string.Equals(hops[0].Name, entity.DisplayField, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Builds a checked data source over the built entities, so each projected path is a list of the
    /// entities' own field models, and its filter is compiled against the built entity's fields, the
    /// parameters, paths through reference fields and the named rules.
    /// </summary>
    private static DataSourceModel BuildDataSource(
        DataSourceResource dataSource, IReadOnlyList<EntityModel> entities, IEnumerable<ExpressionRule> rules, IReadOnlySet<string>? modules)
    {
        EntityModel? Find(string name) =>
            entities.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        var entity = Find(dataSource.Entity)!;
        var descending = dataSource.Sort?.StartsWith('-') == true;
        var parameters = dataSource.Parameters
            .Select(parameter => new DataSourceParameterModel(
                parameter.Name,
                FieldTypes.Parse(parameter.Type),
                parameter.Required ?? false,
                parameter.Label,
                parameter.Values,
                parameter.Target is not null && Find(parameter.Target) is { } target ? new EntityReference(target.Id, target.Name) : null,
                parameter.ValueLabels))
            .ToList();
        var fields = dataSource.Fields
            .Select(field =>
            {
                var path = new List<FieldModel>();
                var current = entity;
                foreach (var name in field.Path.Split('.'))
                {
                    current.TryGetField(name, out var fieldModel);
                    path.Add(fieldModel!);
                    current = fieldModel!.Target is { } target ? Find(target.Name)! : current;
                }

                return new DataSourceFieldModel(field.Name, path);
            })
            .ToList();

        // A checked aggregate names projected fields by their exact names.
        DataSourceFieldModel Projected(string name) =>
            fields.First(field => string.Equals(field.Name, name, StringComparison.Ordinal));

        // The compile check has accepted this data source's search, so a field matches by the same rule.
        var searchFields = dataSource.Search == true
            ? fields
                .Where(field => field.Field.Type == FieldType.Text
                    && (field.Field.Searchable
                        || (field.Path.Count == 1 && string.Equals(field.Field.Name, entity.DisplayField, StringComparison.OrdinalIgnoreCase))))
                .DistinctBy(field => string.Join('.', field.Path.Select(step => step.Name)), StringComparer.Ordinal)
                .ToList()
            : [];

        return new DataSourceModel
        {
            Id = dataSource.Id,
            Name = dataSource.Name,
            File = dataSource.File,
            Entity = new EntityReference(entity.Id, entity.Name),
            Fields = fields,
            Parameters = parameters,
            Filter = dataSource.Filter is { } filter
                ? ExpressionModel.Compile(filter, ExpressionScopes.ForDataSource(entity.Fields, parameters, Find, rules, modules), ExpressionType.Boolean)
                : null,
            Aggregate = dataSource.Aggregate is { } aggregate
                ? new DataSourceAggregateModel(
                    aggregate.GroupBy.Select(Projected).ToList(),
                    aggregate.Measures
                        .Select(measure => new DataSourceMeasureModel(
                            measure.Name,
                            ParseAggregateFunction(measure.Function),
                            measure.Field is null ? null : Projected(measure.Field)))
                        .ToList())
                : null,
            Sort = dataSource.Sort is { } sort ? new DataSourceSortModel(descending ? sort[1..] : sort, descending) : null,
            PageSize = dataSource.PageSize ?? DataSourceModel.DefaultPageSize,
            Search = dataSource.Search == true,
            SearchFields = searchFields,
        };
    }

    private static PageReference PageReferenceTo(PageResource page) => new(page.Id, page.Name);

    /// <summary>The child collection field that owns a child entity, shown as <c>Entity.field</c>.</summary>
    private sealed record ChildOwner(EntityResource Entity, int FieldIndex)
    {
        public override string ToString() => $"{Entity.Name}.{Entity.Fields[FieldIndex].Name}";
    }
}
