namespace Anima.Server.Data.Entities;

// One row per Anima a given account owns (SanctumRoster's real storage). The whole
// Anima.Core.Models.Anima is serialized to AnimaJson rather than mapped column-by-column --
// Skill alone has ~30 fields (see Anima.Core/Models/Skill.cs) and mapping that relationally would
// be a large, fragile effort for a shape that's still evolving. Name/Color/Gen are duplicated out
// as real columns purely so the Sanctum-grid screen's list/filter/sort can query them without
// deserializing every row; they are NOT the source of truth (AnimaJson is) and must be kept in
// sync by the repository on every write.
//
// Skill.SummonFactory/SummonFactoryChoices are Func<Enemy> delegates and cannot be JSON-serialized
// -- confirmed safe here because grep shows they are only ever set on Enemy-owned skills
// (Anima.Core/Data/SampleEnemies.cs), never on a player Anima's Head/Frame/Tail/Crest. They stay
// null on every persisted Anima, so System.Text.Json writes them as `null` without needing to
// serialize the delegate itself. Skill.cs marks both [JsonIgnore] anyway as a defense-in-depth
// measure so this can never break silently if that assumption ever changes.
public class PersistedAnimaEntity : IConcurrencyStamped
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    // Matches Anima.Core.Models.Anima.Id -- the stable identity Weaving lineage/sibling checks key
    // off. Unique per account (an account can't own the same Anima twice).
    public required string AnimaId { get; set; }

    public required string Name { get; set; }
    public required string Color { get; set; }
    public required int Gen { get; set; }

    public required string AnimaJson { get; set; }

    public int Version { get; set; }
}
