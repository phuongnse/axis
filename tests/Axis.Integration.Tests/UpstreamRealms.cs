using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Axis.Integration.Tests;

/// <summary>
/// Realms in the test Keycloak that play the company's issuer for a tenant realm that brokers to it, and the browser that signs a
/// person in through them. The browser follows redirects and submits the self-submitting SAML forms. It deletes every realm it made, and every realm named by <see cref="NewRealmName"/>, when it is disposed.
/// </summary>
public sealed partial class UpstreamRealms : IDisposable
{
    public const string Email = "pat@example.test";
    public const string ClientId = "tenant-broker";
    public const string BrokerSecret = "broker-secret-1";

    private const string UpstreamPassword = "upstream-password-1";

    private static readonly string[] _anyRedirect = ["*"];

    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
    private readonly List<string> _realms = [];
    private readonly Uri _baseAddress;
    private string _token = null!;

    private UpstreamRealms(Uri baseAddress) => _baseAddress = baseAddress;

    /// <summary>Connects to the admin API of the Keycloak at <paramref name="baseAddress"/>.</summary>
    public static async Task<UpstreamRealms> CreateAsync(Uri baseAddress)
    {
        var realms = new UpstreamRealms(baseAddress);
        realms._token = await realms.MasterTokenAsync();
        return realms;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Deletes the realms that were made and clears the list. A realm that is gone already is not an error.</summary>
    public async Task DeleteRealmsAsync()
    {
        foreach (var realm in _realms)
        {
            using var response = await SendAsync(HttpMethod.Delete, $"admin/realms/{realm}");
            Assert.True(response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound);
        }

        _realms.Clear();
    }

    /// <summary>A new realm name that <see cref="DeleteRealmsAsync"/> deletes, for a realm that the caller makes.</summary>
    public string NewRealmName()
    {
        var realm = $"t{Guid.NewGuid():N}"[..16];
        _realms.Add(realm);
        return realm;
    }

    /// <summary>
    /// Makes a realm that plays the company's issuer. Its frontend address is the container's mapped address, so the
    /// <c>iss</c> of a token that the tenant realm fetches from inside the container matches the issuer that the sync stored.
    /// A <paramref name="groups"/> value becomes the <c>groups</c> claim of every token, whoever signs in.
    /// </summary>
    public async Task<string> NewUpstreamRealmAsync(string? groups = null)
    {
        var realm = NewRealmName();
        using var created = await SendAsync(HttpMethod.Post, "admin/realms", new
        {
            realm,
            enabled = true,
            attributes = new Dictionary<string, string> { ["frontendUrl"] = _baseAddress.AbsoluteUri.TrimEnd('/') },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var client = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients", new
        {
            clientId = ClientId,
            enabled = true,
            publicClient = false,
            secret = BrokerSecret,
            standardFlowEnabled = true,
            redirectUris = _anyRedirect,
            protocolMappers = groups is null ? Array.Empty<object>() : [GroupsClaim(groups)],
        });
        Assert.Equal(HttpStatusCode.Created, client.StatusCode);
        return realm;
    }

    /// <summary>
    /// Makes a realm that plays a company's SAML issuer, with the same frontend address as <see cref="NewUpstreamRealmAsync"/>. Its one
    /// client is the tenant realm's service provider, <paramref name="entityId"/>, which accepts unsigned requests and posts its response
    /// to <paramref name="assertionConsumerUrl"/>. It sends the user's first name as <c>name</c> and the user's email as <c>email</c>,
    /// and a <paramref name="groups"/> value as the attribute <c>groups</c>.
    /// </summary>
    public async Task<string> NewUpstreamSamlRealmAsync(string entityId, string assertionConsumerUrl, string? groups = null)
    {
        var realm = NewRealmName();
        using var created = await SendAsync(HttpMethod.Post, "admin/realms", new
        {
            realm,
            enabled = true,
            attributes = new Dictionary<string, string> { ["frontendUrl"] = _baseAddress.AbsoluteUri.TrimEnd('/') },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var client = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/clients", new
        {
            protocol = "saml",
            clientId = entityId,
            enabled = true,
            redirectUris = _anyRedirect,
            attributes = new Dictionary<string, string>
            {
                ["saml.client.signature"] = "false",
                ["saml_assertion_consumer_url_post"] = assertionConsumerUrl,
                ["saml.force.post.binding"] = "true",
                ["saml_name_id_format"] = "persistent",
            },
            protocolMappers = groups is null
                ? new[] { SamlAttribute("name", "firstName"), SamlAttribute("email", "email") }
                : [SamlAttribute("name", "firstName"), SamlAttribute("email", "email"), SamlGroups(groups)],
        });
        Assert.Equal(HttpStatusCode.Created, client.StatusCode);
        return realm;
    }

    /// <summary>The address of the SAML metadata of the <paramref name="upstream"/> realm, which the tenant realm's provider imports.</summary>
    public Uri SamlMetadata(string upstream) => new(_baseAddress, $"realms/{upstream}/protocol/saml/descriptor");

    private static object GroupsClaim(string groups) => new
    {
        name = "groups",
        protocol = "openid-connect",
        protocolMapper = "oidc-hardcoded-claim-mapper",
        config = new Dictionary<string, string>
        {
            ["claim.name"] = "groups",
            ["claim.value"] = groups,
            ["jsonType.label"] = "String",
            ["id.token.claim"] = "true",
            ["access.token.claim"] = "true",
            ["userinfo.token.claim"] = "true",
        },
    };

    private static object SamlGroups(string groups) => new
    {
        name = "groups",
        protocol = "saml",
        protocolMapper = "saml-hardcode-attribute-mapper",
        config = new Dictionary<string, string>
        {
            ["attribute.name"] = "groups",
            ["attribute.value"] = groups,
            ["attribute.nameformat"] = "Basic",
        },
    };

    private static object SamlAttribute(string name, string property) => new
    {
        name,
        protocol = "saml",
        protocolMapper = "saml-user-property-mapper",
        config = new Dictionary<string, string>
        {
            ["user.attribute"] = property,
            ["attribute.name"] = name,
            ["attribute.nameformat"] = "Basic",
        },
    };

    /// <summary>Adds the person <c>pat</c> with a complete profile, so Keycloak's profile check asks for nothing. Returns the user's id, which is the subject.</summary>
    public async Task<string> AddUpstreamUserAsync(string realm)
    {
        using var created = await SendAsync(HttpMethod.Post, $"admin/realms/{realm}/users", new
        {
            username = "pat",
            email = Email,
            emailVerified = true,
            enabled = true,
            firstName = "Pat",
            lastName = "Example",
            credentials = new[] { new { type = "password", temporary = false, value = UpstreamPassword } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return created.Headers.Location!.Segments[^1];
    }

    /// <summary>
    /// The tenant realm runs inside the container, which cannot reach its own mapped port. It calls the upstream realm's
    /// back-channel endpoints on the container's own port instead, and the browser keeps using the mapped one.
    /// </summary>
    public async Task PointBackChannelAtTheContainerAsync(string tenant, string alias, string upstream)
    {
        var path = $"admin/realms/{tenant}/identity-provider/instances/{alias}";
        var provider = await GetJsonAsync(path);
        var representation = provider.Deserialize<Dictionary<string, JsonElement>>()!;
        var config = provider.GetProperty("config").Deserialize<Dictionary<string, string>>()!;
        foreach (var key in new[] { "tokenUrl", "jwksUrl", "userInfoUrl" })
        {
            config[key] = config[key].Replace(_baseAddress.AbsoluteUri, "http://localhost:8080/", StringComparison.Ordinal);
            Assert.Contains($"http://localhost:8080/realms/{upstream}/", config[key], StringComparison.Ordinal);
        }

        representation["config"] = JsonSerializer.SerializeToElement(config);
        using var updated = await SendAsync(HttpMethod.Put, path, representation);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
    }

    /// <summary>
    /// Plays a browser that starts at <paramref name="authorize"/>, an address of the tenant realm that sends the person to the
    /// provider, logs in at the <paramref name="upstream"/> realm and follows every redirect until Keycloak sends it to an address
    /// that starts with <paramref name="callbackPrefix"/>. Returns that address. A page on the way, such as one that offers to link an
    /// account, fails the test with its HTML.
    /// </summary>
    public async Task<Uri> LogInAsync(Uri authorize, string upstream, string callbackPrefix)
    {
        var jar = new List<(string Name, string Path, string Value)>();
        var (page, html) = await FollowAsync(jar, HttpMethod.Get, authorize, null, callbackPrefix);
        Assert.True(page is not null, $"The sign-in ended at the callback before the login at '{upstream}'.");
        Assert.True(page.AbsolutePath.StartsWith($"/realms/{upstream}/", StringComparison.Ordinal), html);
        var action = new Uri(WebUtility.HtmlDecode(FormActionPattern().Match(html).Groups["action"].Value));

        var (stopped, stoppedHtml) = await FollowAsync(jar, HttpMethod.Post, action, new Dictionary<string, string>
        {
            ["username"] = "pat",
            ["password"] = UpstreamPassword,
            ["credentialId"] = "",
        }, callbackPrefix);
        Assert.True(stopped is null, stoppedHtml);
        return new Uri(stoppedHtml);
    }

    public async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Sends the request and follows each redirect by hand. A page with a form that carries a hidden <c>SAMLRequest</c> or
    /// <c>SAMLResponse</c> is submitted as a browser does, as a POST of the form's hidden inputs. Returns the address and HTML of the first
    /// page that is neither, or <see langword="null"/> and the address when the redirects end at the callback.
    /// </summary>
    private async Task<(Uri? Page, string Html)> FollowAsync(
        List<(string Name, string Path, string Value)> jar,
        HttpMethod method,
        Uri address,
        Dictionary<string, string>? form,
        string callbackPrefix)
    {
        for (var hop = 0; hop < 20; hop++)
        {
            using var request = new HttpRequestMessage(method, address);
            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            var cookie = string.Join("; ", jar.Where(c => address.AbsolutePath.StartsWith(c.Path, StringComparison.Ordinal)).Select(c => $"{c.Name}={c.Value}"));
            if (cookie.Length > 0)
            {
                request.Headers.Add("Cookie", cookie);
            }

            using var response = await _http.SendAsync(request, TestContext.Current.CancellationToken);
            Remember(jar, response);
            if (response.StatusCode is not (HttpStatusCode.Found or HttpStatusCode.SeeOther))
            {
                var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                var samlForm = SamlForm(html);
                if (samlForm is null)
                {
                    return (address, $"{(int)response.StatusCode} {address}\n{html}");
                }

                method = HttpMethod.Post;
                address = new Uri(address, samlForm.Value.Action);
                form = samlForm.Value.Fields;
                continue;
            }

            var location = new Uri(address, response.Headers.Location!);
            if (location.AbsoluteUri.StartsWith(callbackPrefix, StringComparison.Ordinal))
            {
                return (null, location.AbsoluteUri);
            }

            method = HttpMethod.Get;
            form = null;
            address = location;
        }

        throw new InvalidOperationException("The sign-in redirected more than 20 times.");
    }

    /// <summary>The action and hidden inputs of the form that carries a <c>SAMLRequest</c> or a <c>SAMLResponse</c>, or <see langword="null"/> when the page has none.</summary>
    private static (string Action, Dictionary<string, string> Fields)? SamlForm(string html)
    {
        var fields = new Dictionary<string, string>();
        foreach (Match input in InputPattern().Matches(html))
        {
            var tag = input.Value;
            if (!HiddenPattern().IsMatch(tag))
            {
                continue;
            }

            var name = NamePattern().Match(tag);
            if (name.Success)
            {
                fields[WebUtility.HtmlDecode(name.Groups["v"].Value)] = WebUtility.HtmlDecode(ValuePattern().Match(tag).Groups["v"].Value);
            }
        }

        var action = FormActionPattern().Match(html);
        return action.Success && (fields.ContainsKey("SAMLRequest") || fields.ContainsKey("SAMLResponse"))
            ? (WebUtility.HtmlDecode(action.Groups["action"].Value), fields)
            : null;
    }

    /// <summary>
    /// Keeps each cookie by name and path. Keycloak sets <c>AUTH_SESSION_ID</c> and <c>KC_RESTART</c> for each realm it
    /// talks to, so one jar that keeps names alone would mix the tenant realm with the upstream one.
    /// </summary>
    private static void Remember(List<(string Name, string Path, string Value)> jar, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        foreach (var cookie in cookies)
        {
            var parts = cookie.Split(';', StringSplitOptions.TrimEntries);
            var separator = parts[0].IndexOf('=', StringComparison.Ordinal);
            var name = parts[0][..separator];
            var value = parts[0][(separator + 1)..];
            var path = parts.Skip(1).Where(part => part.StartsWith("Path=", StringComparison.OrdinalIgnoreCase)).Select(part => part[5..]).FirstOrDefault() ?? "/";
            var expired = parts.Any(part => part.Equals("Max-Age=0", StringComparison.OrdinalIgnoreCase));
            jar.RemoveAll(c => c.Name == name && c.Path == path);
            if (!expired && value.Length > 0)
            {
                jar.Add((name, path, value));
            }
        }
    }

    private async Task<string> MasterTokenAsync()
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = KeycloakFixture.AdminClientId,
            ["client_secret"] = KeycloakFixture.AdminClientSecret,
        });
        using var response = await _http.PostAsync(new Uri(_baseAddress, "realms/master/protocol/openid-connect/token"), content, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("access_token").GetString()!;
    }

    [GeneratedRegex("""<input\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex InputPattern();

    [GeneratedRegex("""\btype\s*=\s*["']hidden["']""", RegexOptions.IgnoreCase)]
    private static partial Regex HiddenPattern();

    [GeneratedRegex("""\bname\s*=\s*["'](?<v>[^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    [GeneratedRegex("""\bvalue\s*=\s*["'](?<v>[^"']*)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex ValuePattern();

    [GeneratedRegex("""<form\b[^>]*\baction="(?<action>[^"]+)""")]
    private static partial Regex FormActionPattern();
}
