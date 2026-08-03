namespace Axis.Mcp.Authentication;

public interface IAxisAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);

    void Invalidate();
}
