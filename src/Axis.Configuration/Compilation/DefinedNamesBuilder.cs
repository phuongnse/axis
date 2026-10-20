using Axis.Configuration.Loading;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Collects <see cref="DefinedNames"/> from the resources a compile loaded. It reads the resources, not the compiled
/// model, because the model exists only when the folder has no errors.
/// </summary>
internal static class DefinedNamesBuilder
{
    public static DefinedNames Build(
        ApplicationLoadResult loaded,
        IReadOnlyList<EntityResource> entities,
        IReadOnlyList<SequenceResource> sequences,
        IReadOnlyList<EventResource> events,
        IReadOnlyList<(string ModuleId, IReadOnlyList<RuleResource> Rules)> moduleRules,
        IReadOnlyList<TextResource> texts,
        Func<string, EntityResource?> findEntity,
        ReferenceIndex index)
    {
        var rules = new List<DefinedRule>(loaded.Rules.Select(rule => Rule(rule, rule.Name, null)));
        foreach (var (moduleId, resources) in moduleRules)
        {
            rules.AddRange(resources.Select(rule => Rule(rule, $"{moduleId}:{rule.Name}", moduleId)));
        }

        var locales = loaded.Texts.Select(text => text.Locale).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new DefinedNames(
            locales,
            [.. entities.Select(entity => new DefinedEntity(
                entity.Id,
                entity.Name,
                ModuleOf(entity),
                [.. entity.Fields.Select(field => new DefinedField(field.Name, field.Type, field.Target, field.Values))]))],
            [.. loaded.DataSources.Select(dataSource => DataSource(dataSource, findEntity))],
            [.. loaded.Processes.Select(process => new DefinedProcess(
                process.Id,
                process.Name,
                [.. process.Steps.Select(step => new DefinedStep(
                    step.Name, step.Type, [.. step.Outcomes?.Select(outcome => outcome.Name) ?? []]))]))],
            rules,
            [.. loaded.Connectors.Select(connector => new DefinedConnector(
                connector.Id, connector.Name, [.. connector.Operations.Select(operation => new DefinedOperation(operation.Name))]))],
            [.. events.Select(@event => new DefinedEvent(
                @event.Id, @event.Name, [.. @event.Fields.Select(field => new DefinedTyped(field.Name, field.Type))]))],
            [.. loaded.Roles.Select(role => new DefinedResource(role.Id, role.Name))],
            [.. sequences.Select(sequence => new DefinedResource(sequence.Id, sequence.Name))],
            [.. loaded.Forms.Select(form => new DefinedResource(form.Id, form.Name))],
            [.. loaded.Pages.Select(page => new DefinedResource(page.Id, page.Name))],
            Texts(texts, index));
    }

    private static string? ModuleOf(Resource resource) =>
        ApplicationCompiler.IsModuleResource(resource) ? resource.Name[..resource.Name.IndexOf('/')] : null;

    private static DefinedRule Rule(RuleResource rule, string name, string? module) =>
        new(rule.Id, name, module, [.. rule.Parameters.Select(parameter => new DefinedTyped(parameter.Name, parameter.Type))], rule.ResultType);

    private static DefinedDataSource DataSource(DataSourceResource dataSource, Func<string, EntityResource?> findEntity)
    {
        var entity = findEntity(dataSource.Entity);
        var columns = new List<DefinedColumn>();
        foreach (var field in dataSource.Fields)
        {
            string? type = null;
            if (entity is not null
                && ApplicationCompiler.ResolvePath(entity, field.Path, findEntity, _ => { }, out var hops) is null
                && hops.Count > 0)
            {
                type = hops[^1].Type;
            }

            columns.Add(new DefinedColumn(field.Name, type));
        }

        if (dataSource.Aggregate is { } aggregate)
        {
            foreach (var measure in aggregate.Measures)
            {
                var type = measure.Function == "count"
                    ? "integer"
                    : columns.Find(column => column.Name == measure.Field)?.Type;
                columns.Add(new DefinedColumn(measure.Name, type));
            }
        }

        return new DefinedDataSource(
            dataSource.Id,
            dataSource.Name,
            ModuleOf(dataSource),
            [.. dataSource.Parameters.Select(parameter => new DefinedTyped(parameter.Name, parameter.Type))],
            columns);
    }

    private static List<DefinedText> Texts(IReadOnlyList<TextResource> texts, ReferenceIndex index)
    {
        // Duplicate locales are reported by the loader, so the first file of a locale counts.
        var byLocale = texts
            .GroupBy(text => text.Locale, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        return [.. byLocale
            .SelectMany(text => text.Texts.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(key => new DefinedText(
                key,
                byLocale.Where(text => text.Texts.ContainsKey(key)).ToDictionary(text => text.Locale, text => text.Texts[key]),
                index.UsedBy(new TextKeyNode(key)).Count(edge => edge.Relation == ReferenceRelations.LabelText)))];
    }
}
