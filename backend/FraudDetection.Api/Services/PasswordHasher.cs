using System.Security.Cryptography;

namespace FraudDetection.Api.Services;

/// <summary>PBKDF2-SHA256, 100k iterations -- no extra NuGet dependency needed, the whole
/// implementation is System.Security.Cryptography from the BCL.</summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;
    private const int HashSizeBytes = 32;

    public static bool Verify(string password, string base64Salt, string base64ExpectedHash)
    {
        var salt = Convert.FromBase64String(base64Salt);
        var expectedHash = Convert.FromBase64String(base64ExpectedHash);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
