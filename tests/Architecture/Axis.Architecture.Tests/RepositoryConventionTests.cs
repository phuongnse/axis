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

    [Fact]
    public void RepositoryDiscovery_WhenRunOnLoadedAssemblies_FindsAtLeastOneRepository()
    {
        // Guard against the silent-skip failure mode: if Infrastructure assemblies
        // fail to load, AllRepositoryClasses returns empty and the Theory tests
        // below produce zero cases (xUnit reports "Passed" with no warnings).
        AllRepositoryClasses().Should().NotBeEmpty(
            "Repository convention checks must run against real repositories — empty discovery " +
            "means Infrastructure assemblies aren't being loaded and convention enforcement is silently off.");
    }

    [Theory]
    [MemberData(nameof(AllRepositoryClasses))]
    public void Repository_WhenInspected_HasNoPublicMethodsReturningIQueryable(Type repoType)
    {
        // Walk the full public instance surface (incl. inherited from base
        // repositories) — DeclaredOnly would let forbidden APIs declared on a
        // base repo evade the rule on derived types. Exclude System.Object
        // members (Equals/GetHashCode/ToString/GetType) which can never return
        // IQueryable anyway and just add noise to debug output.
        IEnumerable<MethodInfo> leakingMethods = repoType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType != typeof(object))
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
    public void Repository_WhenInspected_ExposesNoCommitStyleMethods(Type repoType)
    {
        // We can't easily inspect call sites with reflection alone, but we CAN
        // detect a SaveChanges/SaveChangesAsync helper exposed on the repository
        // itself — which would invite handlers to commit via the repo. The
        // contract is: persistence is IUnitOfWork's job, period.
        MethodInfo[] suspectMethods = repoType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.DeclaringType != typeof(object))
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
        // Use assignability rather than exact-type match so concrete types that
        // implement IQueryable (e.g. EF Core's IIncludableQueryable<,>, or a
        // custom IQueryable wrapper) are still flagged.
        Type unwrapped = UnwrapTask(returnType);
        return typeof(IQueryable).IsAssignableFrom(unwrapped);
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
