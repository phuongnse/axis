using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Axis.Data.Storage;
using Axis.Server.Authentication;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Microsoft.AspNetCore.Authentication;

namespace Axis.Server.Users;

/// <summary>
/// A person's own columns and saved views of a table, see <c>docs/reference/frontend.md#table-views-and-columns</c>. Every route takes a
/// session only, so a service account's bearer token gets a 401, and each query names the signed-in person, so nobody reads or changes
/// another person's rows. A route checks, in order, the session (401), the key (400), the body (400), the person's own view (404) and then
/// the name and the count of views (409). A save also takes a lock for the person and the table before it counts, so two saves at once
/// cannot pass the limit of 50 views. A key that nothing is kept for answers no columns and no views, never a 404.
/// </summary>
internal static partial class TableViewEndpoints
{
    private const int MaxKeyLength = 200;
    private const int MaxNameLength = 80;
    private const int MaxFilterBytes = 8192;
    private const int MaxColumns = 100;

    public static void MapTableViewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/tables/{key}", ReadAsync);
        endpoints.MapPut("/api/me/tables/{key}/columns", SetColumnsAsync);
        endpoints.MapDelete("/api/me/tables/{key}/columns", ResetColumnsAsync);
        endpoints.MapPost("/api/me/tables/{key}/views", SaveViewAsync);
        endpoints.MapPut("/api/me/tables/{key}/views/{id}", ReplaceViewAsync);
        endpoints.MapDelete("/api/me/tables/{key}/views/{id}", DeleteViewAsync);
    }

    private static async Task<IResult> ReadAsync(string key, HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        if (!KeyPattern().IsMatch(key) || key.Length > MaxKeyLength)
        {
            return BadKey();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        var (columns, views) = await TableViews.ReadAsync(connection, userId, key, cancellationToken);
        return Results.Ok(new TableViewsResponse(columns, [.. views.Select(ToResponse)]));
    }

    private static async Task<IResult> SetColumnsAsync(string key, HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        if (!KeyPattern().IsMatch(key) || key.Length > MaxKeyLength)
        {
            return BadKey();
        }

        var body = await ReadBodyAsync<TableColumnsRequest>(httpContext, cancellationToken);
        if (body?.Columns is not { Length: <= MaxColumns } columns
            || columns.Any(string.IsNullOrEmpty)
            || columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "The columns are a list of at most 100 keys, none empty and none repeated.");
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await TableViews.SetColumnsAsync(connection, userId, key, columns!, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ResetColumnsAsync(string key, HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        if (!KeyPattern().IsMatch(key) || key.Length > MaxKeyLength)
        {
            return BadKey();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        await TableViews.ResetColumnsAsync(connection, userId, key, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveViewAsync(string key, HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        if (!KeyPattern().IsMatch(key) || key.Length > MaxKeyLength)
        {
            return BadKey();
        }

        if (await ReadViewAsync(httpContext, Guid.CreateVersion7(), cancellationToken) is not { } view)
        {
            return BadView();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await TableViews.SaveAsync(connection, userId, key, view, cancellationToken) switch
        {
            TableViews.WriteResult.Done => Results.Created($"/api/me/tables/{key}/views/{view.Id}", ToResponse(view)),
            TableViews.WriteResult.TooMany => Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "A person keeps at most 50 views for a table."),
            _ => NameTaken(),
        };
    }

    private static async Task<IResult> ReplaceViewAsync(
        string key, string id, HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        if (!KeyPattern().IsMatch(key) || key.Length > MaxKeyLength)
        {
            return BadKey();
        }

        // The id comes from the address. A malformed one is a 404, like an unknown one, after the body is checked.
        var parsed = Guid.TryParse(id, out var viewId);
        if (await ReadViewAsync(httpContext, viewId, cancellationToken) is not { } view)
        {
            return BadView();
        }

        if (!parsed)
        {
            return NotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await TableViews.ReplaceAsync(connection, userId, key, view, cancellationToken) switch
        {
            TableViews.WriteResult.Done => Results.NoContent(),
            TableViews.WriteResult.NotFound => NotFound(),
            _ => NameTaken(),
        };
    }

    private static async Task<IResult> DeleteViewAsync(
        string key, string id, HttpContext httpContext, TenantDatabase database, CancellationToken cancellationToken)
    {
        if (await SignedInAsync(httpContext) is not { } userId)
        {
            return Grants.SignInRequired();
        }

        if (!KeyPattern().IsMatch(key) || key.Length > MaxKeyLength)
        {
            return BadKey();
        }

        if (!Guid.TryParse(id, out var viewId))
        {
            return NotFound();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        return await TableViews.DeleteAsync(connection, userId, key, viewId, cancellationToken) == TableViews.WriteResult.Done
            ? Results.NoContent()
            : NotFound();
    }

    // The session only: CurrentUsers would also accept a service account's bearer token.
    private static async Task<string?> SignedInAsync(HttpContext httpContext)
    {
        var result = await httpContext.AuthenticateAsync(Session.Scheme);
        return result.Succeeded ? Session.Read(result.Principal)?.Id : null;
    }

    private static async Task<T?> ReadBodyAsync<T>(HttpContext httpContext, CancellationToken cancellationToken)
        where T : class
    {
        if (!httpContext.Request.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await httpContext.Request.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The name is trimmed. The three fields are the address's values as they are, and together they are at most 8,192 bytes of UTF-8.
    private static async Task<TableViews.View?> ReadViewAsync(HttpContext httpContext, Guid id, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync<TableViewRequest>(httpContext, cancellationToken);
        var name = body?.Name?.Trim();
        if (body is null
            || name is not { Length: >= 1 and <= MaxNameLength }
            || Bytes(body.Search) + Bytes(body.Filter) + Bytes(body.Sort) > MaxFilterBytes)
        {
            return null;
        }

        return new TableViews.View(id, name, body.Search, body.Filter, body.Sort);
    }

    private static int Bytes(string? value) => value is null ? 0 : Encoding.UTF8.GetByteCount(value);

    private static TableViewResponse ToResponse(TableViews.View view) => new(view.Id, view.Name, view.Search, view.Filter, view.Sort);

    private static IResult BadKey() =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The table key is not in the form of a table key.");

    private static IResult BadView() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "A view needs a name of 1 to 80 characters, and its search, filter and sort are at most 8,192 bytes together.");

    private static IResult NameTaken() =>
        Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Another view of this table has this name.");

    private static IResult NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The person has no view of this table with this id.");

    [GeneratedRegex(@"^[A-Za-z0-9_-]+(\.[A-Za-z0-9_-]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

/// <summary>The body of <c>PUT /api/me/tables/{key}/columns</c>.</summary>
internal sealed record TableColumnsRequest(string[]? Columns);

/// <summary>The body of a save or a replace of a view.</summary>
internal sealed record TableViewRequest(string? Name, string? Search, string? Filter, string? Sort);

/// <summary>One saved view of a table.</summary>
internal sealed record TableViewResponse(Guid Id, string Name, string? Search, string? Filter, string? Sort);

/// <summary>The answer of <c>GET /api/me/tables/{key}</c>. <c>Columns</c> is null when the person keeps none.</summary>
internal sealed record TableViewsResponse(IReadOnlyList<string>? Columns, IReadOnlyList<TableViewResponse> Views);
