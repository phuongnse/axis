using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Axis.Data.Records;

namespace Axis.Data.Tests;

/// <summary>
/// Every write method of <see cref="RecordCommands"/> reaches the lifecycle check of a module entity,
/// directly or through the methods it calls, so a write path added later cannot skip it. The search reads
/// the compiled IL, including the <c>MoveNext</c> of an async state machine.
/// </summary>
public sealed class RecordCommandsLifecycleTests
{
    private static readonly Dictionary<short, OpCode> _opCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [Fact]
    public void The_public_write_methods_are_the_five_the_check_covers()
    {
        Assert.Equal(
            ["CreateAsync", "CreateFromAsync", "DeleteAsync", "SetAsync", "UpdateAsync"],
            WriteMethods().Select(method => method.Name).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_public_write_method_reaches_the_lifecycle_check()
    {
        var methods = WriteMethods();

        Assert.NotEmpty(methods);
        foreach (var method in methods)
        {
            Assert.True(Reaches(method, typeof(RecordLifecycle), []), $"{method.Name} does not reach the lifecycle check.");
        }
    }

    [Fact]
    public void A_method_that_writes_nothing_does_not_reach_the_lifecycle_check()
    {
        // Shows that the search can fail: LockAsync only takes a row lock.
        var lockAsync = typeof(RecordCommands).GetMethod(nameof(RecordCommands.LockAsync))!;

        Assert.False(Reaches(lockAsync, typeof(RecordLifecycle), []));
    }

    private static List<MethodInfo> WriteMethods() =>
        typeof(RecordCommands)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(Task<RecordWriteResult>) || method.ReturnType == typeof(Task<RecordDeleteResult>))
            .ToList();

    // Whether the method, or a method of RecordCommands that it calls, calls a method declared on target.
    private static bool Reaches(MethodBase method, Type target, HashSet<MethodBase> visited)
    {
        if (!visited.Add(method))
        {
            return false;
        }

        // An async method starts its state machine, whose MoveNext holds the body.
        var bodies = new List<MethodBase> { method };
        if (method.GetCustomAttribute<AsyncStateMachineAttribute>() is { } machine)
        {
            bodies.Add(machine.StateMachineType.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!);
        }

        foreach (var body in bodies)
        {
            foreach (var callee in Callees(body))
            {
                if (IsDeclaredOn(callee, target))
                {
                    return true;
                }

                if (IsDeclaredOn(callee, typeof(RecordCommands)) && Reaches(callee, target, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The type itself, or a type nested in it, such as a compiler-generated closure or state machine.
    private static bool IsDeclaredOn(MethodBase method, Type type)
    {
        for (var declaring = method.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
        {
            if (declaring == type)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<MethodBase> Callees(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var typeArguments = method.DeclaringType is { IsGenericType: true } ? method.DeclaringType.GetGenericArguments() : null;
        var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        var position = 0;
        while (position < il.Length)
        {
            var code = il[position] == 0xFE
                ? _opCodes[(short)(0xFE00 | il[position + 1])]
                : _opCodes[il[position]];
            position += code.Size;
            if (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj || code == OpCodes.Ldftn)
            {
                MethodBase? callee = null;
                try
                {
                    callee = method.Module.ResolveMethod(BitConverter.ToInt32(il, position), typeArguments, methodArguments);
                }
                catch (ArgumentException)
                {
                }

                if (callee is not null)
                {
                    yield return callee;
                }
            }

            position += OperandSize(code, il, position);
        }
    }

    private static int OperandSize(OpCode code, byte[] il, int position) =>
        code.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, position)),
            _ => 4,
        };
}
