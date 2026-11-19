using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Data.Storage;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Processes;
using Axis.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// A person's own notification preferences, see <c>docs/reference/notifications.md#notification-preferences</c>. Both routes take a session
/// only, so a service account's bearer token gets a 401, and each query names the signed-in person, so no route reads or changes
/// another person's row. A save replaces the whole set. It names every source and every field, and a bad value is a 400 keyed by its
/// JSON Pointer that stores nothing.
/// </summary>
internal static class NotificationPreferenceEndpoints
{
    private const string SourcesProperty = "sources";
    private const string DigestHourProperty = "digestHour";
    private const string TimeZoneProperty = "timeZone";
    private const string ChannelsProperty = "channels";
    private const string ApplicationProperty = "application";
    private const string EmailProperty = "email";
    private const string DigestProperty = "digest";

    private const string Required = "Required.";
    private const string Unknown = "Unknown property.";

    public static void MapNotificationPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/notification-preferences", ReadAsync);
        endpoints.MapPut("/api/me/notification-preferences", ReplaceAsync);
    }

    private static async Task<IResult> ReadAsync(HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return Results.Ok(ToResponse(await NotificationPreferences.ReadAsync(connection, userId, cancellationToken)));
    }

    private static async Task<IResult> ReplaceAsync(HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        if (!JsonRequest.IsJson(httpContext.Request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var body = await ProcessOperatorEndpoints.ReadBodyAsync(httpContext.Request, cancellationToken);
        var (preferences, errors) = ProcessOperatorEndpoints.ReadObject(body, [SourcesProperty, DigestHourProperty, TimeZoneProperty], Read);
        if (errors is not null)
        {
            return Results.ValidationProblem(errors);
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await NotificationPreferences.ReplaceAsync(connection, userId, preferences!, cancellationToken);
        return Results.NoContent();
    }

    // The session only: CurrentUsers would also accept a service account's bearer token.
    private static async Task<string?> SignedInAsync(HttpContext httpContext)
    {
        var result = await httpContext.AuthenticateAsync(Session.Scheme);
        return result.Succeeded ? Session.Read(result.Principal)?.Id : null;
    }

    private static NotificationPreferences.Preferences? Read(Dictionary<string, JsonElement> properties, SortedDictionary<string, string[]> errors)
    {
        var sources = ReadSources(properties, errors);
        var hour = ReadHour(properties, errors);
        var zone = ReadZone(properties, errors);
        return errors.Count > 0 ? null : new NotificationPreferences.Preferences(sources!, hour, zone!);
    }

    private static Dictionary<string, NotificationPreferences.Source>? ReadSources(
        Dictionary<string, JsonElement> properties, SortedDictionary<string, string[]> errors)
    {
        const string path = "/" + SourcesProperty;
        if (!properties.TryGetValue(SourcesProperty, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errors[path] = [Required];
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            errors[path] = ["Must be an object."];
            return null;
        }

        var members = ReadMembers(element, NotificationPreferences.SourceNames, path, errors);
        var sources = new Dictionary<string, NotificationPreferences.Source>(StringComparer.Ordinal);
        foreach (var name in NotificationPreferences.SourceNames)
        {
            var sourcePath = $"{path}/{name}";
            if (!members.TryGetValue(name, out var source) || source.ValueKind == JsonValueKind.Null)
            {
                errors[sourcePath] = [Required];
            }
            else if (source.ValueKind != JsonValueKind.Object)
            {
                errors[sourcePath] = ["Must be an object."];
            }
            else if (ReadSource(source, sourcePath, errors) is { } read)
            {
                sources[name] = read;
            }
        }

        return sources;
    }

    private static NotificationPreferences.Source? ReadSource(JsonElement source, string path, SortedDictionary<string, string[]> errors)
    {
        var members = ReadMembers(source, [ChannelsProperty, DigestProperty], path, errors);
        var digest = ReadBoolean(members, DigestProperty, path, errors);
        var channelsPath = $"{path}/{ChannelsProperty}";
        bool? application = null;
        bool? email = null;
        if (!members.TryGetValue(ChannelsProperty, out var channels) || channels.ValueKind == JsonValueKind.Null)
        {
            errors[channelsPath] = [Required];
        }
        else if (channels.ValueKind != JsonValueKind.Object)
        {
            errors[channelsPath] = ["Must be an object."];
        }
        else
        {
            var channelMembers = ReadMembers(channels, [ApplicationProperty, EmailProperty], channelsPath, errors);
            application = ReadBoolean(channelMembers, ApplicationProperty, channelsPath, errors);
            email = ReadBoolean(channelMembers, EmailProperty, channelsPath, errors);
        }

        return application is { } a && email is { } e && digest is { } d ? new NotificationPreferences.Source(a, e, d) : null;
    }

    // The members of an object among the allowed names. An unknown or a repeated member is an error at its own pointer.
    private static Dictionary<string, JsonElement> ReadMembers(
        JsonElement element, string[] allowed, string path, SortedDictionary<string, string[]> errors)
    {
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (!allowed.Contains(member.Name, StringComparer.Ordinal))
            {
                errors.TryAdd($"{path}/{Escape(member.Name)}", [Unknown]);
            }
            else if (!members.TryAdd(member.Name, member.Value))
            {
                errors.TryAdd($"{path}/{member.Name}", ["Must not be repeated."]);
            }
        }

        return members;
    }

    private static bool? ReadBoolean(Dictionary<string, JsonElement> members, string name, string path, SortedDictionary<string, string[]> errors)
    {
        if (!members.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errors[$"{path}/{name}"] = [Required];
            return null;
        }

        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors[$"{path}/{name}"] = ["Must be true or false."];
            return null;
        }

        return element.GetBoolean();
    }

    private static int ReadHour(Dictionary<string, JsonElement> properties, SortedDictionary<string, string[]> errors)
    {
        const string path = "/" + DigestHourProperty;
        if (!properties.TryGetValue(DigestHourProperty, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errors[path] = [Required];
            return 0;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var hour) || hour is < 0 or > 23)
        {
            errors[path] = ["Must be a whole hour from 0 to 23."];
            return 0;
        }

        return hour;
    }

    private static string? ReadZone(Dictionary<string, JsonElement> properties, SortedDictionary<string, string[]> errors)
    {
        const string path = "/" + TimeZoneProperty;
        if (!properties.TryGetValue(TimeZoneProperty, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            errors[path] = [Required];
            return null;
        }

        if (element.ValueKind != JsonValueKind.String || !IanaTimeZones.TryFind(element.GetString()!, out _))
        {
            errors[path] = ["Must be an IANA time zone name."];
            return null;
        }

        return element.GetString();
    }

    private static string Escape(string name) =>
        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static NotificationPreferencesResponse ToResponse(NotificationPreferences.Preferences preferences)
    {
        SourceResponse Source(string name)
        {
            var source = preferences.Sources[name];
            return new SourceResponse(new ChannelsResponse(source.Application, source.Email), source.Digest);
        }

        return new NotificationPreferencesResponse(
            new SourcesResponse(Source("task"), Source("mention"), Source("process"), Source("authoringRequest")),
            preferences.DigestHour,
            preferences.TimeZone);
    }
}

/// <summary>The answer of <c>GET /api/me/notification-preferences</c>.</summary>
internal sealed record NotificationPreferencesResponse(SourcesResponse Sources, int DigestHour, string TimeZone);

/// <summary>The four sources of notices, each with the person's choice.</summary>
internal sealed record SourcesResponse(SourceResponse Task, SourceResponse Mention, SourceResponse Process, SourceResponse AuthoringRequest);

/// <summary>How one source reaches a person.</summary>
internal sealed record SourceResponse(ChannelsResponse Channels, bool Digest);

/// <summary>Whether a source shows in the application and whether it is sent by email.</summary>
internal sealed record ChannelsResponse(bool Application, bool Email);
