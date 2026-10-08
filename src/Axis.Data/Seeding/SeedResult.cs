using Axis.Configuration.Diagnostics;

namespace Axis.Data.Seeding;

/// <summary>
/// The outcome of applying an application's seeds: the diagnostics that stopped it, or none and
/// the number of seed records inserted and updated. Nothing is written when there are diagnostics.
/// </summary>
public sealed record SeedResult(IReadOnlyList<Diagnostic> Diagnostics, int Inserted, int Updated);
