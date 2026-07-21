namespace Anima.Core.Combat;

// Deliberately just the 3 states Phase 5a (core Combat loop porting) needs to know about --
// Victory/Defeat here means only "which side is fully wiped," nothing about rewards or Delve
// progression. Match Result screens, Wisp/Ember/Vessel/Echo Shard grants, DelveEndService, and
// Boss-hatch are all Phase 5b, deliberately not touched by anything that produces this enum.
public enum CombatOutcome
{
    InProgress,
    Victory,
    Defeat,
}
