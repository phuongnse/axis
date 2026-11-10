using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>The disposable domain check of sign-up, see <c>docs/reference/sign-up.md#bots-and-abuse</c>.</summary>
internal static class DisposableDomains
{
    /// <summary>
    /// Whether the email's domain, or a parent domain of it with at least two labels, is in <c>control_plane.disposable_domains</c>.
    /// A bare top-level domain never matches, so one bad row cannot refuse every address under it.
    /// </summary>
    public static async Task<bool> IsListedAsync(NpgsqlDataSource controlPlane, string email, CancellationToken cancellationToken)
    {
        var domain = email[(email.LastIndexOf('@') + 1)..].ToLowerInvariant();
        var labels = domain.Split('.');
        var candidates = new List<string>();
        for (var first = 0; first < labels.Length - 1; first++)
        {
            candidates.Add(string.Join('.', labels[first..]));
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        await using var command = controlPlane.CreateCommand("SELECT EXISTS (SELECT 1 FROM control_plane.disposable_domains WHERE domain = ANY(@domains))");
        command.Parameters.AddWithValue("domains", candidates.ToArray());
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
