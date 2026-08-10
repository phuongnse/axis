using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Domain.Aggregates;

public sealed class Organization : AggregateRoot<Guid>
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;

    private Organization(Guid id, string name) : base(id)
    {
        Name = NormalizeName(name);
        CreatedAt = DateTime.UtcNow;
        Revision = 1;
    }

    public string Name { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public int Revision { get; private set; }

    public static Organization Create(string name) => new(Guid.NewGuid(), name);

    private static string NormalizeName(string name)
    {
        string normalized = name?.Trim().Normalize() ?? string.Empty;
        if (normalized.Length is < MinNameLength or > MaxNameLength)
            throw new ArgumentException($"Organization name must be between {MinNameLength} and {MaxNameLength} characters.", nameof(name));
        return normalized;
    }
}
