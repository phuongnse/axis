using Axis.Configuration.Diagnostics;

namespace Axis.Data.Seeding;

/// <summary>
/// The outcome of applying an application's seeds: the diagnostics that stopped it, or none and
/// the number of seed records inserted. Nothing is inserted when there are diagnostics.
/// </summary>
public sealed record SeedResult(IReadOnlyList<Diagnostic> Diagnostics, int Inserted);
