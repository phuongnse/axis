using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The in-app notification routes on the StepApp fixture: who sees a notification, what the list renders, and the
/// read state of each user. Notifications are inserted by SQL, as a notify step stores them. Every test of tenant a
/// starts by emptying the notification tables, so the counts are exact. Tenant b is used only by the test that
/// activates a release without the role finance.
/// </summary>
public sealed class NotificationEndpointTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private const string Notifications = "/api/apps/StepApp/notifications";
    private const string TenantNotices = "/api/notifications";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_addressed_user_and_a_holder_of_the_addressed_role_each_list_the_notification_once()
    {
        await ClearAsync();
        var id = await InsertAsync(TenantA, ("user", "maria"), ("role", "finance"));
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var otto = fixture.CreateClient().SignedInAs("otto");

        foreach (var client in new[] { maria, fiona })
        {
            using var list = await GetJsonAsync(client, Notifications);
            var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(id, item.GetProperty("id").GetGuid());
            Assert.Equal(fixture.ReleaseId(TenantA), item.GetProperty("releaseId").GetGuid());
            Assert.Equal("Request T", item.GetProperty("subject").GetString());
            Assert.Equal("Amount 500.", item.GetProperty("body").GetString());
            Assert.False(item.GetProperty("read").GetBoolean());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("open").ValueKind);
            Assert.Equal(1, list.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, list.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using var none = await GetJsonAsync(otto, Notifications);
        Assert.Empty(none.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, none.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, none.RootElement.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task A_tenant_notice_with_no_application_is_not_listed_by_the_application()
    {
        await ClearAsync();
        var id = await InsertAsync(TenantA, ("user", "maria"));
        // Addressed to the role finance, which maria holds, so that only the missing application keeps her list clear of it.
        await fixture.ExecuteAsync(
            TenantA,
            """
            INSERT INTO axis.notifications
                (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", value_types, created_at)
            VALUES (@id, NULL, NULL, 'role', 'finance', 'notices.emailsHeld.subject', 'notices.emailsHeld.body',
                    '{"limit":2}'::jsonb, '{"limit":"integer"}'::jsonb, now())
            """,
            ("id", Guid.NewGuid()));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var list = await GetJsonAsync(maria, Notifications);

        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal(1, list.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, list.RootElement.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Values_show_in_the_profile_locale_and_a_date_time_in_the_time_zone_of_the_stored_release()
    {
        await ClearAsync();
        var releaseId = await fixture.ActivateEditedAsync(TenantA, (path, content) =>
            path == "application.json"
                ? content.Replace("\"formatVersion\": 1", "\"formatVersion\": 1, \"timeZone\": \"Asia/Ho_Chi_Minh\"", StringComparison.Ordinal)
                : content);
        await fixture.InsertProfileAsync(TenantA, "maria", "Maria", "vi");
        await fixture.InsertProfileAsync(TenantA, "fiona", "Fiona", "en");
        var id = Guid.NewGuid();
        foreach (var user in new[] { "maria", "fiona" })
        {
            await fixture.ExecuteAsync(
                TenantA,
                """
                INSERT INTO axis.notifications
                    (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", value_types, created_at)
                VALUES (@id, @application, @release, 'user', @user, 'request.notice.subject', 'request.notice.body',
                        @values::jsonb, @types::jsonb, now())
                """,
                ("id", id),
                ("application", fixture.Model.Manifest.Id),
                ("release", releaseId),
                ("user", user),
                ("values", """{"title":"2026-12-31T18:00:00.000000Z","amount":1234.5}"""),
                ("types", """{"title":"date-time","amount":"decimal"}"""));
        }

        foreach (var (user, subject, body) in new[]
        {
            ("maria", "Request 01/01/2027 01:00", "Amount 1.234,5."),
            ("fiona", "Request Jan 1, 2027, 1:00 AM", "Amount 1,234.5."),
        })
        {
            using var client = fixture.CreateClient().SignedInAs(user);
            using var list = await GetJsonAsync(client, Notifications);
            var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal((subject, body), (item.GetProperty("subject").GetString(), item.GetProperty("body").GetString()));
        }
    }

    [Fact]
    public async Task A_notification_shows_the_page_it_opens_and_a_missing_text_key_shows_as_the_key()
    {
        await ClearAsync();
        await fixture.ExecuteAsync(
            TenantA,
            """
            INSERT INTO axis.notifications
                (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", open_page, open_parameters, created_at)
            VALUES (@id, @application, @release, 'user', 'maria', 'no.such.key', 'request.notice.body',
                    '{"amount":null,"title":"T"}'::jsonb, 'RequestDetail', '{"request":"abc"}'::jsonb, now())
            """,
            ("id", Guid.NewGuid()),
            ("application", fixture.Model.Manifest.Id),
            ("release", fixture.ReleaseId(TenantA)));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using var list = await GetJsonAsync(maria, Notifications);

        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("no.such.key", item.GetProperty("subject").GetString());
        Assert.Equal("Amount .", item.GetProperty("body").GetString());
        var open = item.GetProperty("open");
        Assert.Equal("RequestDetail", open.GetProperty("page").GetString());
        Assert.Equal("abc", open.GetProperty("parameters").GetProperty("request").GetString());
    }

    [Fact]
    public async Task A_role_notification_stops_showing_when_the_active_release_no_longer_declares_the_role()
    {
        // Tenant b is used only here, so the other tests keep their release.
        var id = await InsertAsync(TenantB, ("user", "maria"), ("role", "finance"));
        await fixture.ActivateEditedAsync(TenantB, (path, content) =>
            path == Path.Combine("roles", "finance.json")
                ? null
                : content.Replace("\"finance\"", "\"reviewer\"", StringComparison.Ordinal));
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using (var list = await GetJsonAsync(fiona, Notifications, TenantB))
        {
            Assert.Empty(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(0, list.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using var read = await PostAsync(fiona, $"{Notifications}/{id:D}/read", TenantB);
        using var missing = await RecordApiFixture.ReadProblemAsync(read, HttpStatusCode.NotFound);
        Assert.Equal("No notification exists with this id.", missing.RootElement.GetProperty("title").GetString());

        // The user row stays, and its texts come from the release the notification was stored with.
        using var own = await GetJsonAsync(maria, Notifications, TenantB);
        Assert.Equal(id, Assert.Single(own.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Marking_notifications_read_lowers_the_unread_count_only_for_the_caller()
    {
        await ClearAsync();
        var older = await InsertAsync(TenantA, DateTimeOffset.UtcNow.AddMinutes(-5), ("role", "finance"));
        var newer = await InsertAsync(TenantA, DateTimeOffset.UtcNow.AddMinutes(-1), ("role", "finance"));
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var fiona = fixture.CreateClient().SignedInAs("fiona");
        using var otto = fixture.CreateClient().SignedInAs("otto");

        using (var list = await GetJsonAsync(maria, Notifications))
        {
            Assert.Equal([newer, older], Ids(list));
        }

        using (var marked = await PostAsync(maria, $"{Notifications}/{newer:D}/read"))
        {
            Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
            Assert.Equal(0, marked.Content.Headers.ContentLength ?? 0);
        }

        using (var again = await PostAsync(maria, $"{Notifications}/{newer:D}/read"))
        {
            Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        }

        using (var list = await GetJsonAsync(maria, Notifications))
        {
            Assert.Equal(1, list.RootElement.GetProperty("unreadCount").GetInt32());
            Assert.Equal(2, list.RootElement.GetProperty("totalCount").GetInt32());
            Assert.True(list.RootElement.GetProperty("items")[0].GetProperty("read").GetBoolean());
        }

        using (var unread = await GetJsonAsync(maria, $"{Notifications}?unread=true"))
        {
            Assert.Equal([older], Ids(unread));
            Assert.Equal(1, unread.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, unread.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using (var list = await GetJsonAsync(fiona, Notifications))
        {
            Assert.Equal(2, list.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using (var all = await PostAsync(fiona, $"{Notifications}/read-all"))
        {
            Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        }

        using (var list = await GetJsonAsync(fiona, Notifications))
        {
            Assert.Equal(0, list.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using (var list = await GetJsonAsync(maria, Notifications))
        {
            Assert.Equal(1, list.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using var denied = await PostAsync(otto, $"{Notifications}/{older:D}/read");
        using var problem = await RecordApiFixture.ReadProblemAsync(denied, HttpStatusCode.NotFound);
        Assert.Equal("No notification exists with this id.", problem.RootElement.GetProperty("title").GetString());

        using var otherAll = await PostAsync(otto, $"{Notifications}/read-all");
        Assert.Equal(HttpStatusCode.NoContent, otherAll.StatusCode);
        Assert.Equal(3L, await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.notification_reads"));
        Assert.Equal(
            0L,
            await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.notification_reads WHERE user_id = 'otto'"));
    }

    [Fact]
    public async Task A_notification_that_is_not_an_id_or_does_not_exist_answers_404()
    {
        await ClearAsync();
        using var maria = fixture.CreateClient().SignedInAs("maria");

        foreach (var id in new[] { "not-an-id", Guid.NewGuid().ToString("D") })
        {
            using var response = await PostAsync(maria, $"{Notifications}/{id}/read");
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.NotFound);
            Assert.Equal("No notification exists with this id.", problem.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task The_list_pages_and_names_every_bad_query_parameter()
    {
        await ClearAsync();
        var first = await InsertAsync(TenantA, DateTimeOffset.UtcNow.AddMinutes(-3), ("user", "maria"));
        var second = await InsertAsync(TenantA, DateTimeOffset.UtcNow.AddMinutes(-2), ("user", "maria"));
        var third = await InsertAsync(TenantA, DateTimeOffset.UtcNow.AddMinutes(-1), ("user", "maria"));
        using var maria = fixture.CreateClient().SignedInAs("maria");

        using (var page = await GetJsonAsync(maria, $"{Notifications}?page=2&pageSize=2"))
        {
            Assert.Equal([first], Ids(page));
            Assert.Equal(3, page.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, page.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(2, page.RootElement.GetProperty("pageSize").GetInt32());
        }

        using (var page = await GetJsonAsync(maria, $"{Notifications}?pageSize=2"))
        {
            Assert.Equal([third, second], Ids(page));
        }

        using var bad = await SendAsync(maria, $"{Notifications}?page=0&pageSize=x&unread=maybe");
        using var problem = await RecordApiFixture.ReadProblemAsync(bad, HttpStatusCode.BadRequest);
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("page", out _));
        Assert.True(errors.TryGetProperty("pageSize", out _));
        Assert.True(errors.TryGetProperty("unread", out _));
    }

    [Fact]
    public async Task The_routes_need_a_session_and_the_posts_need_the_csrf_header()
    {
        await ClearAsync();
        var id = await InsertAsync(TenantA, ("user", "maria"));
        using var anonymous = fixture.CreateClient();

        using (var list = await SendAsync(anonymous, Notifications))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(list, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
        }

        foreach (var path in new[] { $"{Notifications}/{id:D}/read", $"{Notifications}/read-all" })
        {
            using var response = await PostAsync(anonymous, path);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
        }

        using var maria = fixture.CreateClient().SignedInAs("maria");
        foreach (var path in new[] { $"{Notifications}/{id:D}/read", $"{Notifications}/read-all" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
            request.Headers.Host = Host(TenantA);
            using var response = await maria.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("The request must carry the X-Axis-Csrf header.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(0L, await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.notification_reads"));
    }

    [Fact]
    public async Task An_administrator_lists_a_tenant_notice_in_the_profile_locale_and_marks_it_read()
    {
        await ClearAsync();
        await fixture.InsertProfileAsync(TenantA, "maria", "Maria", "vi");
        var id = await InsertTenantNoticeAsync();
        await using var factory = AdministratorFactory();
        using var maria = factory.CreateClient().SignedInAs("maria");

        using (var list = await GetJsonAsync(maria, TenantNotices))
        {
            var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(id, item.GetProperty("id").GetGuid());
            Assert.Equal("Email được giữ đến ngày mai", item.GetProperty("subject").GetString());
            Assert.Equal(
                "Tenant này đã gửi đủ 2 email trong hôm nay. Các email còn lại sẽ chờ đến 00:00 UTC.",
                item.GetProperty("body").GetString());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("releaseId").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("open").ValueKind);
            Assert.False(item.GetProperty("read").GetBoolean());
            Assert.Equal(1, list.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(1, list.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using (var marked = await PostAsync(maria, $"{TenantNotices}/{id:D}/read"))
        {
            Assert.Equal(HttpStatusCode.NoContent, marked.StatusCode);
        }

        using (var after = await GetJsonAsync(maria, TenantNotices))
        {
            Assert.True(Assert.Single(after.RootElement.GetProperty("items").EnumerateArray()).GetProperty("read").GetBoolean());
            Assert.Equal(0, after.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using var inApplication = await GetJsonAsync(maria, Notifications);
        Assert.Empty(inApplication.RootElement.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_tenant_notice_shows_in_english_when_the_profile_locale_has_no_platform_texts()
    {
        await ClearAsync();
        await fixture.InsertProfileAsync(TenantA, "fiona", "Fiona", "fr");
        await InsertTenantNoticeAsync();
        await using var factory = AdministratorFactory();
        using var fiona = factory.CreateClient().SignedInAs("fiona");

        using var list = await GetJsonAsync(fiona, TenantNotices);

        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Emails are held until tomorrow", item.GetProperty("subject").GetString());
        Assert.Equal("This tenant has sent its 2 emails for today. The other emails wait until 00:00 UTC.", item.GetProperty("body").GetString());
    }

    [Fact]
    public async Task A_user_without_the_administrator_role_lists_no_tenant_notice_and_cannot_mark_one_read()
    {
        await ClearAsync();
        var id = await InsertTenantNoticeAsync();
        await using var factory = AdministratorFactory();
        using var otto = factory.CreateClient().SignedInAs("otto");

        using (var list = await GetJsonAsync(otto, TenantNotices))
        {
            Assert.Empty(list.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(0, list.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(0, list.RootElement.GetProperty("unreadCount").GetInt32());
        }

        using (var denied = await PostAsync(otto, $"{TenantNotices}/{id:D}/read"))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(denied, HttpStatusCode.NotFound);
            Assert.Equal("No notification exists with this id.", problem.RootElement.GetProperty("title").GetString());
        }

        using (var all = await PostAsync(otto, $"{TenantNotices}/read-all"))
        {
            Assert.Equal(HttpStatusCode.NoContent, all.StatusCode);
        }

        Assert.Equal(0L, await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.notification_reads WHERE user_id = 'otto'"));

        // Without the setting nobody is an administrator, so maria sees none either.
        using var maria = fixture.CreateClient().SignedInAs("maria");
        using var none = await GetJsonAsync(maria, TenantNotices);
        Assert.Empty(none.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, none.RootElement.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task The_tenant_notice_routes_need_a_session_and_the_posts_need_the_csrf_header()
    {
        await ClearAsync();
        var id = await InsertTenantNoticeAsync();
        await using var factory = AdministratorFactory();
        using var anonymous = factory.CreateClient();

        using (var list = await SendAsync(anonymous, TenantNotices))
        {
            using var problem = await RecordApiFixture.ReadProblemAsync(list, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
        }

        foreach (var path in new[] { $"{TenantNotices}/{id:D}/read", $"{TenantNotices}/read-all" })
        {
            using var response = await PostAsync(anonymous, path);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Unauthorized);
            Assert.Equal("Sign in to read or write this data.", problem.RootElement.GetProperty("title").GetString());
        }

        using var maria = factory.CreateClient().SignedInAs("maria");
        foreach (var path in new[] { $"{TenantNotices}/{id:D}/read", $"{TenantNotices}/read-all" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
            request.Headers.Host = Host(TenantA);
            using var response = await maria.SendAsync(request, CancellationToken);
            using var problem = await RecordApiFixture.ReadProblemAsync(response, HttpStatusCode.Forbidden);
            Assert.Equal("The request must carry the X-Axis-Csrf header.", problem.RootElement.GetProperty("title").GetString());
        }

        Assert.Equal(0L, await fixture.ScalarAsync(TenantA, "SELECT count(*) FROM axis.notification_reads"));
    }

    private WebApplicationFactory<Program> AdministratorFactory() =>
        fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting("Users:AdministratorRole", "finance"));

    // The notice the worker stores when the tenant has used its emails of the day, for the administrators.
    private async Task<Guid> InsertTenantNoticeAsync()
    {
        var id = Guid.NewGuid();
        await fixture.ExecuteAsync(
            TenantA,
            """
            INSERT INTO axis.notifications
                (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", value_types, created_at)
            VALUES (@id, NULL, NULL, 'platformRole', 'administrator', 'notices.emailsHeld.subject', 'notices.emailsHeld.body',
                    '{"limit":2}'::jsonb, '{"limit":"integer"}'::jsonb, now())
            """,
            ("id", id));
        return id;
    }

    private async Task ClearAsync() =>
        await fixture.ExecuteAsync(TenantA, "DELETE FROM axis.notification_reads; DELETE FROM axis.notifications;");

    private Task<Guid> InsertAsync(string tenant, params (string Kind, string Recipient)[] recipients) =>
        InsertAsync(tenant, DateTimeOffset.UtcNow, recipients);

    // One notification of the StepApp texts "request.notice.*", with one row for each recipient.
    private async Task<Guid> InsertAsync(string tenant, DateTimeOffset createdAt, params (string Kind, string Recipient)[] recipients)
    {
        var id = Guid.NewGuid();
        foreach (var (kind, recipient) in recipients)
        {
            await fixture.ExecuteAsync(
                tenant,
                """
                INSERT INTO axis.notifications
                    (id, application_id, release_id, recipient_kind, recipient, subject_key, body_key, "values", created_at)
                VALUES (@id, @application, @release, @kind, @recipient, 'request.notice.subject', 'request.notice.body',
                        @values::jsonb, @createdAt)
                """,
                ("id", id),
                ("application", fixture.Model.Manifest.Id),
                ("release", fixture.ReleaseId(tenant)),
                ("kind", kind),
                ("recipient", recipient),
                ("values", """{"title":"T","amount":500}"""),
                ("createdAt", createdAt));
        }

        return id;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string tenant = TenantA)
    {
        using var request = RecordApiFixture.Request(path, Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string tenant = TenantA)
    {
        using var request = RecordApiFixture.Request(HttpMethod.Post, path, Host(tenant));
        return await client.SendAsync(request, CancellationToken);
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path, string tenant = TenantA)
    {
        using var response = await SendAsync(client, path, tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    private static List<Guid> Ids(JsonDocument list) =>
        [.. list.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
}
