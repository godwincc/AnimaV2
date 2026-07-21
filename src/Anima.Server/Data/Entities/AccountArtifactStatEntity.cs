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
// Both write paths are now real: FirstDiscoveredAtUtc is set by RecordDiscoveryAsync (Treasure/
// Shop pickup, wired in an earlier session), and DelvesWonWithCount is incremented by
// RecordWinAsync, called once per currently-held Artifact on a confirmed Boss Victory
// (GameHub.SubmitAction's Boss branch, Phase 5b).
public class AccountArtifactStatEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string ArtifactName { get; set; }
    public DateTime FirstDiscoveredAtUtc { get; set; }
    public int DelvesWonWithCount { get; set; }
    public int Version { get; set; }
}
