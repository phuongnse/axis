using System.Security.Cryptography;
using Axis.Configuration.Model;
using Axis.Core.Tenancy;
using Axis.Data.Files;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Authentication;
using Axis.Server.Files;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Axis.Server.Records;

/// <summary>
/// The upload route of a file field, <c>POST /api/apps/{app}/entities/{entity}/fields/{field}/files</c>, and
/// <c>.../fields/{collection}/{field}/files</c> for a field of a child entity. It takes one
/// <c>multipart/form-data</c> part named <c>file</c>, and answers 201 with the id, name, media type and size of the
/// stored file. The file is stored as <c>pending</c>, owned by the session user, the entity and the field path. It
/// belongs to no record until a record write attaches it. Service accounts may upload, as they may write records.
/// A request is checked in order: session or bearer token (401), then the path, where an unknown entity, an unknown
/// field and a field that is no file field are 404, then the user's grant (403), then the request content type
/// (415), and then the body. The grant is a <c>create</c> or <c>update</c> grant on the entity whose <c>fields</c>
/// cover the field. For a child field it must cover the owner's child collection field. Record filters are not
/// read, because an upload has no record yet.
/// A body is checked in order: a missing, extra or malformed part, a part without a file name and an empty file
/// are 400, a media type that the field's <c>accept</c> refuses is 415, a declared <c>image/svg+xml</c> is 415
/// whatever <c>accept</c> says because an SVG can carry scripts, a file over the field's <c>maxSize</c> or
/// the host's <c>Files:MaxSize</c> is 413 as soon as the limit is passed, and a file that starts like an
/// executable, or does not start as its declared PDF, PNG, JPEG, GIF, WebP or ZIP type does, is 415. A part
/// without a content type is stored as <c>application/octet-stream</c>, and the parameters of a media type are dropped.
/// The stored name is the last path segment of the file name, without control characters, cut to 255 characters.
/// The problem titles never contain text from the request.
/// The download route, <c>GET /api/apps/{app}/files/{id}</c>, sends the bytes of an attached file to a session user
/// or a service account that could read the field on the owner record. A request is checked in order: session or
/// bearer token (401), then the application (404), and then the file. A file of a child row goes through its owner
/// record, and the user's read grant must cover the owner's child collection field. Every failure after sign-in is
/// one 404: no read grant on the owner, a record filter that excludes it or reads a computed field that is being
/// recomputed, a field the user may not read, a file that is not <c>attached</c>, a file of another application,
/// an unknown id and a malformed id. The answer is an attachment with the stored media type and file name, and the
/// security headers of every API answer add <c>nosniff</c>. The checks run under the interactive query timeout, then
/// the bytes stream from the database to the client, which sets <c>Content-Length</c>. An upload is written to a
/// temporary file as it arrives, and streamed from there into the database, so no file is held whole in memory.
/// </summary>
internal static class FileEndpoints
{
    private const int MaxNameLength = 255;
    private const int ChunkSize = 81920;
    private const long RequestSizeMargin = 1_048_576;
    private const string OctetStream = "application/octet-stream";
    private const string Svg = "image/svg+xml";

    public static void MapFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var settings = endpoints.ServiceProvider.GetRequiredService<FileSettings>();

        // The request carries the multipart framing too, so the body limit is a little above the file limit.
        // The route does not bind IFormFile, so no antiforgery metadata is added.
        foreach (var pattern in new[]
        {
            "/api/apps/{app}/entities/{entity}/fields/{field}/files",
            "/api/apps/{app}/entities/{entity}/fields/{collection}/{field}/files",
        })
        {
            endpoints.MapPost(pattern, UploadAsync)
                .WithMetadata(ServiceAccountRoute.Instance)
                .WithMetadata(new RequestSizeLimitAttribute(settings.MaxSize + RequestSizeMargin));
        }

        endpoints.MapGet("/api/apps/{app}/files/{id}", DownloadAsync)
            .WithMetadata(ServiceAccountRoute.Instance)
            .WithQueryTimeout(QueryTimeoutSettings.Interactive);
    }

    private static async Task<IResult> DownloadAsync(
        string app,
        string id,
        HttpContext httpContext,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        SensitiveValues sensitive,
        ITenantContextAccessor tenants,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(httpContext) is not { } user)
        {
            return Grants.SignInRequired(httpContext);
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No application is active under this name.");
        }

        // Only the hyphenated form names a file, as for a record.
        if (!Guid.TryParseExact(id, "D", out var fileId) || id.Length != 36)
        {
            return FileNotFound();
        }

        var application = active.Model;
        var connection = await database.GetConnectionAsync(cancellationToken);
        if (await FileStore.FindAttachedAsync(connection, database.ReadTransaction, application.Manifest.Id, fileId, cancellationToken) is not { } file
            || FileOwner(application, file.EntityId, file.FieldPath) is not { } target)
        {
            return FileNotFound();
        }

        var (owner, field) = target;

        var read = PolicyEvaluator.Access(application, user.RolesIn(application), owner, PolicyAction.Read);
        if (!read.Granted || !RecordFieldAccess.Read(application, user.RolesIn(application), owner).CanRead(field))
        {
            return FileNotFound();
        }

        // The entity is known only now, so a pending filter is a 404 too: a 409 would show that the file exists.
        if (read.Filters is { } filters
            && (await RecordCommand.ReadsPendingAsync(connection, database.ReadTransaction, application, owner, filters, loggerFactory, cancellationToken)
                || await RecordCommand.MatchAsync(connection, database.ReadTransaction, application, owner, file.OwnerRecordId, filters, user.Id, RecordEndpoints.Cipher(sensitive, tenants), loggerFactory, cancellationToken) is not { Found: true, Matched: true }))
        {
            return FileNotFound();
        }

        // The response is written here, not by a result, because the query timeout filter ends the read transaction
        // when this handler returns, and the bytes still come from it.
        var response = httpContext.Response;
        var found = await FileStore.CopyContentAsync(
            connection,
            database.ReadTransaction,
            file.Id,
            async (size, content) =>
            {
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = file.MediaType;
                response.ContentLength = size;

                // The file name goes in Content-Disposition: attachment, so a browser never shows the file in the application's origin.
                var disposition = new ContentDispositionHeaderValue("attachment");
                disposition.SetHttpFileName(file.Name);
                response.Headers.ContentDisposition = disposition.ToString();
                await content.CopyToAsync(response.Body, cancellationToken);
            },
            cancellationToken);
        return found ? Results.Empty : FileNotFound();
    }

    private static IResult FileNotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "No file has this id.");

    // The entity whose record owns the file, and the field of it that a read must cover: the file field itself, or
    // the child collection field for a file in a child row. Null when the path no longer names a file field.
    private static (EntityModel Owner, string Field)? FileOwner(ApplicationModel application, Guid entityId, string path)
    {
        if (application.FindEntity(entityId) is not { } entity)
        {
            return null;
        }

        var separator = path.IndexOf('/', StringComparison.Ordinal);
        var name = separator < 0 ? path : path[(separator + 1)..];
        if (!entity.TryGetField(name, out var field) || field.Type != FieldType.File)
        {
            return null;
        }

        if (separator < 0)
        {
            return application.IsChildEntity(entity) ? null : (entity, path);
        }

        // A child entity has exactly one owner entity.
        var collection = path[..separator];
        foreach (var candidate in application.Entities)
        {
            if (candidate.TryGetField(collection, out var collectionField)
                && collectionField.Type == FieldType.ChildCollection
                && collectionField.Target?.Id == entity.Id)
            {
                return (candidate, collection);
            }
        }

        return null;
    }

    private static async Task<IResult> UploadAsync(
        string app,
        string entity,
        string? collection,
        string field,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        FileSettings settings,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired(request.HttpContext);
        }

        var (application, model, notFound, _) = await RecordEndpoints.ResolveEntityAsync(app, entity, resolver, cancellationToken);
        if (model is null)
        {
            return notFound!;
        }

        if (FindFileField(application!, model, collection, field) is not { } target)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The entity has no file field with this name.");
        }

        // A child collection is one field to the record write check, so it is the one the grant must cover.
        if (!RecordFieldAccess.CanWrite(application!, user.RolesIn(application!), model, collection is null ? target.Field.Name : target.Owner!.Name))
        {
            return Grants.Forbidden();
        }

        // A slow upload holds no pooled connection while its body arrives.
        await database.ReleaseConnectionAsync();

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(contentType.Boundary).Length == 0)
        {
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "The request body must be multipart/form-data.");
        }

        var limit = Math.Min(target.Field.MaxSize ?? settings.MaxSize, settings.MaxSize);
        var upload = await ReadPartAsync(request, HeaderUtilities.RemoveQuotes(contentType.Boundary).ToString(), target.Field, limit, cancellationToken);
        if (upload.Failure is { } failure)
        {
            return failure;
        }

        await using var content = upload.Content!;
        content.Position = 0;
        var id = Guid.CreateVersion7();
        var file = new PendingFile(
            id,
            application!.Manifest.Id,
            target.Entity.Id,
            target.Path,
            upload.Name!,
            upload.MediaType!,
            upload.Size,
            upload.Sha256!,
            user.Id,
            DateTimeOffset.UtcNow);
        var connection = await database.GetConnectionAsync(cancellationToken);
        await FileStore.InsertPendingAsync(connection, file, content, cancellationToken);
        return Results.Json(new FileUploadResponse(id, file.Name, file.MediaType, file.Size), statusCode: StatusCodes.Status201Created);
    }

    // The file field of the route and the entity that declares it. A child field is declared by the child entity of
    // the owner's child collection field, and its path holds the collection name too.
    private static (FieldModel Field, EntityModel Entity, FieldModel? Owner, string Path)? FindFileField(
        ApplicationModel application,
        EntityModel owner,
        string? collection,
        string name)
    {
        if (collection is null)
        {
            return owner.TryGetField(name, out var field) && field.Type == FieldType.File
                ? (field, owner, null, field.Name)
                : null;
        }

        if (!owner.TryGetField(collection, out var collectionField)
            || collectionField.Type != FieldType.ChildCollection
            || application.FindEntity(collectionField.Target!.Id) is not { } child
            || !child.TryGetField(name, out var childField)
            || childField.Type != FieldType.File)
        {
            return null;
        }

        return (childField, child, collectionField, $"{collectionField.Name}/{childField.Name}");
    }

    // Reads the one part and checks it, so a failure holds the response and nothing is stored.
    private static async Task<UploadedPart> ReadPartAsync(
        HttpRequest request,
        string boundary,
        FieldModel field,
        long limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var reader = new MultipartReader(boundary, request.Body);
            if (await reader.ReadNextSectionAsync(cancellationToken) is not { } section
                || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                || !string.Equals(HeaderUtilities.RemoveQuotes(disposition.Name).Value, "file", StringComparison.Ordinal)
                || CleanName(disposition) is not { } name)
            {
                return UploadedPart.Fail(BadPart("The request must hold one part named file, with a file name."));
            }

            if (MediaType(section.ContentType) is not { } mediaType || mediaType == Svg || !Accepts(field.Accept, mediaType))
            {
                return UploadedPart.Fail(Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "The file's media type is not accepted."));
            }

            var content = TemporaryFile.Create("axis-upload");
            try
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[ChunkSize];
                var head = new byte[FileSignatures.HeadLength];
                var size = 0L;
                int read;
                while ((read = await section.Body.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    if (size + read > limit)
                    {
                        await content.DisposeAsync();
                        return UploadedPart.Fail(TooLarge());
                    }

                    // The first read can be shorter than the head, so the head fills over more reads.
                    if (size < head.Length)
                    {
                        var take = (int)Math.Min(read, head.Length - size);
                        buffer.AsSpan(0, take).CopyTo(head.AsSpan((int)size));
                    }

                    await content.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    size += read;
                }

                if (size == 0)
                {
                    await content.DisposeAsync();
                    return UploadedPart.Fail(BadPart("The file is empty."));
                }

                var start = head.AsSpan(0, (int)Math.Min(size, head.Length));
                if (FileSignatures.IsExecutable(start) || !FileSignatures.Matches(mediaType, start))
                {
                    await content.DisposeAsync();
                    return UploadedPart.Fail(Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "The file's content does not match an allowed type."));
                }

                if (await reader.ReadNextSectionAsync(cancellationToken) is not null)
                {
                    await content.DisposeAsync();
                    return UploadedPart.Fail(BadPart("The request must hold one part named file, with a file name."));
                }

                return new UploadedPart(null, name, mediaType, content, size, hash.GetHashAndReset());
            }
            catch
            {
                await content.DisposeAsync();
                throw;
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return UploadedPart.Fail(TooLarge());
        }
        catch (InvalidDataException)
        {
            return UploadedPart.Fail(BadPart("The request body is not valid multipart/form-data."));
        }
    }

    private static IResult BadPart(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["file"] = [message] });

    private static IResult TooLarge() =>
        Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "The file is larger than the limit for this field.");

    // The last path segment of the file name, with no control characters and at most 255 characters, or null for no name.
    private static string? CleanName(ContentDispositionHeaderValue disposition)
    {
        var raw = disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName;
        if (!raw.HasValue)
        {
            return null;
        }

        var text = HeaderUtilities.RemoveQuotes(raw).Value!;
        var name = new string(text[(text.LastIndexOfAny(['/', '\\']) + 1)..].Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (name.Length > MaxNameLength)
        {
            // Never cut between the two halves of a surrogate pair.
            var length = char.IsHighSurrogate(name[MaxNameLength - 1]) ? MaxNameLength - 1 : MaxNameLength;
            name = name[..length];
        }

        return name.Length == 0 ? null : name;
    }

    // The bare, lower-case media type of the part's content type, application/octet-stream when it has none, or null when it is no media type.
    private static string? MediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return OctetStream;
        }

        return MediaTypeHeaderValue.TryParse(contentType, out var parsed) && parsed.MediaType.HasValue && !parsed.MatchesAllTypes
            ? parsed.MediaType.Value!.ToLowerInvariant()
            : null;
    }

    // An entry is a media type, or a type with a subtype wildcard such as image/*.
    private static bool Accepts(IReadOnlyList<string>? accept, string mediaType) =>
        accept is null
        || accept.Any(entry => entry.EndsWith("/*", StringComparison.Ordinal)
            ? mediaType.StartsWith(entry[..^1], StringComparison.OrdinalIgnoreCase)
            : mediaType.Equals(entry, StringComparison.OrdinalIgnoreCase));

    // Content is a temporary file that the caller disposes, which deletes it.
    private sealed record UploadedPart(IResult? Failure, string? Name, string? MediaType, FileStream? Content, long Size, byte[]? Sha256)
    {
        public static UploadedPart Fail(IResult failure) => new(failure, null, null, null, 0, null);
    }
}

/// <summary>The answer of a stored upload.</summary>
internal sealed record FileUploadResponse(Guid Id, string Name, string MediaType, long Size);
