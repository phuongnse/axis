using Axis.ControlPlane.SignUp;

namespace Axis.ControlPlane.Plans;

/// <summary>
/// One entry of <c>ControlPlane:Plans</c>: a plan version, see <c>docs/reference/plans.md#the-catalogue</c>. Every number is bound as text,
/// so the start check, not the configuration binder, words the message of a bad number and never holds its value.
/// </summary>
public sealed class PlanOptions
{
    /// <summary>The plan code, such as <c>team</c>.</summary>
    public string? Code { get; set; }

    /// <summary>The version of the plan, a whole number from 1.</summary>
    public string? Version { get; set; }

    /// <summary>The plan's name, in each language.</summary>
    public SignUpTexts Name { get; set; } = new();

    /// <summary>The users that the plan includes.</summary>
    public string? IncludedUsers { get; set; }

    /// <summary>Whether the plan is the trial plan, which sets a new trial's quotas, see <c>docs/reference/plans.md#the-catalogue</c>. A trial plan has no add-on and at most 10 included users, and every trial plan has the same code.</summary>
    public bool Trial { get; set; }

    /// <summary>The quotas of the plan by quota key, every key of the tenant quotas except <c>users</c>.</summary>
    public Dictionary<string, string> Quotas { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The user packs that a subscription of the plan can buy.</summary>
    public List<PlanAddOnOptions> AddOns { get; } = [];
}

/// <summary>A user pack of a plan.</summary>
public sealed class PlanAddOnOptions
{
    /// <summary>The add-on code, such as <c>users-10</c>, unique in its plan version.</summary>
    public string? Code { get; set; }

    /// <summary>The users that the pack adds.</summary>
    public string? Users { get; set; }
}
