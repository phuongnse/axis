using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>
/// Runs a <c>sendSignUpConfirm</c> job: sends the confirm email of a pending sign-up, see
/// <c>docs/reference/control-plane.md#its-own-work</c>. The payload is only <c>{ signUpId }</c>, because a payload is stored in the
/// database, and the job reads the name, the address, the subdomain and the language from the sign-up. It makes the token, 32 random
/// bytes written as base64url, writes the SHA-256 of the bytes to the sign-up and only then sends the link
/// <c>&lt;ControlPlane:PublicUrl&gt;/sign-up/verify#&lt;token&gt;</c>, so a token that was never delivered cannot stay valid. A retry
/// makes a new token, and only the link of the latest email that was sent works. A sign-up that is no longer pending is skipped.
/// The token and the link are never logged and never stored.
/// </summary>
internal sealed class SignUpConfirmJobHandler(NpgsqlDataSource controlPlane, ControlPlaneOptions options, EmailTemplates templates) : IJobHandler
{
    public const string JobKind = "sendSignUpConfirm";

    /// <summary>The kind of email template of the confirm email. <see cref="EmailJobs"/> refuses it, as its link holds a token.</summary>
    public const string Template = "signUpConfirm";

    private const int TokenBytes = 32;

    public string Kind => JobKind;

    public async Task RunAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Payload["signUpId"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var text) || !Guid.TryParse(text, out var signUpId))
        {
            throw new InvalidOperationException($"The {JobKind} payload has no signUpId.");
        }

        string fullName, email, subdomain, language;
        await using (var read = controlPlane.CreateCommand("""
            SELECT full_name, email, subdomain, language FROM control_plane.sign_ups
            WHERE id = @id AND state = 'pending' AND expires_at > now()
            """))
        {
            read.Parameters.AddWithValue("id", signUpId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return;
            }

            (fullName, email, subdomain, language) = (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        }

        var token = RandomNumberGenerator.GetBytes(TokenBytes);
        await using (var store = controlPlane.CreateCommand(
            "UPDATE control_plane.sign_ups SET token_hash = @hash WHERE id = @id AND state = 'pending'"))
        {
            store.Parameters.AddWithValue("hash", SHA256.HashData(token));
            store.Parameters.AddWithValue("id", signUpId);
            if (await store.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                return;
            }
        }

        await MailRelay.SendAsync(
            options,
            templates,
            job.Id,
            Template,
            language,
            email,
            new Dictionary<string, string>
            {
                ["name"] = fullName,
                ["tenantAddress"] = $"https://{subdomain}.{options.TenantDomain!.ToLowerInvariant()}",
                ["confirmLink"] = $"{options.PublicUrl}/sign-up/verify#{Base64Url.EncodeToString(token)}",
            },
            cancellationToken);
    }
}
