namespace Anima.Core.Economy;

// Why AugmentService.TryApplyAugment refused to run at all -- checked (and, if triggered,
// returned) before anything is mutated, any Wisp is charged, or the caller's Ember is treated as
// consumed.
public enum AugmentRejectionReason
{
    None,
    MaxAugmentsReached,
    SkillMissingColor,

    // The dropped Ember's color doesn't match the target skill's color -- the pickup-page UI is
    // expected to only ever offer same-color skills, so this is a defensive check, not an expected
    // player-facing path.
    EmberColorMismatch,
    NotApplicableToSkill,
    InsufficientWisp,
}
