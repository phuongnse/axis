using System.Text.RegularExpressions;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

public interface ITenantSlugGenerator
{
    string GenerateBaseSlug(string tenantName);

    Task<TenantSlug> GenerateUniqueSlugAsync(string tenantName, CancellationToken cancellationToken);
}

public sealed class TenantSlugGenerator(ITenantRepository tenantRepo) : ITenantSlugGenerator
{
    // TenantSlug allows at most 63 chars; reserve room for a "-9999" suffix.
    private const int MaxSlugLength = 63;
    private const int SuffixReserve = 5;

    public string GenerateBaseSlug(string tenantName) =>
        Regex.Replace((tenantName ?? string.Empty).ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-")
            .Trim('-');

    public async Task<TenantSlug> GenerateUniqueSlugAsync(
        string tenantName,
        CancellationToken cancellationToken)
    {
        string baseSlug = Truncate(GenerateBaseSlug(tenantName), MaxSlugLength);
        Result<TenantSlug> candidate = TenantSlug.Create(baseSlug);

        if (candidate.IsSuccess && !await tenantRepo.SlugExistsAsync(candidate.Value, cancellationToken))
            return candidate.Value;

        string suffixBase = Truncate(baseSlug, MaxSlugLength - SuffixReserve);
        for (int i = 0; i < 10; i++)
        {
            string suffix = Random.Shared.Next(1000, 9999).ToString();
            Result<TenantSlug> withSuffix = TenantSlug.Create($"{suffixBase}-{suffix}");
            if (withSuffix.IsSuccess && !await tenantRepo.SlugExistsAsync(withSuffix.Value, cancellationToken))
                return withSuffix.Value;
        }

        return TenantSlug.Create($"Tenant-{Guid.NewGuid():N}"[..20]).Value;
    }

    // Cut to maxLength, then strip any hyphen the cut may have left at the edge
    // (a slug may not start or end with a hyphen).
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].Trim('-');
}
