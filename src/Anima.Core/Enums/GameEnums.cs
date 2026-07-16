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
    Passive
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
