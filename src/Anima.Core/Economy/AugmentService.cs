using Anima.Core.Enums;
using Anima.Core.Models;

namespace Anima.Core.Economy;

// Spends one already-dropped Ember (of a specific color, passed in by the caller -- see
// EmberService's own comment for the full pickup-choice flow) plus a Wisp tier to permanently
// mutate a skill in place -- the real system replacing the Delve simulation's earlier
// [SIM AUGMENT] stub. Mirrors WeavingService.AttemptWeave's own "check everything, reject before
// touching anything, only then commit" shape.
//
// Unlike the old bank-based version, this never touches a per-color Ember balance -- the caller
// already committed to spending ITS Ember on THIS attempt before calling in (that's what
// "Augment now" means at the pickup screen), so the only things TryApplyAugment itself checks or
// charges are the emberColor/skill.Color match and the Wisp tier cost.
public static class AugmentService
{
    public const int MaxAugmentsPerPart = 3;

    // Locked curve: Wisp cost of a part's 1st/2nd/3rd Augment slot, indexed by
    // AppliedAugments.Count going into the attempt (0 -> 1st slot's cost, ... 2 -> 3rd slot's
    // cost). This is IN ADDITION TO the 1 Ember every application consumes -- replaces the old
    // 2/4/7 Ember-only cost curve entirely.
    private static readonly int[] AugmentWispCostCurve = [15, 30, 50];

    // Increase Effect's multiplier. Not specified beyond "boosts the skill's core numbers" --
    // picked +20% to match Wisp Charm's/Ember Core's own already-locked +/-20%, for a consistent
    // Augment/Artifact power level rather than inventing an unrelated number. Flag to the user if
    // a different value was actually intended.
    private const double IncreaseEffectMultiplier = 1.20;

    // AoE Damage's value-per-target hit, per the brief's own "at 50% value" instruction -- not a
    // judgment call, just the locked number.
    private const double AoEDamageValueMultiplier = 0.5;

    // Decrease Cost's flat reduction. Not specified beyond "no floor" (i.e. unlike Clarity's
    // Math.Max(0, cost-1) discount, this is allowed to push EnergyCost to 0 or negative once
    // stacked) -- picked a flat -1/application (matching the size of most sample EnergyCost values,
    // 1-3) over a percentage, specifically so "no floor" has a chance to actually matter: 3
    // applications on a 2-cost skill would leave it at -1, i.e. a net energy REFUND on use. Flag to
    // the user if a different value was actually intended.
    private const int DecreaseCostAmount = 1;

    // The skill's own primary magnitude field, in priority order -- the first nonzero one found is
    // what Increase Effect boosts. Covers every skill shape seen so far: Attack (BaseDamage), Heal
    // (BaseHeal, or HotMagnitude for a HOT-only skill like Renew where BaseHeal is 0), Buff
    // (BaseShield, or BuffMagnitude for a magnitude-only self-buff like Retaliate/Thorns). A skill
    // with none of these set (e.g. Pin, a pure Stun Debuff) has nothing Increase Effect can boost.
    private static readonly (Func<Skill, int> Get, Action<Skill, int> Set)[] IncreaseEffectFields =
    [
        (s => s.BaseDamage, (s, v) => s.BaseDamage = v),
        (s => s.BaseHeal, (s, v) => s.BaseHeal = v),
        (s => s.BaseShield, (s, v) => s.BaseShield = v),
        (s => s.HotMagnitude, (s, v) => s.HotMagnitude = v),
        (s => s.BuffMagnitude, (s, v) => s.BuffMagnitude = v),
    ];

    private static bool TryGetIncreaseEffectField(Skill skill, out (Func<Skill, int> Get, Action<Skill, int> Set) field)
    {
        foreach (var candidate in IncreaseEffectFields)
        {
            if (candidate.Get(skill) > 0)
            {
                field = candidate;
                return true;
            }
        }
        field = default;
        return false;
    }

    // AoE Damage only makes sense converting a genuinely single-enemy-target Attack -- rejects an
    // already-AoE skill (nothing to convert), a 0-damage skill (nothing to scale), and anything
    // that isn't Attack/single-enemy-targeted to begin with (Heal/Buff/Debuff skills, ally-targeted
    // skills, etc.).
    private static bool IsSingleEnemyTargetAttack(Skill skill) =>
        skill.Category == SkillCategory.Attack
        && skill.BaseDamage > 0
        && skill.Target is TargetType.Enemy or TargetType.ChosenEnemy or TargetType.LowestHpEnemy;

    // Extend only makes sense on a skill that actually applies an UntilConsumed on-hit debuff --
    // rejects skills with no on-hit status at all, and skills whose on-hit status is FixedTurn
    // (e.g. Bleed), since Charges (what Extend adds) is an UntilConsumed-only concept -- a
    // FixedTurn status already has its own "how long it lasts" mechanism (RemainingTurns).
    private static bool IsExtendableDebuff(Skill skill) =>
        skill.OnHitStatusKeyword != null && skill.OnHitStatusDuration == DurationType.UntilConsumed;

    public static bool IsApplicable(Skill skill, AugmentType type) => type switch
    {
        AugmentType.IncreaseEffect => TryGetIncreaseEffectField(skill, out _),
        AugmentType.AoEDamage => IsSingleEnemyTargetAttack(skill),
        AugmentType.DecreaseCost => true, // always applicable -- every skill has an EnergyCost, and "no floor" means there's no lower bound to run out of room against
        AugmentType.Extend => IsExtendableDebuff(skill),
        _ => false,
    };

    // Wisp cost of the NEXT Augment slot on this skill, or null if it's already at the 3-Augment
    // cap. Pure lookup (e.g. for a UI cost preview) -- TryApplyAugment is what actually checks
    // affordability and deducts from a PersistentLedger. Doesn't account for the 1 Ember the
    // attempt will also consume -- the caller already has that Ember in hand by the time this
    // matters (it's what got them to the Augment page in the first place).
    public static int? GetNextAugmentCost(Skill skill) =>
        skill.AppliedAugments.Count < AugmentWispCostCurve.Length ? AugmentWispCostCurve[skill.AppliedAugments.Count] : null;

    // Full attempt orchestration: cap, color, color-match, applicability, and Wisp affordability
    // are ALL checked -- and, if any fails, rejected -- before anything is mutated or a single Wisp
    // is spent. Only once every check has passed does this commit: Wisp is spent, the skill's
    // field(s) are mutated in place, and AugmentType is recorded onto AppliedAugments (which also
    // drives next slot's cost via GetNextAugmentCost). The caller's Ember itself isn't tracked
    // here at all -- a Success result means the caller should treat their Ember as spent; a
    // rejection means it wasn't and the caller keeps it (see AugmentResult's own comment).
    //
    // runLedger is optional, same pattern as ReforgeService.Accept -- omitting it just means no
    // Ember Core discount applies to the Wisp tier.
    public static AugmentResult TryApplyAugment(Skill skill, AugmentType type, AnimaColor emberColor, PersistentLedger ledger, RunLedger? runLedger = null)
    {
        if (skill.AppliedAugments.Count >= MaxAugmentsPerPart)
            return AugmentResult.Rejected(AugmentRejectionReason.MaxAugmentsReached);

        if (skill.Color is not AnimaColor color)
            return AugmentResult.Rejected(AugmentRejectionReason.SkillMissingColor);

        if (color != emberColor)
            return AugmentResult.Rejected(AugmentRejectionReason.EmberColorMismatch);

        if (!IsApplicable(skill, type))
            return AugmentResult.Rejected(AugmentRejectionReason.NotApplicableToSkill);

        var cost = ArtifactService.ApplyEmberCoreDiscount(AugmentWispCostCurve[skill.AppliedAugments.Count], runLedger);
        if (!ledger.CanAfford(ResourceType.Wisp, cost))
            return AugmentResult.Rejected(AugmentRejectionReason.InsufficientWisp);

        // Every check passed -- commit. Nothing above this point mutated the skill or the ledger.
        ledger.TrySpend(ResourceType.Wisp, cost);
        Apply(skill, type);
        skill.AppliedAugments.Add(type);

        return new AugmentResult(true, AugmentRejectionReason.None, cost, type);
    }

    private static void Apply(Skill skill, AugmentType type)
    {
        switch (type)
        {
            case AugmentType.IncreaseEffect:
                TryGetIncreaseEffectField(skill, out var field);
                field.Set(skill, (int)Math.Round(field.Get(skill) * IncreaseEffectMultiplier));
                break;

            case AugmentType.AoEDamage:
                skill.Target = TargetType.AllEnemies;
                skill.BaseDamage = (int)Math.Round(skill.BaseDamage * AoEDamageValueMultiplier);
                break;

            case AugmentType.DecreaseCost:
                skill.EnergyCost -= DecreaseCostAmount; // deliberately unclamped -- see DecreaseCostAmount's own comment
                break;

            case AugmentType.Extend:
                skill.OnHitStatusExtraCharges += 1;
                break;
        }
    }
}
