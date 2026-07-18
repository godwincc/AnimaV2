using Anima.Core.Models;

namespace Anima.Core.Reforge;

// One rollable Head/Frame/Tail part in the Reforge pool. ArchetypeName is display-only context
// ("Reaper's Rend") -- Skill.Part/Color already carry the slot and color used for filtering/swap.
public sealed record ReforgeCandidate(string ArchetypeName, Skill Skill);
