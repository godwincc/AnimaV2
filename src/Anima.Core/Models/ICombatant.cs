namespace Anima.Core.Models;

public interface ICombatant
{
    string DisplayName { get; }
    int CurrentHp { get; set; }
    int Position { get; set; }
    int Speed { get; }
    int Defense { get; }
    List<StatusEffectInstance> ActiveStatuses { get; set; }
}
