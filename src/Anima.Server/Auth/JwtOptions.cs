namespace Anima.Server.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public required string SigningKey { get; set; }

    // "Remember me" is the only mode (not opt-in) per spec: one long-lived token issued at login,
    // no refresh-token rotation. See AuthService's own comment for the accepted tradeoff (no
    // server-side revocation before expiry) this implies.
    public int LifetimeDays { get; set; } = 180;
}
