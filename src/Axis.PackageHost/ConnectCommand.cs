using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Configuration.Packages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Axis.PackageHost;

/// <summary>
/// The <c>connect</c> command: runs the installed packages of <c>Extensions:Path</c> as a customer-hosted runner, see
/// docs/reference/package-runners.md#customer-hosted-runners. It signs in with the runner's credential, registers the manifest of every
/// installed version and then takes calls, one at a time, under a lease. Each call runs in the runner of the manifest's language, which
/// the command starts with no environment variables and keeps, one for each version. The <c>run</c> carries only the <c>url</c> and
/// <c>secret</c> bindings that the operation declares, read from <c>Connect:Urls:&lt;binding&gt;</c> and <c>Connect:Secrets:&lt;binding&gt;</c>.
/// The exit code is 0 when it is cancelled, 1 when the tenant or the identity provider refuses the runner, and 2 when a setting is missing
/// or malformed, with one line on the error output that names the setting and never a value. A runner that is still running when its
/// lease ends, or that exits, is killed and no result is posted. The log holds the package, the version and the lease, and never an
/// input, an output or a binding value.
/// </summary>
internal static partial class ConnectCommand
{
    public const string UrlSetting = "Connect:Url";
    public const string TokenUrlSetting = "Connect:TokenUrl";
    public const string ClientIdSetting = "Connect:ClientId";
    public const string ClientSecretSetting = "Connect:ClientSecret";
    public const string UrlsSetting = "Connect:Urls";
    public const string SecretsSetting = "Connect:Secrets";

    private static readonly TimeSpan _defaultIdleWait = TimeSpan.FromSeconds(2);

    // The token is dropped this long before it expires.
    private static readonly TimeSpan _tokenMargin = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, string> _noEnvironment = new Dictionary<string, string>();

    /// <summary>
    /// Runs the command until <paramref name="cancellationToken"/> is cancelled or the tenant refuses the runner. <paramref name="server"/> and
    /// <paramref name="identity"/> replace the handlers of the requests to the tenant and to the token endpoint, and <paramref name="idleWait"/>
    /// the 2 seconds that it waits after a take that gives no call.
    /// </summary>
    public static async Task<int> RunAsync(
        IConfiguration configuration,
        bool development,
        RunnerCommands commands,
        TextWriter error,
        ILogger logger,
        CancellationToken cancellationToken,
        HttpMessageHandler? server = null,
        HttpMessageHandler? identity = null,
        TimeSpan? idleWait = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(logger);

        var settings = ConnectSettings.Read(configuration, development, out var problem);
        if (settings is null)
        {
            await error.WriteLineAsync(problem);
            return PackageBuildCommand.SetupError;
        }

        using var identityClient = CreateClient(identity);
        using var serverClient = CreateClient(server);
        var connection = new Connection(settings, identityClient, serverClient);
        var runners = new Runners(settings, commands, logger);
        try
        {
            await RegisterAsync(settings, connection, cancellationToken);
            await TakeAsync(settings, connection, runners, logger, idleWait ?? _defaultIdleWait, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (ConnectExit exit)
        {
            await error.WriteLineAsync(exit.Message);
            return exit.Code;
        }
        finally
        {
            await runners.KillAllAsync();
        }
    }

    private static HttpClient CreateClient(HttpMessageHandler? handler) =>
        handler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            : new HttpClient(handler, disposeHandler: false);

    /// <summary>Posts the manifest of every installed version. A <c>409</c> means a manifest differs from the release's pin.</summary>
    private static async Task RegisterAsync(ConnectSettings settings, Connection connection, CancellationToken cancellationToken)
    {
        var packages = new JsonArray();
        foreach (var version in settings.Catalog.Versions)
        {
            var manifest = settings.Catalog.ReadManifestJson(version.Id, version.Version)
                ?? throw new ConnectExit(PackageBuildCommand.SetupError, $"The manifest of package '{version.Id}' {version.Version} under '{PackageCatalog.PathSetting}' cannot be read.");
            packages.Add(new JsonObject { ["manifest"] = JsonNode.Parse(manifest.GetRawText()) });
        }

        HttpResponseMessage response;
        try
        {
            response = await connection.PostAsync("api/package-runner/packages", new JsonObject { ["packages"] = packages }, cancellationToken);
        }
        catch (Exception exception) when (IsNetworkError(exception, cancellationToken))
        {
            throw new ConnectExit(PackageBuildCommand.Refused, $"The tenant at '{UrlSetting}' could not be reached to register the packages.");
        }

        using (response)
        {
            switch (response.StatusCode)
            {
                case HttpStatusCode.NoContent:
                    return;
                case HttpStatusCode.Conflict:
                    throw new ConnectExit(PackageBuildCommand.Refused, await ReadTitleAsync(response, cancellationToken));
                default:
                    throw new ConnectExit(PackageBuildCommand.Refused, $"The tenant refused to register the packages with status {(int)response.StatusCode}.");
            }
        }
    }

    /// <summary>Asks for a call and runs it, until the command is cancelled.</summary>
    private static async Task TakeAsync(
        ConnectSettings settings, Connection connection, Runners runners, ILogger logger, TimeSpan idleWait, CancellationToken cancellationToken)
    {
        var held = new JsonArray();
        foreach (var version in settings.Catalog.Versions)
        {
            held.Add(new JsonObject { ["id"] = version.Id, ["version"] = version.Version });
        }

        var body = new JsonObject { ["packages"] = held };
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TakenCall? call = null;
            try
            {
                using var response = await connection.PostAsync("api/package-runner/calls/take", body, cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    call = await response.Content.ReadFromJsonAsync<TakenCall>(_json, cancellationToken);
                }
                else if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    LogTakeRefused(logger, (int)response.StatusCode);
                }
            }
            catch (Exception exception) when (IsNetworkError(exception, cancellationToken) || exception is JsonException)
            {
                LogTakeFailed(logger, exception.GetType().Name);
            }

            if (call is null)
            {
                await Task.Delay(idleWait, cancellationToken);
                continue;
            }

            await RunCallAsync(settings, connection, runners, logger, call, cancellationToken);
        }
    }

    private static async Task RunCallAsync(
        ConnectSettings settings, Connection connection, Runners runners, ILogger logger, TakenCall call, CancellationToken cancellationToken)
    {
        var package = settings.Catalog.Find(call.Package, call.Version);
        var operation = package?.Manifest.Operations.FirstOrDefault(
            candidate => string.Equals(candidate.Name, call.Operation, StringComparison.OrdinalIgnoreCase));
        if (package is null || operation is null)
        {
            LogCallNotInstalled(logger, call.Package, call.Version, call.LeaseId);
            return;
        }

        using var lease = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lease.CancelAfter(Max(call.LeaseUntil - DateTimeOffset.UtcNow, TimeSpan.Zero));
        var run = new RunMessage(operation.Name, call.OperationId, call.Input, settings.BindingsOf(operation));
        var answer = await runners.RunAsync(package.Manifest, run, call.LeaseId, lease.Token, cancellationToken);
        if (answer is null)
        {
            return;
        }

        var result = ResultOf(answer, package.Manifest.Id, operation.Name);
        try
        {
            using var response = await connection.PostAsync($"api/package-runner/leases/{call.LeaseId:D}/result", result, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                LogResultPosted(logger, call.Package, call.Version, call.LeaseId);
            }
            else
            {
                LogResultDropped(logger, call.Package, call.Version, call.LeaseId, (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (IsNetworkError(exception, cancellationToken))
        {
            LogResultNotPosted(logger, call.Package, call.Version, call.LeaseId, exception.GetType().Name);
        }
    }

    /// <summary>The body of the result that posts <paramref name="answer"/>, which <see cref="Runners.RunAsync"/> only gives when it answers a <c>run</c>.</summary>
    private static JsonObject ResultOf(RunnerMessage answer, string package, string operation) => answer switch
    {
        ResultMessage result => new JsonObject { ["outcome"] = "succeeded", ["output"] = result.Output.DeepClone() },
        RetryableMessage => new JsonObject { ["outcome"] = "retryable" },
        FailureMessage { Binding: { } binding } => new JsonObject
        {
            ["outcome"] = "failed",
            ["error"] = PackageOperationContext.BindingRefusedMessage(operation, package, binding),
        },
        FailureMessage failure => new JsonObject { ["outcome"] = "failed", ["error"] = failure.Error ?? nameof(FailureMessage) },
        _ => throw new InvalidOperationException("The answer is not a result, a retryable or a failure."),
    };

    private static async Task<string> ReadTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String
                && title.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // The answer has no problem body, so only the status is reported.
        }

        return "The tenant refused to register the packages with status 409.";
    }

    private static bool IsNetworkError(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A take failed: {Reason}. It asks again.")]
    private static partial void LogTakeFailed(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A take answered status {Status}. It asks again.")]
    private static partial void LogTakeRefused(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The call of lease {Lease} is for package {Package} {Version} and an operation that is not installed. It is left to end.")]
    private static partial void LogCallNotInstalled(ILogger logger, string package, string version, Guid lease);

    [LoggerMessage(Level = LogLevel.Information, Message = "Posted the result of package {Package} {Version}, lease {Lease}.")]
    private static partial void LogResultPosted(ILogger logger, string package, string version, Guid lease);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The result of package {Package} {Version}, lease {Lease}, answered status {Status} and is dropped.")]
    private static partial void LogResultDropped(ILogger logger, string package, string version, Guid lease, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The result of package {Package} {Version}, lease {Lease}, could not be posted: {Reason}.")]
    private static partial void LogResultNotPosted(ILogger logger, string package, string version, Guid lease, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started the runner of package {Package} {Version}.")]
    private static partial void LogRunnerStarted(ILogger logger, string package, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The runner of package {Package} {Version} could not start.")]
    private static partial void LogRunnerNotStarted(ILogger logger, string package, string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The runner of package {Package} {Version} gave no answer to lease {Lease}: {Reason}. It is killed and no result is posted.")]
    private static partial void LogRunnerKilled(ILogger logger, string package, string version, Guid lease, string reason);

    /// <summary>A call that a take gives, as <c>POST /api/package-runner/calls/take</c> answers it.</summary>
    private sealed record TakenCall(Guid LeaseId, string Package, string Version, string Operation, Guid OperationId, JsonObject Input, DateTimeOffset LeaseUntil);

    /// <summary>Stops the command with an exit code and a message that names a setting or a status, never a value.</summary>
    private sealed class ConnectExit(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }

    /// <summary>The settings of the command, read once at start. A binding value is checked here, so no call is taken that is sure to fail.</summary>
    private sealed class ConnectSettings
    {
        private readonly Dictionary<string, string> _urls = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

        private ConnectSettings(Uri url, Uri tokenUrl, string clientId, string clientSecret, string root, PackageCatalog catalog)
        {
            Url = url;
            TokenUrl = tokenUrl;
            ClientId = clientId;
            ClientSecret = clientSecret;
            Root = root;
            Catalog = catalog;
        }

        public Uri Url { get; }

        public Uri TokenUrl { get; }

        public string ClientId { get; }

        public string ClientSecret { get; }

        public string Root { get; }

        public PackageCatalog Catalog { get; }

        /// <summary>Reads and checks the settings. Returns <see langword="null"/> and the line that names the first bad setting when one is bad.</summary>
        public static ConnectSettings? Read(IConfiguration configuration, bool development, out string problem)
        {
            problem = string.Empty;
            if (!TryReadAddress(configuration, UrlSetting, development, out var url, out problem)
                || !TryReadAddress(configuration, TokenUrlSetting, development, out var tokenUrl, out problem)
                || !TryReadText(configuration, ClientIdSetting, out var clientId, out problem)
                || !TryReadText(configuration, ClientSecretSetting, out var clientSecret, out problem))
            {
                return null;
            }

            var path = configuration[PackageCatalog.PathSetting];
            if (string.IsNullOrEmpty(path) || !Directory.Exists(Path.GetFullPath(path)))
            {
                problem = $"Set '{PackageCatalog.PathSetting}' to an existing folder.";
                return null;
            }

            var root = Path.GetFullPath(path);
            PackageCatalog catalog;
            try
            {
                catalog = PackageCatalog.Load(root, PackageLoadMode.MetadataOnly);
            }
            catch (InvalidOperationException)
            {
                problem = $"The folder set by '{PackageCatalog.PathSetting}' cannot be listed.";
                return null;
            }

            if (catalog.Diagnostics.Count > 0)
            {
                problem = $"The packages under '{PackageCatalog.PathSetting}' do not load: "
                    + string.Join(" ", catalog.Diagnostics.Select(diagnostic => $"{diagnostic.Code} {diagnostic.File}: {diagnostic.Message}"));
                return null;
            }

            if (catalog.Packages.Count == 0)
            {
                problem = $"The folder set by '{PackageCatalog.PathSetting}' holds no package.";
                return null;
            }

            var settings = new ConnectSettings(url!, tokenUrl!, clientId, clientSecret, root, catalog);
            foreach (var operation in catalog.Packages.SelectMany(package => package.Manifest.Operations))
            {
                foreach (var name in operation.Capabilities.Urls)
                {
                    if (!TryReadAddress(configuration, $"{UrlsSetting}:{name}", development, out var address, out problem))
                    {
                        return null;
                    }

                    settings._urls[name] = address!.AbsoluteUri;
                }

                foreach (var name in operation.Capabilities.Secrets)
                {
                    if (!TryReadText(configuration, $"{SecretsSetting}:{name}", out var secret, out problem))
                    {
                        return null;
                    }

                    settings._secrets[name] = secret;
                }
            }

            return settings;
        }

        /// <summary>The binding values of one <c>run</c>: only the bindings that <paramref name="operation"/> declares.</summary>
        public RunBindings BindingsOf(PackageOperation operation) => new(
            operation.Capabilities.Urls.ToDictionary(name => name, name => _urls[name]),
            operation.Capabilities.Secrets.ToDictionary(name => name, name => _secrets[name]));

        private static bool TryReadAddress(IConfiguration configuration, string setting, bool development, out Uri? address, out string problem)
        {
            address = null;
            problem = string.Empty;
            var value = configuration[setting];
            if (string.IsNullOrEmpty(value))
            {
                problem = $"Set '{setting}'.";
                return false;
            }

            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                problem = $"'{setting}' is not an absolute address.";
                return false;
            }

            if (uri.Scheme != Uri.UriSchemeHttps && !(development && uri.Scheme == Uri.UriSchemeHttp))
            {
                problem = $"'{setting}' is not an https address. Only Development allows http.";
                return false;
            }

            address = uri;
            return true;
        }

        private static bool TryReadText(IConfiguration configuration, string setting, out string value, out string problem)
        {
            value = configuration[setting] ?? string.Empty;
            problem = value.Length == 0 ? $"Set '{setting}'." : string.Empty;
            return value.Length > 0;
        }
    }

    /// <summary>
    /// The requests to the tenant and to the token endpoint. A token is kept until a minute before it expires. A <c>401</c> drops it, gets a
    /// fresh one and sends the request once more, and a second <c>401</c> stops the command with exit code 1.
    /// </summary>
    private sealed class Connection(ConnectSettings settings, HttpClient identity, HttpClient server)
    {
        private readonly Uri _base = settings.Url.AbsoluteUri.EndsWith('/') ? settings.Url : new Uri(settings.Url.AbsoluteUri + "/");
        private string? _token;
        private DateTimeOffset _tokenUntil;

        /// <summary>Posts <paramref name="body"/> as JSON to <paramref name="path"/> of the tenant, with the bearer token.</summary>
        /// <exception cref="ConnectExit">The identity provider refuses the credential, or the tenant answers <c>401</c> twice.</exception>
        public async Task<HttpResponseMessage> PostAsync(string path, JsonObject body, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                var token = await GetTokenAsync(cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_base, path))
                {
                    Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await server.SendAsync(request, cancellationToken);
                if (response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    return response;
                }

                response.Dispose();
                _token = null;
                if (attempt == 2)
                {
                    throw new ConnectExit(PackageBuildCommand.Refused, $"The tenant at '{UrlSetting}' refused the token twice. The runner may be revoked.");
                }
            }
        }

        private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenUntil)
            {
                return _token;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, settings.TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = settings.ClientId,
                    ["client_secret"] = settings.ClientSecret,
                }),
            };
            using var response = await identity.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ConnectExit(
                    PackageBuildCommand.Refused,
                    $"The token endpoint set by '{TokenUrlSetting}' refused the credential of '{ClientIdSetting}' and '{ClientSecretSetting}' with status {(int)response.StatusCode}.");
            }

            try
            {
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("access_token", out var token)
                    && token.ValueKind == JsonValueKind.String
                    && token.GetString() is { Length: > 0 } text
                    && root.TryGetProperty("expires_in", out var expires)
                    && expires.TryGetInt64(out var seconds))
                {
                    _token = text;
                    _tokenUntil = DateTimeOffset.UtcNow.AddSeconds(seconds) - _tokenMargin;
                    return text;
                }
            }
            catch (JsonException)
            {
                // Falls through to the exit below.
            }

            throw new ConnectExit(PackageBuildCommand.Refused, $"The token endpoint set by '{TokenUrlSetting}' answered with no access token.");
        }
    }

    /// <summary>The runners of the command, one for each installed version that has taken a call.</summary>
    private sealed class Runners(ConnectSettings settings, RunnerCommands commands, ILogger logger)
    {
        private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

        private readonly Dictionary<(string Package, string Version), RunnerSession> _sessions = [];

        /// <summary>
        /// Sends <paramref name="run"/> to the runner of the manifest's version and reads its one answer, which ends at <paramref name="lease"/>.
        /// Returns <see langword="null"/> when there is no answer: the runner does not start, exits, answers something else or is still
        /// running when the lease ends. The runner is then killed and dropped.
        /// </summary>
        /// <exception cref="OperationCanceledException"><paramref name="stopping"/> was cancelled.</exception>
        public async Task<RunnerMessage?> RunAsync(
            PackageManifest manifest, RunMessage run, Guid leaseId, CancellationToken lease, CancellationToken stopping)
        {
            var key = (manifest.Id, manifest.Version);
            if (_sessions.TryGetValue(key, out var existing) && existing.Process.HasExited)
            {
                _sessions.Remove(key);
                existing.Dispose();
            }

            if (!_sessions.TryGetValue(key, out var session))
            {
                var process = RunnerProcess.TryStart(commands, settings.Root, manifest.Id, manifest.Version, manifest.Language, _noEnvironment);
                if (process is null)
                {
                    LogRunnerNotStarted(logger, manifest.Id, manifest.Version);
                    return null;
                }

                session = new RunnerSession(process);
                _sessions[key] = session;
                LogRunnerStarted(logger, manifest.Id, manifest.Version);
            }

            try
            {
                if (!session.Ready)
                {
                    if (await session.ReadAsync(lease) is not ReadyMessage)
                    {
                        return await DropAsync(key, session, leaseId, "it did not say ready");
                    }

                    session.Ready = true;
                }

                await session.WriteAsync(RunnerProtocol.Write(run), lease);
                var answer = await session.ReadAsync(lease);
                if (answer is ResultMessage or RetryableMessage or FailureMessage)
                {
                    return answer;
                }

                return await DropAsync(key, session, leaseId, answer is null ? "it exited" : "it sent a message that answers no run");
            }
            catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
            {
                return await DropAsync(key, session, leaseId, "the lease ended");
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                return await DropAsync(key, session, leaseId, "it broke the protocol");
            }
        }

        public async Task KillAllAsync()
        {
            foreach (var session in _sessions.Values)
            {
                await session.KillAsync();
                session.Dispose();
            }

            _sessions.Clear();
        }

        private async Task<RunnerMessage?> DropAsync((string Package, string Version) key, RunnerSession session, Guid leaseId, string reason)
        {
            LogRunnerKilled(logger, key.Package, key.Version, leaseId, reason);
            _sessions.Remove(key);
            await session.KillAsync();
            session.Dispose();
            return null;
        }

        /// <summary>A started runner with its standard input and output as UTF-8 lines.</summary>
        private sealed class RunnerSession(Process process) : IDisposable
        {
            private readonly StreamWriter _input = new(process.StandardInput.BaseStream, _utf8, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            private readonly StreamReader _output = new(process.StandardOutput.BaseStream, _utf8, leaveOpen: true);

            public Process Process { get; } = process;

            /// <summary>True once the runner has written its <c>ready</c> line.</summary>
            public bool Ready { get; set; }

            public Task WriteAsync(string line, CancellationToken cancellationToken) =>
                _input.WriteLineAsync(line.AsMemory(), cancellationToken);

            /// <summary>The next message, or <see langword="null"/> when the runner's output has ended.</summary>
            /// <exception cref="JsonException">The line is not a message of the protocol.</exception>
            public async Task<RunnerMessage?> ReadAsync(CancellationToken cancellationToken) =>
                await _output.ReadLineAsync(cancellationToken) is { } line ? RunnerProtocol.Read(line) : null;

            public Task KillAsync() => RunnerProcess.KillAsync(Process);

            public void Dispose()
            {
                _input.Dispose();
                _output.Dispose();
                Process.Dispose();
            }
        }
    }
}
