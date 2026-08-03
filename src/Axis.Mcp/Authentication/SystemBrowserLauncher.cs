using System.Diagnostics;

namespace Axis.Mcp.Authentication;

public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public bool TryOpen(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            ProcessStartInfo startInfo = CreateStartInfo(uri);

            startInfo.UseShellExecute = false;
            Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static ProcessStartInfo CreateStartInfo(Uri uri)
    {
        if (OperatingSystem.IsWindows() || IsWindowsSubsystemForLinux())
        {
            ProcessStartInfo startInfo = new("cmd.exe");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("start");
            startInfo.ArgumentList.Add(string.Empty);
            startInfo.ArgumentList.Add(uri.ToString());
            return startInfo;
        }

        ProcessStartInfo unixStartInfo = new(
            OperatingSystem.IsMacOS() ? "open" : "xdg-open");
        unixStartInfo.ArgumentList.Add(uri.ToString());
        return unixStartInfo;
    }

    private static bool IsWindowsSubsystemForLinux()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WSL_INTEROP")))
            return true;

        try
        {
            return File.ReadAllText("/proc/version")
                .Contains("microsoft", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
