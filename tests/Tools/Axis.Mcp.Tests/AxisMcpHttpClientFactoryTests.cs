using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Axis.Mcp.Configuration;

namespace Axis.Mcp.Tests;

public sealed class AxisMcpHttpClientFactoryTests
{
    [Fact]
    public void Create_WhenRootCertificateIsCertificateOnlyPem_LoadsIt()
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=Axis MCP test root",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddHours(1));

        string path = Path.Combine(
            Path.GetTempPath(),
            $"axis-mcp-root-{Guid.NewGuid():N}.pem");

        try
        {
            File.WriteAllText(path, certificate.ExportCertificatePem());

            using HttpClient client = AxisMcpHttpClientFactory.Create(
                AxisMcpOptions.Create(
                    new Uri("https://localhost:5281/"),
                    path));

            Assert.NotNull(client);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
