namespace Anima.Server.Data.Entities;

// A single-use, expiring token issued by the "request password reset" endpoint. Only the hash is
// stored (same PasswordHasher-backed approach as the account password itself) so a DB read alone
// never discloses a usable token. Email DELIVERY of the raw token is explicitly out of scope for
// this pass -- see AuthService's own comment -- so today the raw token is only ever handed back to
// the caller in the request-reset response, which is a real, flagged stand-in for a real mailer.
public class PasswordResetTokenEntity
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}
