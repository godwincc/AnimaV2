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

    // The player's currently-selected active team (Sanctum's "In team" badge, Hub's Team card) --
    // up to 3 Anima.Core.Models.Anima.Id strings, JSON-array-encoded. Null/empty means no team
    // selected yet. Deliberately NOT a relational join table: exactly 3 slots, no need to query
    // "who's on team X" from the other direction, same reasoning PersistedAnimaEntity's own
    // AnimaJson blob uses for a shape that doesn't need per-field SQL querying.
    public string? TeamAnimaIdsJson { get; set; }

    // Optimistic-concurrency token. Sqlite has no native rowversion/timestamp column, so this is a
    // plain int bumped by hand in AnimaDbContext.SaveChangesAsync -- see IConcurrencyStamped.
    public int Version { get; set; }
}
