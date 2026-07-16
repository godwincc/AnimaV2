using Anima.Core.Enums;

namespace Anima.Core.Models;

public class StatusEffectInstance
{
    public required string Keyword { get; set; } // "Shield", "Bleed", "Weak", "Marked", etc.
    public int Magnitude { get; set; } // damage/heal/shield amount, debuff %, etc.
    public DurationType Duration { get; set; }
    public int RemainingTurns { get; set; } // relevant only for FixedTurn
}
