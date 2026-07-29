namespace Anima.Server.Data.Entities;

// DB-backed counterpart to Sessions.PendingStarterReveal, mirroring PendingBossHatchEntity's shape
// and stakes: a freshly-registered account's 3 rolled starter Anima are real, already-committed-in-
// substance (StarterAnimaService.RollStarterTrio already ran, at AuthService.RegisterAsync time) and
// must survive a disconnect mid-naming -- losing them would mean a brand-new account permanently
// stuck with zero playable Anima and no way to re-roll for free. At most one row per account.
//
// RollsJson serializes the whole IReadOnlyList&lt;StarterAnimaRoll&gt; (ArchetypeName + AnimaGenome
// per slot) as one JSON array -- same "serialize the whole object" pattern PendingBossHatchEntity.
// GenomeJson/PersistedAnimaEntity.AnimaJson already use, just for a 3-element list instead of one
// genome. NextUnnamedIndex is a plain column (not folded into the JSON) since GameHub needs to
// mutate it independently on every ConfirmStarterAnima call without re-serializing the rolls.
//
// ConfirmedAnimaIdsJson (NEW -- migration AddConfirmedAnimaIdsToPendingStarterReveal) mirrors
// NextUnnamedIndex's own "plain mutable column" treatment, for the same reason: GameHub needs to
// append one Id per Confirm call. Defaults to "[]" so the migration doesn't break any row that
// already existed pre-migration (NextUnnamedIndex==0 for all of those, so an empty confirmed list
// is correct for them regardless).
public class PendingStarterRevealEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public required string RollsJson { get; set; }
    public int NextUnnamedIndex { get; set; }
    public string ConfirmedAnimaIdsJson { get; set; } = "[]";
    public int Version { get; set; }
}
