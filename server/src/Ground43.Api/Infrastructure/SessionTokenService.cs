using System.Security.Cryptography;
using System.Text;

namespace Ground43.Api.Infrastructure;

public sealed class SessionTokenService
{
    public string CreateToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
