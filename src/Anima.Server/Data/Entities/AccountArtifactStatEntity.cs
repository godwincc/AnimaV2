namespace Anima.Server.Data.Entities;

// One row per (account, Artifact) the account has ever picked up -- backs the Collection screen's
// "X of 12 discovered" count and each unlocked Artifact's "Delves won with: X" stat. Neither stat
// had ANY persistent home before this: RunLedger.Artifacts is a fresh, run-scoped list every
// Delve and is never written to the DB, and nothing in Anima.Core records a win-while-held event
// at all. A missing row for a given ArtifactName means "never discovered" (matches
// PersistedLedgerEntryEntity's own "missing row = 0" convention).
//
// ArtifactName is checked against Anima.Core.Data.SampleArtifacts' Name strings, the same
// by-name identity the rest of the Artifact system already uses (see SampleArtifacts' own
// comment) -- there is no separate Artifact Id today.
//
// NOTE (scope): only the read side (AccountArtifactStatRepository.LoadAsync) is wired up in this
// pass. Nothing yet calls a write path to set FirstDiscoveredAtUtc (on Artifact pickup) or
// increment DelvesWonWithCount (on Boss Victory while held) -- those call sites live in
// Treasure/Shop pickup and Boss-victory resolution, none of which are ported onto GameHub yet
// (see CLAUDE.md's Known TODOs). Every account's Collection reads as "0 of 12 discovered" until
// that follow-up work lands.
public class AccountArtifactStatEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string ArtifactName { get; set; }
    public DateTime FirstDiscoveredAtUtc { get; set; }
    public int DelvesWonWithCount { get; set; }
    public int Version { get; set; }
}
