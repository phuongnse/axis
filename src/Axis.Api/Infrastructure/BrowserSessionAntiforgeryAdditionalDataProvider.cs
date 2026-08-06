using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;

namespace Axis.Api.Infrastructure;

internal sealed class BrowserSessionAntiforgeryAdditionalDataProvider
    : IAntiforgeryAdditionalDataProvider
{
    private const string AnonymousSession = "anonymous";

    public string GetAdditionalData(HttpContext context) =>
        context.User.FindFirst(BrowserSessionCorrelation.ClaimType)?.Value
        ?? AnonymousSession;

    public bool ValidateAdditionalData(HttpContext context, string additionalData)
    {
        byte[] expected = Encoding.UTF8.GetBytes(GetAdditionalData(context));
        byte[] actual = Encoding.UTF8.GetBytes(additionalData);
        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
