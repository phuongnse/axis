using Axis.Identity.Application.Services;

namespace Axis.Identity.Infrastructure.Services;

internal sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) =>
        BCrypt.Net.BCrypt.EnhancedHashPassword(password, 12);

    public bool Verify(string password, string hash) =>
        BCrypt.Net.BCrypt.EnhancedVerify(password, hash)
        || BCrypt.Net.BCrypt.Verify(password, hash);
}
