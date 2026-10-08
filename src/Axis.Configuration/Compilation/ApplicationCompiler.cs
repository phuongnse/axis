using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Compiles an application folder, or its resources held in memory: loads them, checks the texts
/// of every locale, checks every entity's fields against the field type rules, checks sites,
/// pages and seeds, and resolves references between entities, pages, sites and seeds. Every problem is reported, together with the loader's, in one
/// sorted list. An application without errors also gets its content hash. Both inputs give the
/// same result for the same resources.
/// </summary>
public static class ApplicationCompiler
{
    /// <summary>The largest length PostgreSQL accepts for <c>varchar(n)</c>.</summary>
    public const int MaxTextLength = 10_485_760;

    /// <summary>The largest precision PostgreSQL accepts for <c>numeric(p, s)</c>.</summary>
    public const int MaxPrecision = 1000;

    public static CompilationResult Compile(string folderPath) => Compile(ApplicationLoader.Load(folderPath));

    /// <summary>Compiles resources held in memory, such as the stored resources of a release.</summary>
    /// <exception cref="ArgumentException">A path appears more than once.</exception>
    public static CompilationResult Compile(IReadOnlyList<ResourceContent> resources) => Compile(ApplicationLoader.Load(resources));

    private static CompilationResult Compile(ApplicationLoadResult loaded)
    {
        var diagnostics = new List<Diagnostic>(loaded.Diagnostics);

        // Entity names are unique ignoring letter case; a second entity with the same name is
        // already reported by the loader, so references resolve to the first one.
        var entitiesByName = new Dictionary<string, EntityResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in loaded.Entities)
        {
            entitiesByName.TryAdd(entity.Name, entity);
        }

        EntityResource? FindEntity(string name) => entitiesByName.GetValueOrDefault(name);

        // Page names follow the same rule as entity names.
        var pagesByName = new Dictionary<string, PageResource>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in loaded.Pages)
        {
            pagesByName.TryAdd(page.Name, page);
        }

        PageResource? FindPage(string name) => pagesByName.GetValueOrDefault(name);

        var textKeys = CheckTexts(loaded.Texts, diagnostics);
        if (loaded.Application is { } application)
        {
            CheckTextKey(application.Label, application.File, application.Id, "/label", textKeys, diagnostics);
        }

        foreach (var entity in loaded.Entities)
        {
            CheckEntity(entity, FindEntity, loaded.UnloadedEntityNames, textKeys, diagnostics);
        }

        PresentationChecker.Check(loaded, FindEntity, FindPage, textKeys, diagnostics);
        CheckSeeds(loaded, FindEntity, diagnostics);
        CheckDataSources(loaded, FindEntity, diagnostics);

        var result = new CompilationResult(null, DiagnosticOrder.Sort(diagnostics));
        if (result.HasErrors || loaded.Application is null)
        {
            return result;
        }

        var entities = loaded.Entities.Select(entity => BuildEntity(entity, entitiesByName)).ToList();
        var model = new ApplicationModel
        {
            Manifest = loaded.Application,
            Entities = entities,
            Sites = loaded.Sites.Select(site => BuildSite(site, pagesByName)).ToList(),
            Pages = loaded.Pages.Select(page => BuildPage(page, entitiesByName, pagesByName)).ToList(),
            Texts = loaded.Texts,
            Seeds = loaded.Seeds.Select(seed => BuildSeed(seed, entitiesByName)).ToList(),
            DataSources = loaded.DataSources.Select(dataSource => BuildDataSource(dataSource, entities)).ToList(),
        };
        return result with
        {
            Model = model,
            ContentHash = ContentHash.Compute(loaded.Resources),
            Resources = loaded.Resources,
        };
    }

    /// <summary>
    /// Checks the text resources, which come in path order: each locale appears once, ignoring
    /// letter case, and every locale has the same keys. A file whose locale is already taken is
    /// left out of the key check, so its drift is not reported on top. Returns every key that
    /// some locale has.
    /// </summary>
    private static HashSet<string> CheckTexts(IReadOnlyList<TextResource> texts, List<Diagnostic> diagnostics)
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

    /// <summary>Reports a label whose text key no locale has. Unused keys are allowed.</summary>
    internal static void CheckTextKey(
        TextReference? label,
        string file,
        Guid? resourceId,
        string pathPrefix,
        IReadOnlySet<string> textKeys,
        List<Diagnostic> diagnostics)
    {
        if (label is not null && !textKeys.Contains(label.TextKey))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.MissingTextKey,
                $"No locale has the text key '{label.TextKey}'.",
                file,
                $"{pathPrefix}/textKey",
                resourceId));
        }
    }

    private static void CheckEntity(
        EntityResource entity,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> unloadedEntityNames,
        IReadOnlySet<string> textKeys,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, entity.File, path, entity.Id));

        CheckTextKey(entity.Label, entity.File, entity.Id, "/label", textKeys, diagnostics);

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

            CheckTextKey(field.Label, entity.File, entity.Id, $"{path}/label", textKeys, diagnostics);
            CheckField(field, path, findEntity, unloadedEntityNames, Report);
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
    }

    /// <summary>Finds a field by name, ignoring letter case, as the model does.</summary>
    private static FieldDefinition? FindField(EntityResource entity, string name) =>
        entity.Fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    private static void CheckField(
        FieldDefinition field,
        string path,
        Func<string, EntityResource?> findEntity,
        IReadOnlySet<string> unloadedEntityNames,
        Action<string, string, string> report)
    {
        var type = FieldTypes.Parse(field.Type);

        // Reports a type-specific property on a field of another type. Returns whether it fits.
        bool Fits(string property, FieldType fittingType, string fittingTypeName)
        {
            if (type == fittingType)
            {
                return true;
            }

            report(
                DiagnosticCodes.InvalidConstraint,
                $"'{property}' applies only to {fittingTypeName} fields, not to {field.Type} fields.",
                $"{path}/{property}");
            return false;
        }

        if (field.MaxLength is { } maxLength
            && Fits("maxLength", FieldType.Text, "text")
            && maxLength is < 1 or > MaxTextLength)
        {
            report(
                DiagnosticCodes.InvalidConstraint,
                $"'maxLength' must be between 1 and {MaxTextLength}, but is {maxLength}.",
                $"{path}/maxLength");
        }

        var precisionValid = false;
        if (field.Precision is { } precision && Fits("precision", FieldType.Decimal, "decimal"))
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

        if (field.Scale is { } scale && Fits("scale", FieldType.Decimal, "decimal"))
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
            if (Fits("target", FieldType.Reference, "reference"))
            {
                if (findEntity(target) is { } targetEntity)
                {
                    // Every record a reference points to is shown by its name.
                    if (targetEntity.DisplayField is null)
                    {
                        report(
                            DiagnosticCodes.ReferenceTargetWithoutDisplayField,
                            $"The target entity '{targetEntity.Name}' has no 'displayField', so its records have no name to show.",
                            $"{path}/target");
                    }
                }
                else if (!unloadedEntityNames.Contains(target))
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

        if (field.Values is not null)
        {
            Fits("values", FieldType.Enum, "enum");
        }
        else if (type == FieldType.Enum)
        {
            report(DiagnosticCodes.MissingTypeProperty, "An enum field must list its 'values'.", path);
        }
    }

    /// <summary>
    /// Checks the seeds, which come in path order: each names a loaded entity, and every seed record
    /// id is used once across all seeds, compared as a UUID. The values are not checked here; the
    /// startup step checks them with the record API's rules before it inserts any record.
    /// </summary>
    private static void CheckSeeds(ApplicationLoadResult loaded, Func<string, EntityResource?> findEntity, List<Diagnostic> diagnostics)
    {
        var firstFileById = new Dictionary<Guid, string>();
        foreach (var seed in loaded.Seeds)
        {
            // An entity file that was not loaded because of its own errors is not reported again.
            if (findEntity(seed.Entity) is null && !loaded.UnloadedEntityNames.Contains(seed.Entity))
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
    /// Checks the data sources: each names a loaded entity, each projected <c>path</c> is a single
    /// field of that entity, projected names are unique, and the default <c>sort</c> names a
    /// projected field that is not a reference. Entity and field names resolve ignoring letter
    /// case; projected names compare exactly, as the query <c>sort</c> does.
    /// </summary>
    private static void CheckDataSources(ApplicationLoadResult loaded, Func<string, EntityResource?> findEntity, List<Diagnostic> diagnostics)
    {
        foreach (var dataSource in loaded.DataSources)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, dataSource.File, path, dataSource.Id));

            var entity = findEntity(dataSource.Entity);
            if (entity is null)
            {
                // An entity file that was not loaded because of its own errors is not reported again.
                if (!loaded.UnloadedEntityNames.Contains(dataSource.Entity))
                {
                    Report(
                        DiagnosticCodes.UnknownDataSourceEntity,
                        $"The entity '{dataSource.Entity}' was not found. No loaded entity has that name.",
                        "/entity");
                }

                // The paths and the sort cannot be checked without the entity.
                continue;
            }

            var fieldsByName = new Dictionary<string, FieldDefinition?>(StringComparer.Ordinal);
            for (var index = 0; index < dataSource.Fields.Count; index++)
            {
                var projected = dataSource.Fields[index];
                var field = projected.Path.Contains('.', StringComparison.Ordinal) ? null : FindField(entity, projected.Path);
                if (!fieldsByName.TryAdd(projected.Name, field))
                {
                    Report(
                        DiagnosticCodes.DuplicateDataSourceFieldName,
                        $"The name '{projected.Name}' is already used by an earlier projected field.",
                        $"/fields/{index}/name");
                }

                if (field is null)
                {
                    Report(
                        DiagnosticCodes.InvalidDataSourceFieldPath,
                        $"The path '{projected.Path}' must name a field of the entity '{entity.Name}'. Paths through references are not supported yet.",
                        $"/fields/{index}/path");
                }
            }

            if (dataSource.Sort is { } sort)
            {
                var name = sort.StartsWith('-') ? sort[1..] : sort;
                if (!fieldsByName.TryGetValue(name, out var field))
                {
                    Report(
                        DiagnosticCodes.InvalidDataSourceSort,
                        $"The sort '{sort}' must name a projected field, optionally preceded by '-'.",
                        "/sort");
                }
                else if (field is not null && FieldTypes.Parse(field.Type) == FieldType.Reference)
                {
                    Report(
                        DiagnosticCodes.InvalidDataSourceSort,
                        $"The sort '{sort}' names the reference field '{name}'. A data source cannot sort by a reference.",
                        "/sort");
                }
            }
        }
    }

    private static EntityModel BuildEntity(EntityResource entity, Dictionary<string, EntityResource> entitiesByName) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Label = entity.Label,
            File = entity.File,
            Fields = entity.Fields.Select(field => BuildField(field, entitiesByName)).ToList(),
            DisplayField = entity.DisplayField is null ? null : FindField(entity, entity.DisplayField)!.Name,
        };

    private static FieldModel BuildField(FieldDefinition field, Dictionary<string, EntityResource> entitiesByName)
    {
        var target = field.Target is null ? null : entitiesByName[field.Target];
        return new FieldModel
        {
            Name = field.Name,
            Type = FieldTypes.Parse(field.Type),
            Label = field.Label,
            Required = field.Required ?? false,
            Unique = field.Unique ?? false,
            MaxLength = field.MaxLength,
            Precision = field.Precision,
            Scale = field.Scale,
            Values = field.Values,
            Target = target is null ? null : new EntityReference(target.Id, target.Name),
            TargetDisplayField = target?.DisplayField is { } displayField ? FindField(target, displayField)?.Name : null,
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
        Dictionary<string, PageResource> pagesByName) =>
        new()
        {
            Id = page.Id,
            Name = page.Name,
            Title = page.Title,
            File = page.File,
            Widgets = page.Widgets
                .Select(widget =>
                {
                    var entity = entitiesByName[widget.Entity];
                    return new WidgetModel(
                        WidgetTypes.Parse(widget.Type),
                        new EntityReference(entity.Id, entity.Name),
                        widget.FormPage is null ? null : PageReferenceTo(pagesByName[widget.FormPage]));
                })
                .ToList(),
        };

    private static SeedModel BuildSeed(SeedResource seed, Dictionary<string, EntityResource> entitiesByName)
    {
        var entity = entitiesByName[seed.Entity];
        return new SeedModel
        {
            Id = seed.Id,
            Name = seed.Name,
            File = seed.File,
            Entity = new EntityReference(entity.Id, entity.Name),
            Sync = seed.Sync,
            Records = seed.Records,
        };
    }

    /// <summary>Builds a checked data source over the built entities, so each projected field is the entity's own field model.</summary>
    private static DataSourceModel BuildDataSource(DataSourceResource dataSource, IReadOnlyList<EntityModel> entities)
    {
        var entity = entities.First(candidate => string.Equals(candidate.Name, dataSource.Entity, StringComparison.OrdinalIgnoreCase));
        var descending = dataSource.Sort?.StartsWith('-') == true;
        return new DataSourceModel
        {
            Id = dataSource.Id,
            Name = dataSource.Name,
            File = dataSource.File,
            Entity = new EntityReference(entity.Id, entity.Name),
            Fields = dataSource.Fields
                .Select(field =>
                {
                    entity.TryGetField(field.Path, out var fieldModel);
                    return new DataSourceFieldModel(field.Name, fieldModel!);
                })
                .ToList(),
            Sort = dataSource.Sort is { } sort ? new DataSourceSortModel(descending ? sort[1..] : sort, descending) : null,
            PageSize = dataSource.PageSize ?? DataSourceModel.DefaultPageSize,
        };
    }

    private static PageReference PageReferenceTo(PageResource page) => new(page.Id, page.Name);
}
