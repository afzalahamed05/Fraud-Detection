namespace FraudDetection.Api.Configuration;

/// <summary>
/// Deliberately minimal for a portfolio project: one seeded admin account, no user table.
/// The password is never stored in plaintext -- see AuthController for the PBKDF2 check.
/// A real system would back this with ASP.NET Core Identity (or an external IdP) instead.
/// </summary>
public class AuthOptions
{
    public const string SectionName = "Auth";

    public string JwtSecret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "fraud-detection-api";
    public string Audience { get; set; } = "fraud-detection-dashboard";
    public int TokenExpiryMinutes { get; set; } = 60;

    public string AdminUsername { get; set; } = "admin";

    /// <summary>Base64 PBKDF2 hash of the demo admin password.</summary>
    public string AdminPasswordHash { get; set; } = string.Empty;

    /// <summary>Base64 salt used to produce AdminPasswordHash.</summary>
    public string AdminPasswordSalt { get; set; } = string.Empty;
}
