using Anima.Core.Models;

namespace Anima.Core.Reforge;

// One browsable Head/Frame/Tail skill option in the Reforge pool (see ReforgeService.
// GetBrowseOptions). ArchetypeName is display-only context ("Reaper's Rend") -- Skill.Part/Color
// already carry the slot and color used for filtering/cost calculation.
public sealed record ReforgeCandidate(string ArchetypeName, Skill Skill);
