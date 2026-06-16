namespace Axis.Identity.Application.Queries.GetTeamAccountSettings;

public sealed record TeamAccountSettingsDto(
    Guid TeamAccountId,
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
