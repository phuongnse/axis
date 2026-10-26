using Axis.Configuration.Model;
using Axis.Data.Profiles;

namespace Axis.Integration.Tests;

/// <summary>The copy of StepApp with the process <c>NotifyMail</c> that the notify and email tests share.</summary>
internal static class NotifyMailApp
{
    /// <summary>The address <c>NotifyMail</c> notifies in <see cref="ActivateAsync"/> unless the test gives another.</summary>
    internal const string DefaultAddress = "ops@example.test";

    /// <summary>
    /// Activates a copy of StepApp with the process NotifyMail and Vietnamese texts. The process notifies the title user and
    /// <paramref name="address"/> by both channels, and the comment user, who is null.
    /// </summary>
    internal static async Task<(Guid ReleaseId, ApplicationModel Model)> ActivateAsync(
        ProcessStepFixture fixture,
        CancellationToken cancellationToken,
        string address = DefaultAddress)
    {
        var process = await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "processes", "notify.json"), cancellationToken);
        process = process
            .Replace("d74f217a-8513-4825-9467-c3aa720d70a5", "d74f217a-8513-4825-9467-c3aa720d70b6", StringComparison.Ordinal)
            .Replace("\"name\": \"Notify\"", "\"name\": \"NotifyMail\"", StringComparison.Ordinal)
            .Replace("""{ "role": "reviewer" }""", $$"""{ "email": "'{{address}}'" }""", StringComparison.Ordinal)
            .Replace("""["inApp"]""", """["inApp", "email"]""", StringComparison.Ordinal);
        var texts = await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "texts", "en.json"), cancellationToken);
        texts = texts
            .Replace("0b6e1f4c-5d2a-4e8b-9c3f-7a1d2e4b6c81", "0b6e1f4c-5d2a-4e8b-9c3f-7a1d2e4b6c82", StringComparison.Ordinal)
            .Replace("\"TextsEn\"", "\"TextsVi\"", StringComparison.Ordinal)
            .Replace("\"locale\": \"en\"", "\"locale\": \"vi\"", StringComparison.Ordinal)
            .Replace("\"Amount {amount}.\"", "\"Số tiền {amount}.\"", StringComparison.Ordinal)
            .Replace("\"Request {title}\"", "\"Yêu cầu {title}\"", StringComparison.Ordinal);
        var site = await File.ReadAllTextAsync(Path.Combine(ProcessStepFixture.StepAppFolder, "sites", "steps.json"), cancellationToken);
        site = site.Replace("\"available\": [\"en\"]", "\"available\": [\"en\", \"vi\"]", StringComparison.Ordinal);
        return await fixture.ActivateCopyWithFilesAsync(
            ("processes/notify-mail.json", process),
            ("texts/vi.json", texts),
            ("sites/steps.json", site));
    }

    /// <summary>Stores the profile of the user <paramref name="userId"/> with a name, <paramref name="locale"/> and <paramref name="email"/>.</summary>
    internal static async Task UpsertProfileAsync(
        ProcessStepFixture fixture,
        string userId,
        string locale,
        string email,
        CancellationToken cancellationToken)
    {
        await using var connection = await fixture.DataSource.OpenConnectionAsync(cancellationToken);
        await UserProfiles.UpsertAsync(connection, userId, "Requester", email, locale, cancellationToken);
    }
}
