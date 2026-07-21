namespace Anima.Core.Combat;

using Anima.Core.Models;

public class CombatState
{
    public required List<Anima> PlayerTeam { get; set; }
    public required List<Enemy> EnemyTeam { get; set; }
    public int SharedEnergy { get; set; } = 3;
    public int RoundNumber { get; set; } = 1;
    public List<Skill> DrawPile { get; set; } = new();
    public List<Skill> Hand { get; set; } = new();
    public List<Skill> DiscardPile { get; set; } = new();

    // Twin Flame Artifact: set the first time it saves a player Anima from a lethal hit this
    // combat. Never reset mid-combat -- a fresh CombatState is constructed per combat, so this
    // naturally resets between fights with no extra plumbing needed.
    public bool TwinFlameUsed { get; set; }

    // Focusing Lens Artifact: counts Attack-category skills played by the PLAYER team this
    // combat (team-wide, not per-Anima -- same "shared resource" shape as SharedEnergy). Resets
    // between fights the same way TwinFlameUsed does, for free, via a fresh CombatState per combat.
    public int AttackSkillsPlayed { get; set; }

    // This Round's rolled Initiative order + a cursor into it -- moved here (off a private
    // CombatEngine instance field) so a resumable, hub-driven combat can reconstruct a fresh
    // CombatEngine on every SubmitAction call (matching every other GameHub method's stateless-
    // per-call pattern) without losing its place mid-Round. RunRound's own synchronous loop
    // (console harness) doesn't read TurnIndex at all -- it still just foreachs its own local
    // initiativeOrder list -- so this is purely additive for the new resumable path; see
    // CombatEngine.AdvanceUntilPlayerActionNeeded.
    public List<ICombatant> TurnOrder { get; set; } = new();
    public int TurnIndex { get; set; }

    // The combatant whose turn it currently is, under the resumable model -- null once TurnIndex
    // runs off the end (a Round just finished, or combat is over and nobody has re-rolled yet).
    public ICombatant? CurrentActor => TurnIndex >= 0 && TurnIndex < TurnOrder.Count ? TurnOrder[TurnIndex] : null;

    // Silent Chime: set by CombatEngine.TryActivateSilentChime, consumed the moment the targeted
    // Anima's normal turn resolves this Round -- see both for the full mechanic. Moved here from a
    // private CombatEngine instance field for the same reason TurnOrder was: it must survive a
    // fresh CombatEngine being reconstructed per hub call (GameHub.SubmitAction's pattern), or an
    // activation made on one hub call would silently vanish before the extra action could fire on
    // the next.
    public Anima? PendingSilentChimeTarget { get; set; }
}
