namespace Anima.Server.Data.Entities;

// Sqlite has no native rowversion/timestamp column type (unlike SQL Server), so optimistic
// concurrency is emulated by hand: every entity that needs it exposes a plain int Version, mapped
// as a concurrency token in AnimaDbContext.OnModelCreating, and AnimaDbContext.SaveChangesAsync
// increments Version on every Modified entry that implements this interface right before the real
// save. A second writer holding a stale Version then collides on the token EF already checks,
// producing the normal DbUpdateConcurrencyException instead of silently overwriting the other
// writer's change.
public interface IConcurrencyStamped
{
    int Version { get; set; }
}
