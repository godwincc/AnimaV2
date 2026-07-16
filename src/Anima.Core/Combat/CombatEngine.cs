namespace Anima.Core.Combat;

using Anima.Core.Enums;
using Anima.Core.Models;

public class CombatEngine
{
    private readonly CombatState _state;

    // Narration hook — kept out of Core so callers (console, UI, tests) decide how/whether to surface it.
    public Action<string>? OnLog { get; set; }

    // Placeholder for real player input / AI; the harness supplies a hardcoded priority for now.
    public Func<Anima, CombatState, Skill?>? ChoosePlayerSkill { get; set; }

    public CombatEngine(CombatState state)
    {
        _state = state;
    }

    public void RunRound()
    {
        Log($"=== Round {_state.RoundNumber} ===");
        RoundStartPhase();
        var initiativeOrder = InitiativePhase();
        LogInitiativeOrder(initiativeOrder);
        ActionPhase(initiativeOrder);
        RoundEndPhase();
    }

    private void RoundStartPhase()
    {
        // Energy refill (+3, capped)
        _state.SharedEnergy = Math.Min(_state.SharedEnergy + 3, 9);
        Log($"Energy: {_state.SharedEnergy}");

        // Draw cards (opening hand 7 handled separately at combat start; +3/round here)
        DrawCards(3);

        // Tick down all active Durations across all combatants
        foreach (var combatant in AllCombatants())
        {
            TickStatusDurations(combatant);
        }
    }

    private IEnumerable<ICombatant> AllCombatants() =>
        _state.PlayerTeam.Cast<ICombatant>().Concat(_state.EnemyTeam.Cast<ICombatant>());

    private List<ICombatant> InitiativePhase()
    {
        // TODO: handle Providence-style "always acts first" override
        // TODO: handle Speed ties via roll
        return AllCombatants()
            .Where(c => c.CurrentHp > 0)
            .OrderByDescending(c => c.Speed)
            .ToList();
    }

    private void LogInitiativeOrder(List<ICombatant> initiativeOrder)
    {
        var order = string.Join(" > ", initiativeOrder.Select(c => $"{c.DisplayName}(Spd {c.Speed})"));
        Log($"Initiative: {order}");
    }

    private void ActionPhase(List<ICombatant> initiativeOrder)
    {
        foreach (var actor in initiativeOrder)
        {
            if (actor.CurrentHp <= 0) continue; // may have died mid-round

            switch (actor)
            {
                case Anima anima:
                    ResolvePlayerTurn(anima);
                    break;
                case Enemy enemy:
                    ResolveEnemyTurn(enemy);
                    break;
            }
        }
    }

    private void ResolvePlayerTurn(Anima anima)
    {
        var skill = ChoosePlayerSkill?.Invoke(anima, _state);
        if (skill == null)
        {
            Log($"{anima.DisplayName} passes.");
            return;
        }

        Log($"{anima.DisplayName} uses {skill.Name} ({skill.EnergyCost} energy).");
        _state.SharedEnergy -= skill.EnergyCost;
        ResolveSkill(anima, skill, _state.EnemyTeam.Cast<ICombatant>().ToList(), anima.BaseStats.DamageMultiplier);
    }

    private void ResolveEnemyTurn(Enemy enemy)
    {
        var rule = enemy.BehaviorRules.FirstOrDefault(r => r.Condition(enemy, _state));
        if (rule == null)
        {
            Log($"{enemy.DisplayName} has no valid action.");
            return;
        }

        Log($"{enemy.DisplayName} uses {rule.Skill.Name}.");
        ResolveSkill(enemy, rule.Skill, _state.PlayerTeam.Cast<ICombatant>().ToList(), damageMultiplier: 1.0);
    }

    private void ResolveSkill(ICombatant actor, Skill skill, List<ICombatant> opposingTeam, double damageMultiplier)
    {
        switch (skill.Category)
        {
            case SkillCategory.Attack:
                ResolveAttack(skill, opposingTeam, damageMultiplier);
                break;
            case SkillCategory.Move:
                ResolveMove(actor, skill);
                break;
            case SkillCategory.Buff:
                ResolveBuff(actor, skill);
                break;
            default:
                Log($"  ({skill.Category} skills aren't resolved yet.)");
                break;
        }
    }

    private void ResolveAttack(Skill skill, List<ICombatant> opposingTeam, double damageMultiplier)
    {
        var target = SelectTarget(skill.Target, opposingTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        var raw = skill.BaseDamage * damageMultiplier;
        var defense = target.Defense;
        var final = Math.Max(0, (int)Math.Round(raw) - defense);
        target.CurrentHp = Math.Max(0, target.CurrentHp - final);

        Log($"  Target: {target.DisplayName}");
        Log($"  Damage: {skill.BaseDamage} base x {damageMultiplier:0.##} mult = {raw:0.#} raw - {defense} def = {final} dealt");
        Log($"  {target.DisplayName} HP: {target.CurrentHp}");
    }

    private void ResolveMove(ICombatant actor, Skill skill)
    {
        if (skill.TargetPositionOverride is { Length: > 0 } positions)
        {
            actor.Position = positions[0];
            Log($"  {actor.DisplayName} moves to position {actor.Position}.");
        }
    }

    private void ResolveBuff(ICombatant actor, Skill skill)
    {
        // Simplification: a Buff skill applies a status named after itself to its caster.
        actor.ActiveStatuses.Add(new StatusEffectInstance
        {
            Keyword = skill.Name,
            Duration = skill.Duration,
            RemainingTurns = skill.DurationTurns ?? 0,
        });
        Log($"  {actor.DisplayName} gains {skill.Name}.");
    }

    private static ICombatant? SelectTarget(TargetType targetType, List<ICombatant> opposingTeam)
    {
        var alive = opposingTeam.Where(c => c.CurrentHp > 0).ToList();
        return targetType switch
        {
            TargetType.Enemy => alive.OrderBy(c => c.Position).FirstOrDefault(),
            TargetType.LowestHpEnemy => alive.OrderBy(c => c.CurrentHp).FirstOrDefault(),
            _ => alive.FirstOrDefault(),
        };
    }

    private void RoundEndPhase()
    {
        _state.RoundNumber++;
        // Win/loss condition is checked by the caller after RunRound() returns.
    }

    private void Log(string message) => OnLog?.Invoke(message);

    private void DrawCards(int count) { /* TODO: full 27-card deck/draw system */ }
    private void TickStatusDurations(ICombatant combatant) { /* TODO */ }
}
