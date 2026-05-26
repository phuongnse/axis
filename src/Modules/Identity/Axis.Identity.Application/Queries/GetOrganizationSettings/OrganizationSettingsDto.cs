namespace Axis.Identity.Application.Queries.GetOrganizationSettings;

public sealed record OrganizationSettingsDto(
    Guid OrganizationId,
    string Name,
    string Slug,
    string? LogoUrl,
    string PlanName,
    string Status,
    DateTime CreatedAt,
    string? TimeZoneId,
    string? DefaultLanguage,
    DateTime? ScheduledHardDeleteAt,
    UsageStatsDto Usage);

public sealed record UsageStatsDto(
    int WorkflowsUsed,
    int? WorkflowsLimit,
    int ExecutionsUsedThisMonth,
    int? ExecutionsPerMonthLimit,
    int UsersUsed,
    int? UsersLimit);
