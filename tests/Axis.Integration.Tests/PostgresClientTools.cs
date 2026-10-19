using System.Diagnostics;

namespace Axis.Integration.Tests;

/// <summary>
/// <c>pg_dump</c> and <c>pg_restore</c> of the test PostgreSQL server, which is version 18. The CI runner ships older tools, and a
/// <c>pg_dump</c> refuses a newer server. So the tools run inside the container, and two wrapper scripts stand in for them on a
/// <c>PATH</c> that the admin tool is given. The wrapper of <c>pg_dump</c> passes the <c>PG*</c> variables of the admin tool on and
/// points the host and port at the server inside the container.
/// </summary>
internal sealed class PostgresClientTools : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "axis-pg-tools-" + Guid.NewGuid().ToString("N"));

    private PostgresClientTools(string containerId)
    {
        ContainerId = containerId;
        Directory.CreateDirectory(_folder);
        WriteScript(
            Path.Combine(_folder, "pg_dump"),
            $"""
            #!/bin/sh
            exec docker exec -e PGHOST=127.0.0.1 -e PGPORT=5432 -e PGDATABASE -e PGUSER -e PGPASSWORD {containerId} pg_dump "$@"
            """);
        // The dump arrives on standard input, so the container's exec needs -i.
        WriteScript(
            Path.Combine(_folder, "pg_restore"),
            $"""
            #!/bin/sh
            exec docker exec -i -e PGHOST=127.0.0.1 -e PGPORT=5432 -e PGDATABASE -e PGUSER -e PGPASSWORD {containerId} pg_restore "$@"
            """);
    }

    public string ContainerId { get; }

    /// <summary>The <c>PATH</c> setting for the admin tool: the wrapper folder first, then the real <c>PATH</c>.</summary>
    public string PathSetting => _folder + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

    /// <summary>Writes a shell script that only its owner can read, write and run. The tests run on Linux.</summary>
    public static void WriteScript(string path, string content)
    {
        File.WriteAllText(path, content.TrimEnd('\n') + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public static async Task<PostgresClientTools> CreateAsync() => new(await PostgreSqlServer.ContainerIdAsync());

    /// <summary>Runs <c>pg_restore</c> in the container as the superuser, with <paramref name="dump"/> on its standard input, and returns its standard output.</summary>
    public async Task<string> RestoreAsync(string dump, CancellationToken cancellationToken, params string[] arguments)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add("-i");
        info.ArgumentList.Add(ContainerId);
        info.ArgumentList.Add("pg_restore");
        info.ArgumentList.Add("-U");
        info.ArgumentList.Add("postgres");
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await using (var file = File.OpenRead(dump))
        {
            await file.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
        }

        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken);
        var text = await output;
        Assert.True(process.ExitCode == 0, $"pg_restore exited with code {process.ExitCode}: {await error}");
        return text;
    }

    /// <summary>Runs <c>pg_dump</c> in the container as the superuser over <paramref name="database"/> and writes the custom-format dump to <paramref name="file"/>.</summary>
    public async Task DumpAsync(string database, string file, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "exec", ContainerId, "pg_dump", "-U", "postgres", "--format=custom", "--no-owner", "--no-privileges", "-d", database })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await using (var output = File.Create(file))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        }

        await process.WaitForExitAsync(cancellationToken);
        Assert.True(process.ExitCode == 0, $"pg_dump exited with code {process.ExitCode}: {await error}");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
