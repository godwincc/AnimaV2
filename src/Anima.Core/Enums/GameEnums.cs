namespace Anima.Core.Enums;

public enum AnimaColor
{
    Crimson,
    Onyx,
    Verdant,
    Azure,
    Vulcan,   // hybrid: Onyx + Crimson
    Mirage    // hybrid: Verdant + Azure
}

public enum Part
{
    Head,
    Frame,
    Tail,
    Crest
}

public enum AttackRange
{
    Melee,
    Ranged,
    NA
}

public enum TargetType
{
    Enemy,          // default front (position 1)
    ChosenEnemy,
    ChosenAny,
    LowestHpEnemy,
    LowestHpAlly,
    SelfTarget,
    Ally,
    AllAllies,
    AllEnemies
}

public enum SkillCategory
{
    Attack,
    Move,
    Buff,
    Debuff,
    Heal,
    Passive,
    Summon
}

public enum DurationType
{
    Instant,
    UntilConsumed,
    FixedTurn
}

public enum TriggerType
{
    None,
    PassiveConditional,
    PassiveEvent
}

// The 4 locked Augment types (see Anima.Core.Economy.AugmentService for eligibility/effect rules).
public enum AugmentType
{
    IncreaseEffect,
    AoEDamage,
    DecreaseCost,
    Extend
}
