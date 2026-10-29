using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the texts of the pinned modules and merges them into the application's texts. A module
/// text key starts with the module's <c>&lt;id&gt;.</c>, and an application text key may not start
/// with the prefix of a declared or pinned module. Each application locale needs a text from every pinned
/// module. The first module text of each pinned module in a locale joins that locale's application
/// texts, and module texts in a locale the application lacks are left out.
/// </summary>
internal static class ModuleTextChecker
{
    public static IReadOnlyList<TextResource> Check(
        ApplicationManifest application,
        IReadOnlyList<PinnedModuleModel> pins,
        IReadOnlyList<TextResource> applicationTexts,
        IReadOnlyList<ModuleText> moduleTexts,
        List<Diagnostic> diagnostics)
    {
        CheckApplicationKeys(application, pins, applicationTexts, diagnostics);

        foreach (var pin in pins)
        {
            var texts = moduleTexts.Where(module => module.ModuleId == pin.Id).Select(module => module.Text).ToList();
            CheckModuleKeys(pin.Id, texts, diagnostics);
            ApplicationCompiler.CheckTexts(texts, diagnostics);
        }

        CheckLocales(application, pins, applicationTexts, moduleTexts, diagnostics);

        return moduleTexts.Count == 0
            ? applicationTexts
            : applicationTexts.Select(text => Merge(text, moduleTexts)).ToList();
    }

    private static void CheckApplicationKeys(ApplicationManifest application, IReadOnlyList<PinnedModuleModel> pins, IReadOnlyList<TextResource> applicationTexts, List<Diagnostic> diagnostics)
    {
        var moduleIds = application.Modules.Select(module => module.Id).Concat(pins.Select(pin => pin.Id)).Distinct(StringComparer.Ordinal).ToList();
        foreach (var text in applicationTexts)
        {
            foreach (var key in text.Texts.Keys)
            {
                var moduleId = moduleIds.FirstOrDefault(id => key.StartsWith($"{id}.", StringComparison.Ordinal));
                if (moduleId is null)
                {
                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ApplicationTextKeyWithModulePrefix,
                    $"The text key '{key}' starts with the prefix '{moduleId}.' of a module the application uses. Rename it so it does not use a module's prefix.",
                    text.File,
                    $"/texts/{Escape(key)}",
                    text.Id));
            }
        }
    }

    private static void CheckModuleKeys(string moduleId, IReadOnlyList<TextResource> texts, List<Diagnostic> diagnostics)
    {
        foreach (var text in texts)
        {
            foreach (var key in text.Texts.Keys)
            {
                if (key.StartsWith($"{moduleId}.", StringComparison.Ordinal))
                {
                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ModuleTextKeyWithoutPrefix,
                    $"The text key '{key}' lacks the prefix '{moduleId}.'. Start each text key of the module with it.",
                    text.File,
                    $"/texts/{Escape(key)}",
                    text.Id));
            }
        }
    }

    private static void CheckLocales(
        ApplicationManifest application,
        IReadOnlyList<PinnedModuleModel> pins,
        IReadOnlyList<TextResource> applicationTexts,
        IReadOnlyList<ModuleText> moduleTexts,
        List<Diagnostic> diagnostics)
    {
        var locales = applicationTexts
            .DistinctBy(text => text.Locale, StringComparer.OrdinalIgnoreCase)
            .Select(text => text.Locale)
            .ToList();
        foreach (var pin in pins)
        {
            var moduleLocales = moduleTexts
                .Where(module => module.ModuleId == pin.Id)
                .Select(module => module.Text.Locale)
                .ToList();
            var index = application.Modules.ToList().FindIndex(module => module.Id == pin.Id);
            foreach (var locale in locales)
            {
                if (moduleLocales.Contains(locale, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.MissingModuleLocale,
                    $"The module '{pin.Id}' has no text for the locale '{locale}', which the application has. Pin a version of the module that has texts for '{locale}', or remove the locale from the application.",
                    application.File,
                    index < 0 ? "/modules" : $"/modules/{index}",
                    application.Id));
            }
        }
    }

    private static TextResource Merge(TextResource text, IReadOnlyList<ModuleText> moduleTexts)
    {
        var merged = new Dictionary<string, string>(text.Texts, StringComparer.Ordinal);
        var firstTextOfEachModule = moduleTexts
            .Where(module => string.Equals(module.Text.Locale, text.Locale, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(module => module.ModuleId, StringComparer.Ordinal);
        foreach (var module in firstTextOfEachModule)
        {
            foreach (var (key, value) in module.Text.Texts)
            {
                merged.TryAdd(key, value);
            }
        }

        return text with { Texts = merged };
    }

    /// <summary>Escapes a text key as an RFC 6901 JSON Pointer segment.</summary>
    private static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
