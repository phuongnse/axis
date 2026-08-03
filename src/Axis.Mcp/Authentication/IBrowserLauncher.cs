namespace Axis.Mcp.Authentication;

public interface IBrowserLauncher
{
    bool TryOpen(Uri uri);
}
