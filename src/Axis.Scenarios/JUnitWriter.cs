using System.Globalization;
using System.Xml.Linq;
using Axis.Scenarios.Runner;

namespace Axis.Scenarios;

/// <summary>Writes the results of an application's scenarios as a JUnit file, one test case per scenario.</summary>
internal static class JUnitWriter
{
    public static async Task WriteAsync(
        string path,
        string application,
        string folder,
        IReadOnlyList<ScenarioResult> results,
        CancellationToken cancellationToken)
    {
        var cases = results.Select(result =>
        {
            var testCase = new XElement(
                "testcase",
                new XAttribute("classname", application),
                new XAttribute("name", result.Name),
                new XAttribute("file", Path.Combine(folder, result.File).Replace('\\', '/')),
                new XAttribute("time", Seconds(result.Duration)));
            if (result.Failure is { } failure)
            {
                testCase.Add(new XElement("failure", new XAttribute("message", failure), failure));
            }

            return testCase;
        });
        var suite = new XElement(
            "testsuite",
            new XAttribute("name", application),
            new XAttribute("tests", results.Count),
            new XAttribute("failures", results.Count(result => !result.Passed)),
            new XAttribute("time", Seconds(TimeSpan.FromTicks(results.Sum(result => result.Duration.Ticks)))),
            cases);
        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("testsuites", suite));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var stream = File.Create(path);
        await document.SaveAsync(stream, SaveOptions.None, cancellationToken);
    }

    private static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
