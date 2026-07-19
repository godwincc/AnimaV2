using Anima.Core.Models;

namespace Anima.Core.Economy;

// Grants an Artifact to a RunLedger. Full shop/pickup UI flow isn't built yet -- this is the
// simple "give the player this Artifact" entry point both real code and tests use, firing the
// Artifact's own OnPickup hook (e.g. Marked Coin's one-time bonus) exactly once as part of the
// grant.
public static class ArtifactService
{
    public static void Grant(RunLedger runLedger, Artifact artifact, PersistentLedger persistentLedger, Random rng)
    {
        runLedger.Artifacts.Add(artifact);
        artifact.OnPickup?.Invoke(persistentLedger, rng);
    }
}
