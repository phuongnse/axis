namespace Axis.Identity.Infrastructure.Persistence.Entities;

internal sealed class ServiceAssertionReplayRecord { public string Digest { get; set; } = null!; public DateTime ExpiresAt { get; set; } public DateTime CreatedAt { get; set; } }
