using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;
using Axis.Expressions;
using Axis.Expressions.Diagnostics;
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
public static class ApplicationCompiler
{
    /// <summary>The largest length PostgreSQL accepts for <c>varchar(n)</c>.</summary>
    public const int MaxTextLength = 10_485_760;

    /// <summary>The largest precision PostgreSQL accepts for <c>numeric(p, s)</c>.</summary>
    public const int MaxPrecision = 1000;

    /// <summary>
    /// Compiles the folder at <paramref name="folderPath"/> against <paramref name="installedPackages"/>
    /// and <paramref name="installedModules"/>. Null means none is installed.
    /// </summary>
    public static CompilationResult Compile(
        string folderPath,
        IReadOnlyCollection<PackageVersion>? installedPackages = null,
        IReadOnlyCollection<InstalledModule>? installedModules = null) =>
        Compile(folderPath, installedPackages, installedModules, collectReferences: true);

    /// <summary>Lets the performance tests measure what the reference index adds to a compile.</summary>
    internal static CompilationResult Compile(
        string folderPath,
        IReadOnlyCollection<PackageVersion>? installedPackages,
        IReadOnlyCollection<InstalledModule>? installedModules,
        bool collectReferences) =>
        Compile(ApplicationLoader.Load(folderPath), installedPackages ?? [], installedModules ?? [], collectReferences);

    /// <summary>
    /// Compiles the files of an application held in memory, such as a publish snapshot, as the folder overload compiles a
    /// folder: the paths are the application's own, relative with <c>/</c>, and the pinned modules come from
    /// <paramref name="installedModules"/>. Null means none is installed. Unlike the overload for the resources of a stored
    /// release, an entry under <c>modules/</c> is not a release copy of a module.
    /// </summary>
    /// <exception cref="ArgumentException">A path appears more than once.</exception>
    public static CompilationResult CompileFiles(
        IReadOnlyList<ResourceContent> files,
        IReadOnlyCollection<PackageVersion>? installedPackages,
        IReadOnlyCollection<InstalledModule>? installedModules)
    {
        ArgumentNullException.ThrowIfNull(files);

        return Compile(ApplicationLoader.Load(files), installedPackages ?? [], installedModules ?? [], collectReferences: true);
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
        ExpressionProbe probe)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(probe);

        return Compile(ApplicationLoader.Load(files), installedPackages ?? [], installedModules ?? [], collectReferences: false, probe);
    }

    /// <summary>
    /// Compiles resources held in memory, such as the stored resources of a release, against
    /// <paramref name="installedPackages"/>. Null means no package is installed. Entries under
    /// <c>modules/&lt;id&gt;/</c> are the release's own copies of its pinned modules, and the
    /// application compiles against those copies alone.
    /// </summary>
    /// <exception cref="ArgumentException">A path appears more than once.</exception>
    public static CompilationResult Compile(IReadOnlyList<ResourceContent> resources, IReadOnlyCollection<PackageVersion>? installedPackages = null)
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
        var result = Compile(ApplicationLoader.Load(applicationResources), installedPackages ?? [], copies, collectReferences: true);
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
        var moduleResources = new ModuleResources([], [], [], [], [], [], [], [], new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
        if (loaded.Application is { } manifest)
        {
            packages = PackageChecker.Check(manifest, installedPackages, diagnostics, references);
            (modules, moduleFiles, moduleResources) = ModuleChecker.Check(manifest, installedModules, diagnostics, references);
            declaredPackageIds = new HashSet<string>(manifest.Packages.Select(package => package.Id), StringComparer.Ordinal);
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
                diagnostics);
        }

        PresentationChecker.Check(
            loaded,
            FindEntity,
            FindPage,
            FindDataSource,
            FindForm,
            FindProcess,
            (field, path, report) => CheckField(field, path, FindEntity, ownersByChildName, loaded.UnloadedEntityNames, moduleReferences, report),
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
            (field, path, report) => CheckField(field, path, FindEntity, ownersByChildName, loaded.UnloadedEntityNames, moduleReferences, report),
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
            Entities = entities,
            Sites = loaded.Sites.Select(site => BuildSite(site, pagesByName)).ToList(),
            Pages = loaded.Pages.Select(page => BuildPage(page, entitiesByName, pagesByName, dataSourcesByName, formsByName, processesByName)).ToList(),
            Texts = texts,
            Seeds = allSeeds.Select(seed => BuildSeed(seed, entitiesByName, applicationId)).ToList(),
            DataSources = loaded.DataSources.Select(dataSource => BuildDataSource(
                dataSourcesByName[dataSource.Name], entities, IsModuleResource(dataSource) ? [] : rules, IsModuleResource(dataSource) ? null : declaredModules)).ToList(),
            Processes = loaded.Processes.Select(process => ProcessChecker.Build(process, FindEntity, FindForm, FindConnector, FindEvent, FindPackage, rules, declaredModules)).ToList(),
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
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, entity.File, path, entity.Id));

        CheckTextKey(entity.Label, entity, "/label", references, diagnostics);

        var childOwner = ownersByChildName.GetValueOrDefault(entity.Name);
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
            CheckField(field, path, findEntity, ownersByChildName, unloadedEntityNames, moduleReferences, Report);
            AddFieldTarget(entity, field, path, findEntity, references);
            CheckSequence(entity, field, path, childOwner, findSequence, unloadedSequenceNames, moduleReferences, references, Report);
            CheckCreator(field, path, childOwner, Report);

            // A child entity's rows are read and written only through its owner, so they point nowhere else.
            var type = FieldTypes.Parse(field.Type);
            if (childOwner is not null && type is FieldType.Reference or FieldType.ChildCollection)
            {
                var kind = type == FieldType.Reference ? "a reference" : "a child collection";
                Report(
                    DiagnosticCodes.ChildEntityWithReference,
                    $"The field '{field.Name}' is {kind}, but '{entity.Name}' is a child entity owned by '{childOwner}'. A child entity has no reference or child collection fields.",
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
    }

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

        var scope = ExpressionScopes.ForEntity(entity.Fields, includeComputed: false, findEntity: findEntity);
        for (var index = 0; index < entity.Fields.Count; index++)
        {
            var field = entity.Fields[index];

            // A reference or child collection is reported by CheckField, and so is an enum without values.
            var type = FieldTypes.Parse(field.Type);
            if (field.Expression is null
                || type is FieldType.Reference or FieldType.ChildCollection
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

    /// <summary>Finds a field by name, ignoring letter case, as the model does.</summary>
    internal static FieldDefinition? FindField(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the <c>field target</c> edge of a reference or child collection field whose target entity resolves.</summary>
    internal static void AddFieldTarget(
        Resource owner, FieldDefinition field, string path, Func<string, EntityResource?> findEntity, ReferenceIndexBuilder references)
    {
        if (field.Target is { } target
            && FieldTypes.Parse(field.Type) is FieldType.Reference or FieldType.ChildCollection
            && findEntity(target) is { } targetEntity)
        {
            references.Add(owner, $"{path}/target", ReferenceRelations.FieldTarget, ResourceNode.Of(targetEntity));
        }
    }

    private static void CheckField(
        FieldDefinition field,
        string path,
        Func<string, EntityResource?> findEntity,
        IReadOnlyDictionary<string, ChildOwner> ownersByChildName,
        IReadOnlySet<string> unloadedEntityNames,
        ModuleReferences moduleReferences,
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

        var precisionValid = false;
        if (field.Precision is { } precision && Fits("precision", "decimal", FieldType.Decimal))
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

        if (field.Scale is { } scale && Fits("scale", "decimal", FieldType.Decimal))
        {
            if (field.Precision is null)
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

        if (field.Target is { } target)
        {
            if (Fits("target", "reference and child-collection", FieldType.Reference, FieldType.ChildCollection))
            {
                if (findEntity(target) is { } targetEntity)
                {
                    // A child collection's target is checked against the other owners by FindChildOwners.
                    if (type == FieldType.Reference)
                    {
                        if (ownersByChildName.TryGetValue(targetEntity.Name, out var owner))
                        {
                            report(
                                DiagnosticCodes.ReferenceToChildEntity,
                                $"The target entity '{targetEntity.Name}' is a child entity owned by '{owner}'. A child entity cannot be referenced.",
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

        // The expression itself is checked by CheckComputedFields, against the other fields.
        if (field.Expression is not null)
        {
            Fits(
                "expression",
                "text, integer, decimal, boolean, date, date-time and enum",
                FieldType.Text,
                FieldType.Integer,
                FieldType.Decimal,
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
    /// id is used once across all seeds, compared as a UUID. The values are not checked here; the
    /// startup step checks them with the record API's rules before it inserts any record.
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
            if (findEntity(seed.Entity) is { } entity)
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
    /// Checks the data sources: each names a loaded entity, each projected <c>path</c> resolves as
    /// <see cref="ResolvePath"/> describes, projected names are unique, and the default <c>sort</c>
    /// names a projected field that does not end at a reference. An entity that is a child entity
    /// is reported at <c>/entity</c>, naming its owner. Entity and field names
    /// resolve ignoring letter case; projected names compare exactly, as the query <c>sort</c> does.
    /// Each parameter name differs from the entity's fields, from <c>page</c>, <c>pageSize</c> and
    /// <c>sort</c> and from earlier parameters, ignoring letter case. Its type properties and label
    /// are checked as an entity field's. The <c>filter</c> parses, type-checks as a boolean over the
    /// entity's fields, the parameters, paths through reference fields and the named rules, and
    /// translates to SQL, with each rule call inlined. Its child collections are in the type check's scope, so an aggregate
    /// type-checks and is then reported as outside the SQL subset. Its first problem
    /// is reported at <c>/filter</c> with its expression code. It is not checked while a parameter
    /// has a diagnostic. An <c>aggregate</c> groups by projected names, its measure names differ
    /// from the group fields and from each other, and each measure is valid as
    /// <see cref="CheckMeasure"/> describes. A grouped data source's sort names a group field that
    /// is not a reference, or a measure.
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
            for (var index = 0; index < dataSource.Fields.Count; index++)
            {
                var projected = dataSource.Fields[index];
                var pathProblem = ResolvePath(
                    entity,
                    projected.Path,
                    findEntity,
                    target => references.Add(dataSource, $"/fields/{index}/path", ReferenceRelations.FieldPath, ResourceNode.Of(target)),
                    out var hops);

                // A path that does not resolve, or ends at a child collection, has no column. It is
                // stored as an invalid path for the sort check.
                var field = pathProblem is null && hops.Count > 0 ? hops[^1] : null;
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
            }

            var countBeforeParameters = diagnostics.Count;
            var parameterNames = new HashSet<string>(["page", "pageSize", "sort"], StringComparer.OrdinalIgnoreCase);
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
                        $"The parameter name '{parameter.Name}' is reserved or already used by an earlier parameter. 'page', 'pageSize' and 'sort' are reserved, ignoring letter case.",
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
                    Target = parameter.Target,
                };
                CheckField(
                    parameterField,
                    path,
                    findEntity,
                    ownersByChildName,
                    loaded.UnloadedEntityNames,
                    moduleReferences,
                    Report);
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
    /// <c>count</c> takes no field. <c>sum</c>, <c>min</c> and <c>max</c> need a projected field:
    /// <c>sum</c> of an integer or a decimal, <c>min</c> and <c>max</c> of an integer, a decimal, a
    /// date or a date-time. A projected field with an invalid path is not reported again.
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
        var accepted = function == AggregateFunction.Sum
            ? type is FieldType.Integer or FieldType.Decimal
            : type is FieldType.Integer or FieldType.Decimal or FieldType.Date or FieldType.DateTime;
        return accepted
            ? null
            : (
                $"The measure function '{measure.Function}' cannot take the {field.Type} field '{name}'. "
                    + "'sum' takes an integer or a decimal. 'min' and 'max' take an integer, a decimal, a date or a date-time.",
                "/field");
    }

    private static AggregateFunction ParseAggregateFunction(string function) =>
        function switch
        {
            "count" => AggregateFunction.Count,
            "sum" => AggregateFunction.Sum,
            "min" => AggregateFunction.Min,
            "max" => AggregateFunction.Max,
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, "The JSON Schema allows only count, sum, min and max."),
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
    /// collection. Returns the problem's message, or null when the path resolves or reaches an
    /// entity that was not loaded, which is reported at that entity's own file. When the path
    /// resolves, <paramref name="hops"/> holds one field per name. It is empty when an entity was
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
        var inputScope = ExpressionScopes.ForEntity(entity.Fields, includeComputed: false, findEntity: FindEntity);
        var fields = entity.Fields.Select(field => BuildField(field, inputScope, entitiesByName, sequencesByName)).ToList();
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
        };
    }

    private static FieldModel BuildField(
        FieldDefinition field,
        ExpressionScope inputScope,
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
            Precision = field.Precision,
            Scale = field.Scale,
            Values = field.Values,
            Target = target is null ? null : new EntityReference(target.Id, target.Name),
            TargetDisplayField = type == FieldType.Reference && target?.DisplayField is { } displayField ? FindField(target, displayField)?.Name : null,
            Computed = field.Expression is null
                ? null
                : ComputedFieldModel.Compile(field.Expression, inputScope, ExpressionScopes.TypeOf(field)!),
            Sequence = sequence is null ? null : new SequenceModel(sequence.Id, sequence.Name, sequence.Format),
            Creator = field.Creator == true,
            Sensitive = field.Sensitive == true,
        };
    }

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
        IReadOnlyList<WidgetActionModel> BuildActions(IReadOnlyList<WidgetActionDefinition>? actions) =>
            actions is null || actions.Count == 0 ? [] : [.. actions.Select(BuildAction)];

        WidgetActionModel BuildAction(WidgetActionDefinition action)
        {
            if (action.Type == "startProcess")
            {
                var process = processesByName[action.Process!];
                return new StartProcessActionModel(new ProcessReference(process.Id, process.Name), action.Label);
            }

            // The checker has matched each key to a parameter of the target, ignoring letter case. The model
            // keys them by the declared name, so a client never fixes the case itself.
            var target = pagesByName[action.Page!];
            var parameters = action.Parameters is not { Count: > 0 }
                ? NavigateActionModel.NoParameters
                : action.Parameters.ToDictionary(
                    pair => target.Parameters.First(parameter => string.Equals(parameter.Name, pair.Key, StringComparison.OrdinalIgnoreCase)).Name,
                    pair => pair.Value);
            return new NavigateActionModel(PageReferenceTo(target), action.Label)
            {
                Parameters = parameters,
                Record = action.Record,
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
                Actions = BuildActions(widget.Actions),
                RowActions = BuildActions(widget.RowActions),
                ToolbarActions = BuildActions(widget.ToolbarActions),
                Parameters = BuildParameters(widget),
                Kpi = BuildKpi(widget, dataSource),
            };
        }

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
                widget.Navigate is { } navigate ? (NavigateActionModel)BuildAction(navigate) : null);
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
                parameter.Target is not null && Find(parameter.Target) is { } target ? new EntityReference(target.Id, target.Name) : null))
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
        };
    }

    private static PageReference PageReferenceTo(PageResource page) => new(page.Id, page.Name);

    /// <summary>The child collection field that owns a child entity, shown as <c>Entity.field</c>.</summary>
    private sealed record ChildOwner(EntityResource Entity, int FieldIndex)
    {
        public override string ToString() => $"{Entity.Name}.{Entity.Fields[FieldIndex].Name}";
    }
}
