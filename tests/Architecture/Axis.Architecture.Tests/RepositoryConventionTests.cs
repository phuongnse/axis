using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Axis.Architecture.Tests;

/// <summary>
/// Convention checks for repository implementations across module Infrastructure
/// projects. Repositories are the "anti-corruption layer" between the domain
/// and EF Core; they must materialize results before returning so callers in
/// Application can't accidentally extend the query (which would defer execution
/// across tenant/schema boundaries) or expose <c>IQueryable</c> to UI handlers
/// (which leaks transport-bound types into pure business code).
///
/// <para>
/// Repository implementations are identified as concrete classes ending in
/// <c>Repository</c> inside any <c>Axis.{Module}.Infrastructure</c> assembly.
/// </para>
///
/// <para>
/// Persistence orchestration (<c>SaveChangesAsync</c>) belongs in
/// <c>IUnitOfWork</c> only — repos must not call it directly or transactions
/// can split across the tracked aggregates.
/// </para>
/// </summary>
public class RepositoryConventionTests
{
    public static IEnumerable<object[]> AllRepositoryClasses() =>
        Conventions.ModuleNames
            .Select(m => Conventions.TryLoadModuleLayer(m, "Infrastructure"))
            .Where(a => a is not null)
            .SelectMany(a => a!.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllRepositoryClasses))]
    public void Repository_PublicMethods_DoNotReturnIQueryable(Type repoType)
    {
        IEnumerable<MethodInfo> leakingMethods = repoType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => ReturnsIQueryable(m.ReturnType));

        leakingMethods.Should().BeEmpty(
            $"{repoType.FullName} exposes IQueryable on a public method — repositories must " +
            "materialize (ToList/FirstOrDefault/ProjectTo) before returning. IQueryable leaks " +
            "deferred-execution semantics into Application/Domain and breaks tenant schema interceptors " +
            "(patterns.md § Repository pattern). " +
            $"Methods: {string.Join(", ", leakingMethods.Select(m => m.Name))}");
    }

    [Theory]
    [MemberData(nameof(AllRepositoryClasses))]
    public void Repository_DoesNotCallSaveChangesAsync(Type repoType)
    {
        // We can't easily inspect call sites with reflection alone, but we CAN
        // detect a SaveChanges/SaveChangesAsync helper exposed on the repository
        // itself — which would invite handlers to commit via the repo. The
        // contract is: persistence is IUnitOfWork's job, period.
        MethodInfo[] suspectMethods = repoType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m =>
                m.Name.Equals("SaveChanges", StringComparison.Ordinal)
                || m.Name.Equals("SaveChangesAsync", StringComparison.Ordinal)
                || m.Name.Equals("Commit", StringComparison.Ordinal)
                || m.Name.Equals("CommitAsync", StringComparison.Ordinal))
            .ToArray();

        suspectMethods.Should().BeEmpty(
            $"{repoType.FullName} exposes a commit-style method — persistence belongs in " +
            "IUnitOfWork.SaveChangesAsync (patterns.md § Unit of Work). " +
            $"Methods: {string.Join(", ", suspectMethods.Select(m => m.Name))}");
    }

    private static bool ReturnsIQueryable(Type returnType)
    {
        Type unwrapped = UnwrapTask(returnType);
        if (!unwrapped.IsGenericType)
            return unwrapped == typeof(IQueryable);
        Type def = unwrapped.GetGenericTypeDefinition();
        return def == typeof(IQueryable<>);
    }

    private static Type UnwrapTask(Type type)
    {
        if (type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(Task<>)
                || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            return type.GetGenericArguments()[0];
        }
        return type;
    }
}
