namespace Axis.Identity.Domain.Aggregates;

public sealed class ServiceIdentityKey
{
    private ServiceIdentityKey() { }

    internal ServiceIdentityKey(Guid id, string kid, string thumbprint, string x, string y, DateTime createdAt)
    {
        Id = id; Kid = kid; Thumbprint = thumbprint; X = x; Y = y; CreatedAt = createdAt;
        Status = ServiceIdentityKeyStatus.Active;
    }

    public Guid Id { get; private set; }
    public string Kid { get; private set; } = null!;
    public string Thumbprint { get; private set; } = null!;
    public string X { get; private set; } = null!;
    public string Y { get; private set; } = null!;
    public ServiceIdentityKeyStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    internal void Revoke(DateTime at) { if (Status == ServiceIdentityKeyStatus.Active) { Status = ServiceIdentityKeyStatus.Revoked; RevokedAt = at; } }
}
