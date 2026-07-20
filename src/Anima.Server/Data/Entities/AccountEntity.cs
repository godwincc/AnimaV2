namespace Anima.Server.Data.Entities;

// One row per registered player. Username is the login handle; Email exists solely to support the
// password-reset flow (no verification/confirmation step per spec -- collected at signup and
// otherwise unused until a reset is requested).
public class AccountEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public required string Username { get; set; }
    public required string NormalizedUsername { get; set; } // uppercase, for case-insensitive uniqueness/lookup
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // Optimistic-concurrency token. Sqlite has no native rowversion/timestamp column, so this is a
    // plain int bumped by hand in AnimaDbContext.SaveChangesAsync -- see IConcurrencyStamped.
    public int Version { get; set; }
}
