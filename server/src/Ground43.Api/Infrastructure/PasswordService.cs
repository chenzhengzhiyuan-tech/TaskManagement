using Ground43.Api.Data;
using Microsoft.AspNetCore.Identity;

namespace Ground43.Api.Infrastructure;

public sealed class PasswordService
{
    private readonly PasswordHasher<UserEntity> _hasher = new();
    public string Hash(UserEntity user, string password) => _hasher.HashPassword(user, password);
    public bool Verify(UserEntity user, string password) => _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
