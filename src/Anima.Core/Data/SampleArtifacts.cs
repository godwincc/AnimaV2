namespace Anima.Core.Data;

using Anima.Core.Models;

// SAMPLE / REFERENCE DATA — validates the Artifact + OnCombatStart hook pattern end-to-end.
// Not the real artifact set. Replace once itemization design is finalized.
public static class SampleArtifacts
{
    public static Artifact CreateEmberCore()
    {
        return new Artifact
        {
            Name = "Ember Core",
            Description = "At the start of combat, gain 1 additional shared energy.",
            OnCombatStart = state => state.SharedEnergy += 1,
        };
    }
}
