using Axis.Configuration.Diagnostics;
using Axis.Configuration.Loading;
using Axis.Configuration.Model;
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
/// of every locale, checks the named rules and their calls, checks every entity's fields against
/// the field type rules and type-checks its computed fields and validations, checks sites,
/// pages and seeds, checks data sources and their filters, and resolves references between entities, pages, sites and seeds. Every problem is reported, together with the loader's, in one
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

        var ownersByChildName = FindChildOwners(loaded.Entities, FindEntity, diagnostics);

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

        var textKeys = CheckTexts(loaded.Texts, diagnostics);
        if (loaded.Application is { } application)
        {
            CheckTextKey(application.Label, application.File, application.Id, "/label", textKeys, diagnostics);
        }

        // Only validations call rules for now, at their top level. Computed fields, aggregate item
        // expressions and filters see no rules.
        var rules = RuleChecker.Check(loaded.Rules, diagnostics).Values;
        foreach (var entity in loaded.Entities)
        {
            CheckEntity(entity, FindEntity, ownersByChildName, loaded.UnloadedEntityNames, textKeys, rules, diagnostics);
        }

        PresentationChecker.Check(loaded, FindEntity, FindPage, FindDataSource, textKeys, diagnostics);
        CheckSeeds(loaded, FindEntity, diagnostics);
        CheckDataSources(loaded, FindEntity, ownersByChildName, textKeys, diagnostics);

        var result = new CompilationResult(null, DiagnosticOrder.Sort(diagnostics));
        if (result.HasErrors || loaded.Application is null)
        {
            return result;
        }

        var entities = loaded.Entities.Select(entity => BuildEntity(entity, entitiesByName, rules)).ToList();
        var model = new ApplicationModel
        {
            Manifest = loaded.Application,
            Entities = entities,
            Sites = loaded.Sites.Select(site => BuildSite(site, pagesByName)).ToList(),
            Pages = loaded.Pages.Select(page => BuildPage(page, entitiesByName, pagesByName, dataSourcesByName)).ToList(),
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
        IReadOnlySet<string> textKeys,
        IEnumerable<ExpressionRule> rules,
        List<Diagnostic> diagnostics)
    {
        void Report(string code, string message, string path) =>
            diagnostics.Add(new Diagnostic(code, message, entity.File, path, entity.Id));

        CheckTextKey(entity.Label, entity.File, entity.Id, "/label", textKeys, diagnostics);

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

            CheckTextKey(field.Label, entity.File, entity.Id, $"{path}/label", textKeys, diagnostics);
            CheckField(field, path, findEntity, ownersByChildName, unloadedEntityNames, Report);

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

        CheckComputedFields(entity, findEntity, Report);
        CheckValidations(entity, findEntity, textKeys, rules, diagnostics, Report);
    }

    /// <summary>
    /// Checks the expression of each computed field of a scalar type: it parses and type-checks to
    /// the field's type over the entity's fields that are not computed and its child collections,
    /// which only aggregates accept. A computed field is not in the scope, so naming one, itself
    /// included, is an unknown name. An aggregate's item expression sees the child row's fields,
    /// computed ones included. An expression problem is reported at the expression with its own code.
    /// </summary>
    private static void CheckComputedFields(
        EntityResource entity, Func<string, EntityResource?> findEntity, Action<string, string, string> report)
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
            ExpressionDiagnostic? problem = parsed.Succeeded
                ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionScopes.TypeOf(field)!).Diagnostic
                : parsed.Diagnostic;
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
        IReadOnlySet<string> textKeys,
        IEnumerable<ExpressionRule> rules,
        List<Diagnostic> diagnostics,
        Action<string, string, string> report)
    {
        if (entity.Validations.Count == 0)
        {
            return;
        }

        var scope = ExpressionScopes.ForEntity(entity.Fields, rules: rules, findEntity: findEntity);
        for (var index = 0; index < entity.Validations.Count; index++)
        {
            var validation = entity.Validations[index];
            var path = $"/validations/{index}";

            CheckTextKey(validation.Message, entity.File, entity.Id, $"{path}/message", textKeys, diagnostics);

            if (FindField(entity, validation.Field) is null)
            {
                report(
                    DiagnosticCodes.UnknownValidationField,
                    $"The validation field '{validation.Field}' names no field of this entity.",
                    $"{path}/field");
            }

            var parsed = ExpressionParser.Parse(validation.Expression);
            ExpressionDiagnostic? problem = parsed.Succeeded
                ? ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean).Diagnostic
                : parsed.Diagnostic;
            if (problem is not null)
            {
                report(problem.Code, problem.Message, $"{path}/expression");
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
        IReadOnlyDictionary<string, ChildOwner> ownersByChildName,
        IReadOnlySet<string> unloadedEntityNames,
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
    /// Checks the data sources: each names a loaded entity, each projected <c>path</c> resolves as
    /// <see cref="ResolvePath"/> describes, projected names are unique, and the default <c>sort</c>
    /// names a projected field that does not end at a reference. Entity and field names
    /// resolve ignoring letter case; projected names compare exactly, as the query <c>sort</c> does.
    /// Each parameter name differs from the entity's fields, from <c>page</c>, <c>pageSize</c> and
    /// <c>sort</c> and from earlier parameters, ignoring letter case. Its type properties and label
    /// are checked as an entity field's. The <c>filter</c> parses, type-checks as a boolean over the
    /// entity's fields, the parameters and paths through reference fields, with no rules, and
    /// translates to SQL. Its child collections are in the type check's scope, so an aggregate
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
        IReadOnlySet<string> textKeys,
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
                var pathProblem = ResolvePath(entity, projected.Path, findEntity, out var hops);

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

                CheckTextKey(parameter.Label, dataSource.File, dataSource.Id, $"{path}/label", textKeys, diagnostics);
                CheckField(
                    new FieldDefinition
                    {
                        Name = parameter.Name,
                        Type = parameter.Type,
                        Required = parameter.Required,
                        Label = parameter.Label,
                        Values = parameter.Values,
                        Target = parameter.Target,
                    },
                    path,
                    findEntity,
                    ownersByChildName,
                    loaded.UnloadedEntityNames,
                    Report);
            }

            // A parameter without a usable type would only add noise to the filter's diagnostics.
            if (diagnostics.Count == countBeforeParameters
                && dataSource.Filter is { } filter
                && CheckFilter(filter, ExpressionScopes.ForDataSource(entity.Fields, dataSource.Parameters, findEntity)) is { } problem)
            {
                Report(problem.Code, problem.Message, "/filter");
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
    /// The first problem of a data source filter: in parsing, in type checking over the entity's
    /// fields, its child collections, the parameters and paths through reference fields, with no
    /// rules, or outside the SQL subset.
    /// </summary>
    private static ExpressionDiagnostic? CheckFilter(string filter, ExpressionScope scope)
    {
        var parsed = ExpressionParser.Parse(filter);
        if (!parsed.Succeeded)
        {
            return parsed.Diagnostic;
        }

        var check = ExpressionTypeChecker.Check(parsed.Expression, scope, ExpressionType.Boolean);
        return check.Succeeded
            ? SqlTranslator.Translate(parsed.Expression, path => string.Join('.', path)).Diagnostic
            : check.Diagnostic;
    }

    /// <summary>
    /// Resolves a projected <c>path</c> from the entity <paramref name="root"/>, ignoring letter
    /// case. Every name before the last is a reference field, the path takes at most
    /// <see cref="ExpressionLimits.MaxHops"/> hops, and the last name is a field that is not a child
    /// collection. Returns the problem's message, or null when the path resolves or reaches an
    /// entity that was not loaded, which is reported at that entity's own file. When the path
    /// resolves, <paramref name="hops"/> holds one field per name. It is empty when an entity was
    /// not loaded.
    /// </summary>
    private static string? ResolvePath(
        EntityResource root, string path, Func<string, EntityResource?> findEntity, out IReadOnlyList<FieldDefinition> hops)
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
                return type == FieldType.ChildCollection
                    ? $"The path '{path}' names the child collection '{field.Name}'. A data source cannot project a child collection."
                    : null;
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

            entity = target;
        }

        return null;
    }

    private static EntityModel BuildEntity(
        EntityResource entity, Dictionary<string, EntityResource> entitiesByName, IEnumerable<ExpressionRule> rules)
    {
        // The child models are not built yet, so both scopes come from the definitions.
        EntityResource? FindEntity(string name) => entitiesByName.GetValueOrDefault(name);
        var inputScope = ExpressionScopes.ForEntity(entity.Fields, includeComputed: false, findEntity: FindEntity);
        var fields = entity.Fields.Select(field => BuildField(field, inputScope, entitiesByName)).ToList();
        var scope = ExpressionScopes.ForEntity(entity.Fields, rules: rules, findEntity: FindEntity);
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

    private static FieldModel BuildField(FieldDefinition field, ExpressionScope inputScope, Dictionary<string, EntityResource> entitiesByName)
    {
        var type = FieldTypes.Parse(field.Type);
        var target = field.Target is null ? null : entitiesByName[field.Target];
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
        Dictionary<string, DataSourceResource> dataSourcesByName) =>
        new()
        {
            Id = page.Id,
            Name = page.Name,
            Title = page.Title,
            File = page.File,
            Widgets = page.Widgets
                .Select(widget =>
                {
                    // A checked widget names exactly one of an entity and a data source.
                    var entity = widget.Entity is null ? null : entitiesByName[widget.Entity];
                    var dataSource = widget.DataSource is null ? null : dataSourcesByName[widget.DataSource];
                    return new WidgetModel(
                        WidgetTypes.Parse(widget.Type),
                        entity is null ? null : new EntityReference(entity.Id, entity.Name),
                        widget.FormPage is null ? null : PageReferenceTo(pagesByName[widget.FormPage]),
                        dataSource is null ? null : new DataSourceReference(dataSource.Id, dataSource.Name));
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

    /// <summary>
    /// Builds a checked data source over the built entities, so each projected path is a list of the
    /// entities' own field models, and its filter is compiled against the built entity's fields, the
    /// parameters and paths through reference fields.
    /// </summary>
    private static DataSourceModel BuildDataSource(DataSourceResource dataSource, IReadOnlyList<EntityModel> entities)
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
                ? ExpressionModel.Compile(filter, ExpressionScopes.ForDataSource(entity.Fields, parameters, Find), ExpressionType.Boolean)
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
