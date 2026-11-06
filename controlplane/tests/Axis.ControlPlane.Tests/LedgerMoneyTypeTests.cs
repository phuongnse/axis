using System.Reflection;
using Axis.ControlPlane.Billing;
using Axis.ControlPlane.Storage;
using Microsoft.EntityFrameworkCore;

namespace Axis.ControlPlane.Tests;

/// <summary>Money is a whole number of minor units end to end: no billing type holds a floating point or decimal amount.</summary>
public sealed class LedgerMoneyTypeTests
{
    private const BindingFlags AllMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] _forbidden = [typeof(decimal), typeof(double), typeof(float), typeof(Half)];

    [Fact]
    public void No_property_field_or_parameter_of_the_billing_code_is_a_decimal_or_floating_point_number()
    {
        var types = BillingTypes().ToList();
        var used = types.SelectMany(MemberTypes).SelectMany(Unwrap).ToList();

        Assert.NotEmpty(used);
        Assert.DoesNotContain(used, type => _forbidden.Contains(type));
    }

    [Theory]
    [InlineData(typeof(LedgerEntryRow))]
    [InlineData(typeof(LedgerEntry))]
    [InlineData(typeof(LedgerBalance))]
    public void The_amount_of_every_ledger_type_is_a_long(Type type)
    {
        Assert.Contains(BillingTypes(), found => found == type);
        Assert.Equal(typeof(long), type.GetProperty("AmountMinor", AllMembers)?.PropertyType);
    }

    [Fact]
    public void The_amount_column_is_a_bigint()
    {
        using var context = new ControlPlaneDbContext(new DbContextOptionsBuilder<ControlPlaneDbContext>().UseNpgsql("Host=localhost;Database=x").Options);

        var table = Assert.Single(context.Model.GetRelationalModel().Tables, t => t is { Schema: "billing", Name: "ledger_entries" });
        var amount = Assert.Single(table.Columns, c => c.Name == "amount_minor");

        Assert.Equal("bigint", amount.StoreType);
    }

    private static IEnumerable<Type> BillingTypes() =>
        typeof(Ledger).Assembly.GetTypes().Where(type => type.Namespace == "Axis.ControlPlane.Billing")
            .SelectMany(type => new[] { type }.Concat(NestedTypes(type)));

    private static IEnumerable<Type> NestedTypes(Type type) =>
        type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(nested => new[] { nested }.Concat(NestedTypes(nested)));

    private static IEnumerable<Type> MemberTypes(Type type)
    {
        foreach (var property in type.GetProperties(AllMembers))
        {
            yield return property.PropertyType;
        }

        foreach (var field in type.GetFields(AllMembers))
        {
            yield return field.FieldType;
        }

        foreach (var method in type.GetMethods(AllMembers).Cast<MethodBase>().Concat(type.GetConstructors(AllMembers)))
        {
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            if (method is MethodInfo info)
            {
                yield return info.ReturnType;
            }
        }
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        if (type.HasElementType)
        {
            foreach (var inner in Unwrap(type.GetElementType()!))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var inner in type.GetGenericArguments().SelectMany(Unwrap))
            {
                yield return inner;
            }
        }
    }
}
