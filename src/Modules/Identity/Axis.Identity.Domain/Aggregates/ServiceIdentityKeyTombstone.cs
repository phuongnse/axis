namespace Axis.Identity.Domain.Aggregates;

public sealed class ServiceIdentityKeyTombstone
{
    private ServiceIdentityKeyTombstone() { }
    internal ServiceIdentityKeyTombstone(Guid id, string kid, string thumbprint, DateTime revokedAt)
    { Id = id; Kid = kid; Thumbprint = thumbprint; RevokedAt = revokedAt; }
    public Guid Id { get; private set; }
    public string Kid { get; private set; } = null!;
    public string Thumbprint { get; private set; } = null!;
    public DateTime RevokedAt { get; private set; }
}
