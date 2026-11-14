using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Axis.PackageHost;

/// <summary>
/// Tells whether the host runs under gVisor, the sandbox that isolates an application's package, see
/// docs/reference/extension-packages.md#an-applications-packages. gVisor's kernel log begins with its <c>Starting gVisor</c> line,
/// which <c>dmesg</c> reads with <c>klogctl</c>. <paramref name="readKernelLog"/> is the seam that tests feed a log through. It returns the
/// log, or <see langword="null"/> when the read is refused or the system is not Linux.
/// </summary>
internal sealed partial class Sandbox(Func<string?> readKernelLog)
{
    // The commands of syslog(2): the size of the log buffer, and all of it without clearing it.
    private const int ActionReadAll = 3;
    private const int ActionSizeBuffer = 10;

    /// <summary>The sandbox of this machine, which reads the real kernel log.</summary>
    public static Sandbox ForThisMachine() => new(ReadThisKernelLog);

    /// <summary>
    /// Whether the kernel log begins with gVisor's <c>Starting gVisor</c> line. A log that is missing, refused or begins otherwise,
    /// as a Linux kernel's does, is not gVisor.
    /// </summary>
    public bool IsGvisor() => readKernelLog() is { } log && GvisorStart().IsMatch(log);

    private static string? ReadThisKernelLog()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        var size = NativeMethods.Klogctl(ActionSizeBuffer, [], 0);
        if (size <= 0)
        {
            return null;
        }

        var buffer = new byte[size];
        var read = NativeMethods.Klogctl(ActionReadAll, buffer, buffer.Length);
        return read < 0 ? null : Encoding.UTF8.GetString(buffer, 0, read);
    }

    // gVisor's raw syslog line is like "<6>[    0.000000] Starting gVisor...".
    [GeneratedRegex(@"\A(?:<\d+>)?(?:\[\s*[\d.]+\]\s*)?Starting gVisor")]
    private static partial Regex GvisorStart();

    private static partial class NativeMethods
    {
        [LibraryImport("libc", EntryPoint = "klogctl", SetLastError = true)]
        public static partial int Klogctl(int type, Span<byte> buffer, int length);
    }
}
