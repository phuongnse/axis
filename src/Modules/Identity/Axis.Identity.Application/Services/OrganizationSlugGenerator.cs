using System.Text.RegularExpressions;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;

namespace Axis.Identity.Application.Services;

public interface IOrganizationSlugGenerator
{
    string GenerateBaseSlug(string orgName);

    Task<OrganizationSlug> GenerateUniqueSlugAsync(string orgName, CancellationToken cancellationToken);
}

public sealed class OrganizationSlugGenerator(IOrganizationRepository orgRepo) : IOrganizationSlugGenerator
{
    public string GenerateBaseSlug(string orgName) =>
        Regex.Replace(orgName.ToLowerInvariant().Trim(), @"[^a-z0-9]+", "-")
            .Trim('-');

    public async Task<OrganizationSlug> GenerateUniqueSlugAsync(
        string orgName,
        CancellationToken cancellationToken)
    {
        string baseSlug = GenerateBaseSlug(orgName);
        Result<OrganizationSlug> candidate = OrganizationSlug.Create(baseSlug);

        if (candidate.IsSuccess && !await orgRepo.SlugExistsAsync(candidate.Value, cancellationToken))
            return candidate.Value;

        for (int i = 0; i < 10; i++)
        {
            string suffix = Random.Shared.Next(1000, 9999).ToString();
            Result<OrganizationSlug> withSuffix = OrganizationSlug.Create($"{baseSlug}-{suffix}");
            if (withSuffix.IsSuccess && !await orgRepo.SlugExistsAsync(withSuffix.Value, cancellationToken))
                return withSuffix.Value;
        }

        return OrganizationSlug.Create($"org-{Guid.NewGuid():N}"[..20]).Value;
    }
}
