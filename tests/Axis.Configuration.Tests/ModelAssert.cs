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
                (expectedEntity.Id, expectedEntity.Name, expectedEntity.Label, expectedEntity.File),
                (actualEntity.Id, actualEntity.Name, actualEntity.Label, actualEntity.File));
            Assert.Equal(expectedEntity.Fields.Count, actualEntity.Fields.Count);
            foreach (var (expectedField, actualField) in expectedEntity.Fields.Zip(actualEntity.Fields))
            {
                Assert.Equal(expectedField with { Values = null }, actualField with { Values = null });
                Assert.Equal(expectedField.Values, actualField.Values);
            }
        }
    }
}
