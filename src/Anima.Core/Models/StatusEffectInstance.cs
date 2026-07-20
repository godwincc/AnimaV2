using Anima.Core.Enums;

namespace Anima.Core.Models;

public class StatusEffectInstance
{
    public required string Keyword { get; set; } // "Shield", "Bleed", "Weak", "Marked", etc.
    public int Magnitude { get; set; } // damage/heal/shield amount, debuff %, etc.
    public DurationType Duration { get; set; }
    public int RemainingTurns { get; set; } // relevant only for FixedTurn

    // Relevant only for UntilConsumed: how many more times this status survives being consumed
    // before it's actually removed. 1 (the default) reproduces every existing UntilConsumed status's
    // original behavior unchanged (gone the moment it's first consumed) -- only the Extend Augment
    // ever sets this above 1, via Skill.OnHitStatusExtraCharges.
    public int Charges { get; set; } = 1;
}
