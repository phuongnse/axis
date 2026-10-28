using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Axis.Tenancy;

/// <summary>
/// The rule that a host's own database connection string sets <c>SSL Mode=VerifyFull</c> outside Development and Testing.
/// A message names the setting and never holds its value.
/// </summary>
public static class DatabaseTlsRequirement
{
    /// <summary>Whether the rule applies: every environment except Development and Testing.</summary>
    public static bool Applies(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return !environment.IsDevelopment() && !environment.IsEnvironment("Testing");
    }

    /// <summary>
    /// Returns what is wrong with <paramref name="connectionString"/>, or null when it is blank or sets
    /// <c>SSL Mode=VerifyFull</c>. A blank value is left to the check that the setting is present.
    /// </summary>
    public static string? Problem(string setting, string? connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            if (new NpgsqlConnectionStringBuilder(connectionString).SslMode == SslMode.VerifyFull)
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return $"The setting {setting} is not a valid connection string.";
        }

        return $"The setting {setting} must set SSL Mode=VerifyFull outside Development.";
    }

    /// <summary>Throws when the rule applies in <paramref name="environment"/> and <paramref name="value"/> breaks it.</summary>
    public static void Require(IHostEnvironment environment, string setting, string? value)
    {
        if (!Applies(environment))
        {
            return;
        }

        var problem = Problem(setting, value);
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }
    }
}
