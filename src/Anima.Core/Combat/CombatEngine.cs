namespace Anima.Core.Combat;

using Anima.Core.Enums;
using Anima.Core.Models;

public class CombatEngine
{
    private readonly CombatState _state;
    private readonly Random _random = new();

    private const int HandCap = 10;
    private const int CopiesPerSkill = 3;

    // Narration hook — kept out of Core so callers (console, UI, tests) decide how/whether to surface it.
    public Action<string>? OnLog { get; set; }

    // Placeholder for real player input / AI; the harness supplies a hardcoded priority for now.
    public Func<Anima, CombatState, Skill?>? ChoosePlayerSkill { get; set; }

    public CombatEngine(CombatState state)
    {
        _state = state;
    }

    // Builds the shared 27-card team deck (Head/Frame/Tail x3 copies each, Crests excluded),
    // shuffles it, and draws the opening hand of 7. Call once before the first RunRound().
    public void StartCombat()
    {
        BuildDeck();
        Shuffle(_state.DrawPile);
        DrawCards(7);
    }

    private void BuildDeck()
    {
        foreach (var anima in _state.PlayerTeam)
        {
            foreach (var skill in anima.DeckSkills)
            {
                for (var i = 0; i < CopiesPerSkill; i++)
                {
                    _state.DrawPile.Add(skill);
                }
            }
        }
    }

    // Fisher-Yates — the deck shuffle is an explicit, documented exception to the
    // "no randomness in combat" rule, so a plain Random is fine here.
    private void Shuffle(List<Skill> pile)
    {
        for (var i = pile.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (pile[i], pile[j]) = (pile[j], pile[i]);
        }
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

        // Elite/Boss Enrage — universal safety net against stalemates.
        foreach (var enemy in _state.EnemyTeam.Where(e => e.CurrentHp > 0))
        {
            if (enemy.EnrageRound.HasValue && !enemy.IsEnraged && _state.RoundNumber >= enemy.EnrageRound.Value)
            {
                enemy.IsEnraged = true;
                Log($"{enemy.Name} becomes ENRAGED! Damage output increased.");
            }
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
        ResolveSkill(
            anima,
            skill,
            _state.EnemyTeam.Cast<ICombatant>().ToList(),
            _state.PlayerTeam.Cast<ICombatant>().ToList(),
            anima.BaseStats.DamageMultiplier,
            anima.BaseStats.SpiritMultiplier);

        _state.Hand.Remove(skill);
        _state.DiscardPile.Add(skill);
    }

    private void ResolveEnemyTurn(Enemy enemy)
    {
        var rule = enemy.BehaviorRules.FirstOrDefault(r => (!enemy.IsEnraged || !r.IsDefensive) && r.Condition(enemy, _state));
        if (rule == null)
        {
            Log($"{enemy.DisplayName} has no valid action.");
            return;
        }

        Log($"{enemy.DisplayName} uses {rule.Skill.Name}.");
        ResolveSkill(
            enemy,
            rule.Skill,
            _state.PlayerTeam.Cast<ICombatant>().ToList(),
            _state.EnemyTeam.Cast<ICombatant>().ToList(),
            damageMultiplier: 1.0,
            spiritMultiplier: 1.0);
        rule.OnUsed?.Invoke(enemy);
    }

    private void ResolveSkill(
        ICombatant actor,
        Skill skill,
        List<ICombatant> opposingTeam,
        List<ICombatant> friendlyTeam,
        double damageMultiplier,
        double spiritMultiplier)
    {
        // Weak is Until-Consumed (same pattern as Shield/Primed) — it persists on its target
        // until that target's next skill use of any kind, regardless of Round/turn-order timing.
        var weakMagnitude = ConsumeWeak(actor);

        switch (skill.Category)
        {
            case SkillCategory.Attack:
                ResolveAttack(actor, skill, opposingTeam, friendlyTeam, damageMultiplier, spiritMultiplier, weakMagnitude);
                break;
            case SkillCategory.Move:
                ResolveMove(actor, skill, opposingTeam, friendlyTeam);
                break;
            case SkillCategory.Buff:
                ResolveBuff(actor, skill);
                break;
            case SkillCategory.Heal:
                ResolveHeal(actor, skill, friendlyTeam, spiritMultiplier, weakMagnitude);
                break;
            default:
                Log($"  ({skill.Category} skills aren't resolved yet.)");
                break;
        }
    }

    private int ConsumeWeak(ICombatant actor)
    {
        var weak = GetStatus(actor, "Weak");
        if (weak == null) return 0;

        actor.ActiveStatuses.Remove(weak);
        Log($"  {actor.DisplayName}'s Weak is consumed.");
        return weak.Magnitude;
    }

    private void ResolveAttack(
        ICombatant actor,
        Skill skill,
        List<ICombatant> opposingTeam,
        List<ICombatant> friendlyTeam,
        double damageMultiplier,
        double spiritMultiplier,
        int weakMagnitude)
    {
        var target = SelectTarget(skill.Target, opposingTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        var enrageMultiplier = actor is Enemy { IsEnraged: true } enragedEnemy ? enragedEnemy.EnrageDamageMultiplier : 1.0;
        var raw = skill.BaseDamage * damageMultiplier * enrageMultiplier;
        if (weakMagnitude > 0)
        {
            raw *= 1 - weakMagnitude / 100.0;
            Log($"  {actor.DisplayName} is Weak: damage reduced by {weakMagnitude}%.");
        }

        var rawInt = (int)Math.Round(raw);
        var multLabel = enrageMultiplier > 1.0 ? $"{damageMultiplier:0.##} mult x {enrageMultiplier:0.##} enrage" : $"{damageMultiplier:0.##} mult";
        var final = ApplyDamage(target, rawInt, $"{skill.BaseDamage} base x {multLabel}");

        TriggerReactiveEffects(actor, target, skill);

        if (skill.OnHitStatusKeyword != null)
        {
            ApplyStatus(target, skill.OnHitStatusKeyword, skill.OnHitStatusMagnitude, skill.OnHitStatusDuration, skill.OnHitStatusDurationTurns ?? 0);
            Log($"  {target.DisplayName} is afflicted with {skill.OnHitStatusKeyword} ({skill.OnHitStatusMagnitude}%).");
        }

        if (skill.BaseHeal > 0 && skill.SecondaryTarget != null)
        {
            var healTarget = SelectTarget(skill.SecondaryTarget.Value, friendlyTeam);
            if (healTarget != null)
            {
                ApplyHeal(actor, healTarget, skill.BaseHeal, spiritMultiplier, weakMagnitude);
            }
        }

        if (skill.SelfHealPercentOfDamage is double pct && final > 0)
        {
            var healAmount = (int)Math.Round(final * pct);
            var before = actor.CurrentHp;
            actor.CurrentHp = Math.Min(actor.CurrentHp + healAmount, actor.MaxHp);
            Log($"  {actor.DisplayName} drains {actor.CurrentHp - before} HP from the hit ({actor.DisplayName} HP: {actor.CurrentHp}).");
        }
    }

    // Shared damage pipeline (Shield absorb, then Defense floor) — used by both direct attacks
    // and reactive counter-damage (Retaliate/Thorns), so counters respect the attacker's own
    // Shield/Defense rather than being a bypassing special case.
    private int ApplyDamage(ICombatant target, int rawDamage, string sourceDescription)
    {
        var remaining = rawDamage;

        var shield = GetStatus(target, "Shield");
        var absorbed = 0;
        if (shield != null && remaining > 0)
        {
            absorbed = Math.Min(shield.Magnitude, remaining);
            shield.Magnitude -= absorbed;
            remaining -= absorbed;
            if (shield.Magnitude <= 0)
            {
                target.ActiveStatuses.Remove(shield);
            }
        }

        var final = remaining > 0 ? Math.Max(remaining - target.Defense, 1) : 0;
        target.CurrentHp = Math.Max(0, target.CurrentHp - final);

        Log($"  Target: {target.DisplayName}");
        var mathLine = absorbed > 0
            ? $"  Damage: {sourceDescription} = {rawDamage} raw - {absorbed} shield = {remaining} - {target.Defense} def = {final} dealt"
            : $"  Damage: {sourceDescription} = {rawDamage} raw - {target.Defense} def = {final} dealt";
        Log(mathLine);
        Log($"  {target.DisplayName} HP: {target.CurrentHp}");

        PurgeDeadAnimaCards(target);

        return final;
    }

    // A dead Anima never takes another turn, so its cards would otherwise sit in Hand/DrawPile/
    // DiscardPile forever — never played, never discarded, permanently eating into the shared
    // hand cap. Removing them on death is a real gameplay rule, not just a test-harness fix.
    private void PurgeDeadAnimaCards(ICombatant combatant)
    {
        if (combatant is not Anima anima || anima.CurrentHp > 0) return;

        var deckSkills = anima.DeckSkills;
        var removed = _state.Hand.RemoveAll(s => deckSkills.Contains(s))
            + _state.DrawPile.RemoveAll(s => deckSkills.Contains(s))
            + _state.DiscardPile.RemoveAll(s => deckSkills.Contains(s));

        if (removed > 0)
        {
            Log($"  {anima.DisplayName} has fallen — their remaining cards are removed from the deck.");
        }
    }

    // Retaliate/Thorns: flat counter-damage back at a Melee attacker, through the same
    // Shield/Defense pipeline as any other hit. Doesn't itself re-trigger reactive effects
    // (no counter-to-a-counter) and only fires if the defender survived the original hit.
    private void TriggerReactiveEffects(ICombatant attacker, ICombatant target, Skill skill)
    {
        if (skill.Range != AttackRange.Melee) return;
        if (target.CurrentHp <= 0) return;

        var retaliate = GetStatus(target, "Retaliate");
        if (retaliate != null)
        {
            Log($"  {target.DisplayName} Retaliates!");
            ApplyDamage(attacker, retaliate.Magnitude, "Retaliate counter");
        }

        var thorns = GetStatus(target, "Thorns");
        if (thorns != null)
        {
            Log($"  {target.DisplayName}'s Thorns triggers!");
            ApplyDamage(attacker, thorns.Magnitude, "Thorns counter");
        }
    }

    private void ResolveHeal(ICombatant actor, Skill skill, List<ICombatant> friendlyTeam, double spiritMultiplier, int weakMagnitude)
    {
        var target = skill.Target == TargetType.SelfTarget ? actor : SelectTarget(skill.Target, friendlyTeam);
        if (target == null)
        {
            Log("  No valid target.");
            return;
        }

        ApplyHeal(actor, target, skill.BaseHeal, spiritMultiplier, weakMagnitude);
    }

    private void ApplyHeal(ICombatant caster, ICombatant target, int baseHeal, double spiritMultiplier, int weakMagnitude)
    {
        var raw = baseHeal * spiritMultiplier;
        if (weakMagnitude > 0)
        {
            raw *= 1 - weakMagnitude / 100.0;
            Log($"  {caster.DisplayName} is Weak: healing reduced by {weakMagnitude}%.");
        }

        var healAmount = (int)Math.Round(raw);
        var before = target.CurrentHp;
        target.CurrentHp = Math.Min(target.CurrentHp + healAmount, target.MaxHp);
        var applied = target.CurrentHp - before;

        Log($"  Target: {target.DisplayName}");
        Log($"  Heal: {baseHeal} base x {spiritMultiplier:0.##} mult = {healAmount} healed ({applied} applied)");
        Log($"  {target.DisplayName} HP: {target.CurrentHp}");
    }

    private void ResolveMove(ICombatant actor, Skill skill, List<ICombatant> opposingTeam, List<ICombatant> friendlyTeam)
    {
        if (skill.TargetPositionOverride is { Length: > 0 } positions)
        {
            actor.Position = positions[0];
            Log($"  {actor.DisplayName} moves to position {actor.Position}.");
            return;
        }

        if (skill.MoveOffset is int offset)
        {
            var team = IsFriendlyTargetType(skill.Target) ? friendlyTeam : opposingTeam;
            var target = skill.Target == TargetType.SelfTarget ? actor : SelectTarget(skill.Target, team);
            if (target == null)
            {
                Log("  No valid target.");
                return;
            }

            var before = target.Position;
            target.Position = Math.Clamp(target.Position + offset, 1, 3);
            Log($"  {target.DisplayName} moves from position {before} to {target.Position}.");
        }
    }

    private static bool IsFriendlyTargetType(TargetType targetType) =>
        targetType is TargetType.Ally or TargetType.ChosenAny or TargetType.LowestHpAlly or TargetType.AllAllies or TargetType.SelfTarget;

    private const int MaxShieldMagnitude = 50;

    private void ResolveBuff(ICombatant actor, Skill skill)
    {
        if (skill.BaseShield > 0)
        {
            var existingShield = GetStatus(actor, "Shield");
            if (existingShield != null)
            {
                existingShield.Magnitude = Math.Min(existingShield.Magnitude + skill.BaseShield, MaxShieldMagnitude);
                Log($"  {actor.DisplayName} gains {skill.BaseShield} Shield (now {existingShield.Magnitude}).");
            }
            else
            {
                ApplyStatus(actor, "Shield", Math.Min(skill.BaseShield, MaxShieldMagnitude), skill.Duration, skill.DurationTurns ?? 0);
                Log($"  {actor.DisplayName} gains {skill.BaseShield} Shield.");
            }
            return;
        }

        // Simplification: a non-Shield Buff skill applies a status named after itself to its caster.
        // BuffMagnitude carries a value for statuses that need one (e.g. Retaliate/Thorns counter amount).
        ApplyStatus(actor, skill.Name, skill.BuffMagnitude, skill.Duration, skill.DurationTurns ?? 0);
        Log(skill.BuffMagnitude > 0
            ? $"  {actor.DisplayName} gains {skill.Name} ({skill.BuffMagnitude})."
            : $"  {actor.DisplayName} gains {skill.Name}.");
    }

    private static void ApplyStatus(ICombatant target, string keyword, int magnitude, DurationType duration, int durationTurns)
    {
        target.ActiveStatuses.Add(new StatusEffectInstance
        {
            Keyword = keyword,
            Magnitude = magnitude,
            Duration = duration,
            RemainingTurns = durationTurns,
        });
    }

    private static StatusEffectInstance? GetStatus(ICombatant combatant, string keyword) =>
        combatant.ActiveStatuses.FirstOrDefault(s => s.Keyword == keyword);

    private static ICombatant? SelectTarget(TargetType targetType, List<ICombatant> team)
    {
        var alive = team.Where(c => c.CurrentHp > 0).ToList();
        return targetType switch
        {
            // Marked overrides Taunt: it forces the *opposing* team's attacks specifically,
            // taking priority over any Taunt held within the target team.
            TargetType.Enemy or TargetType.Ally =>
                alive.FirstOrDefault(c => c.ActiveStatuses.Any(s => s.Keyword == "Marked"))
                    ?? alive.FirstOrDefault(c => c.ActiveStatuses.Any(s => s.Keyword == "Taunt"))
                    ?? alive.OrderBy(c => c.Position).FirstOrDefault(),
            TargetType.LowestHpEnemy or TargetType.LowestHpAlly => alive.OrderBy(c => c.CurrentHp).FirstOrDefault(),
            _ => alive.FirstOrDefault(),
        };
    }

    private void RoundEndPhase()
    {
        _state.RoundNumber++;
        // Win/loss condition is checked by the caller after RunRound() returns.
    }

    private void Log(string message) => OnLog?.Invoke(message);

    private void DrawCards(int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (_state.Hand.Count >= HandCap) break; // cap reached — remaining draws are skipped/lost

            if (_state.DrawPile.Count == 0)
            {
                if (_state.DiscardPile.Count == 0) break; // nothing left anywhere to draw

                _state.DrawPile.AddRange(_state.DiscardPile);
                _state.DiscardPile.Clear();
                Shuffle(_state.DrawPile);
                Log("  Discard pile shuffled back into the draw pile.");
            }

            var card = _state.DrawPile[0];
            _state.DrawPile.RemoveAt(0);
            _state.Hand.Add(card);
        }
    }

    private void TickStatusDurations(ICombatant combatant)
    {
        // Until-Consumed statuses (Shield, Primed, Weak) never decrement here — they're removed
        // only by ConsumeWeak / shield absorption / etc. at the point they're actually used.
        foreach (var status in combatant.ActiveStatuses.ToList())
        {
            if (status.Duration != DurationType.FixedTurn) continue;

            // Bleed is a DOT — applies its Magnitude directly to HP, bypassing Defense and
            // Shield entirely, rather than going through the normal damage pipeline.
            if (status.Keyword == "Bleed")
            {
                combatant.CurrentHp = Math.Max(0, combatant.CurrentHp - status.Magnitude);
                Log($"  {combatant.DisplayName} bleeds for {status.Magnitude} ({combatant.DisplayName} HP: {combatant.CurrentHp}).");
                PurgeDeadAnimaCards(combatant);
            }

            status.RemainingTurns--;
            if (status.RemainingTurns <= 0)
            {
                combatant.ActiveStatuses.Remove(status);
                Log($"  {combatant.DisplayName}'s {status.Keyword} expires.");
            }
        }
    }
}
