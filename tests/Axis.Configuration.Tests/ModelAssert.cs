using Axis.Configuration.Model;

namespace Axis.Configuration.Tests;

/// <summary>Compares models by value. Records holding lists compare those lists by reference, so they are compared here item by item.</summary>
internal static class ModelAssert
{
    public static void Equal(ApplicationModel expected, ApplicationModel actual)
    {
        Assert.Equal(expected.Manifest, actual.Manifest);
        Assert.Equal(expected.Entities.Count, actual.Entities.Count);
        foreach (var (expectedEntity, actualEntity) in expected.Entities.Zip(actual.Entities))
        {
            Assert.Equal(
                (expectedEntity.Id, expectedEntity.Name, expectedEntity.Label, expectedEntity.File, expectedEntity.DisplayField),
                (actualEntity.Id, actualEntity.Name, actualEntity.Label, actualEntity.File, actualEntity.DisplayField));
            Assert.Equal(expectedEntity.Fields.Count, actualEntity.Fields.Count);
            foreach (var (expectedField, actualField) in expectedEntity.Fields.Zip(actualEntity.Fields))
            {
                Assert.Equal(expectedField with { Values = null }, actualField with { Values = null });
                Assert.Equal(expectedField.Values, actualField.Values);
            }

            Assert.Equal(
                expectedEntity.Validations.Select(validation => (validation.Expression, validation.Message, validation.Field)),
                actualEntity.Validations.Select(validation => (validation.Expression, validation.Message, validation.Field)));
        }

        Assert.Equal(expected.Sites.Count, actual.Sites.Count);
        foreach (var (expectedSite, actualSite) in expected.Sites.Zip(actual.Sites))
        {
            Assert.Equal(
                (expectedSite.Id, expectedSite.Name, expectedSite.Path, expectedSite.Title, expectedSite.Locales.Default, expectedSite.Locales.Fallback, expectedSite.File),
                (actualSite.Id, actualSite.Name, actualSite.Path, actualSite.Title, actualSite.Locales.Default, actualSite.Locales.Fallback, actualSite.File));
            Assert.Equal(expectedSite.Locales.Available, actualSite.Locales.Available);
            Assert.Equal(expectedSite.Navigation, actualSite.Navigation);
        }

        Assert.Equal(expected.Pages.Count, actual.Pages.Count);
        foreach (var (expectedPage, actualPage) in expected.Pages.Zip(actual.Pages))
        {
            Assert.Equal(
                (expectedPage.Id, expectedPage.Name, expectedPage.Title, expectedPage.File),
                (actualPage.Id, actualPage.Name, actualPage.Title, actualPage.File));
            Assert.Equal(expectedPage.Widgets, actualPage.Widgets);
        }

        Assert.Equal(expected.Texts.Count, actual.Texts.Count);
        foreach (var (expectedText, actualText) in expected.Texts.Zip(actual.Texts))
        {
            Assert.Equal(expectedText with { Texts = actualText.Texts }, actualText);
            Assert.Equal(expectedText.Texts.OrderBy(text => text.Key, StringComparer.Ordinal), actualText.Texts.OrderBy(text => text.Key, StringComparer.Ordinal));
        }

        Assert.Equal(expected.DataSources.Count, actual.DataSources.Count);
        foreach (var (expectedDataSource, actualDataSource) in expected.DataSources.Zip(actual.DataSources))
        {
            Assert.Equal(
                (expectedDataSource.Id, expectedDataSource.Name, expectedDataSource.File, expectedDataSource.Entity, expectedDataSource.Filter?.Expression, expectedDataSource.Sort, expectedDataSource.PageSize),
                (actualDataSource.Id, actualDataSource.Name, actualDataSource.File, actualDataSource.Entity, actualDataSource.Filter?.Expression, actualDataSource.Sort, actualDataSource.PageSize));
            Assert.Equal(
                expectedDataSource.Fields.Select(field => (field.Name, field.Field.Name)),
                actualDataSource.Fields.Select(field => (field.Name, field.Field.Name)));
            Assert.Equal(expectedDataSource.Aggregate is null, actualDataSource.Aggregate is null);
            if (expectedDataSource.Aggregate is { } expectedAggregate && actualDataSource.Aggregate is { } actualAggregate)
            {
                Assert.Equal(
                    expectedAggregate.GroupBy.Select(field => field.Name),
                    actualAggregate.GroupBy.Select(field => field.Name));
                Assert.Equal(
                    expectedAggregate.Measures.Select(measure => (measure.Name, measure.Function, measure.Field?.Name)),
                    actualAggregate.Measures.Select(measure => (measure.Name, measure.Function, measure.Field?.Name)));
            }
        }

        Assert.Equal(expected.Processes.Count, actual.Processes.Count);
        foreach (var (expectedProcess, actualProcess) in expected.Processes.Zip(actual.Processes))
        {
            Assert.Equal(
                (expectedProcess.Id, expectedProcess.Name, expectedProcess.File, expectedProcess.Entity, expectedProcess.Start),
                (actualProcess.Id, actualProcess.Name, actualProcess.File, actualProcess.Entity, actualProcess.Start));
            Assert.Equal(
                (expectedProcess.StartCondition?.Expression.Expression, expectedProcess.StartCondition?.Message),
                (actualProcess.StartCondition?.Expression.Expression, actualProcess.StartCondition?.Message));
            Assert.Equal(expectedProcess.Steps.Select(Describe), actualProcess.Steps.Select(Describe));
        }
    }

    /// <summary>
    /// A step's name, its type, and for a decision each branch's condition and target, then its
    /// <c>otherwise</c>. For an operation, each field it sets with its expression, then its <c>next</c>.
    /// </summary>
    private static string Describe(ProcessStepModel step) =>
        step switch
        {
            DecisionStepModel decision =>
                $"{decision.Name} decision [{string.Join(", ", decision.Branches.Select(branch => $"{branch.When.Expression} → {branch.Next}"))}] otherwise {decision.Otherwise}",
            OperationStepModel operation =>
                $"{operation.Name} {operation.Operation} [{string.Join(", ", operation.Set.Select(assignment => $"{assignment.Field} = {assignment.Value.Expression}"))}] next {operation.Next}",
            _ => $"{step.Name} {step.GetType().Name}",
        };
}
