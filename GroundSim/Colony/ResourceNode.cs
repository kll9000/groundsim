namespace GroundSim;

/// <summary>
/// A surface resource patch (leaf source) Foragers gather from. An entity at
/// an Air cell, not grid material — matching Colony Builder, where raw
/// material is an abstract quantity rather than physical cells.
/// </summary>
public sealed class ResourceNode
{
    public int X { get; }
    public int Y { get; }
    public double Remaining { get; set; }

    /// <summary>Regeneration ceiling — the node's initial amount.</summary>
    public double Cap { get; }

    /// <summary>Phase 28: colony-wide discovery flag. Starts false — a
    /// Forager must come within ColonyConfig.NodeDetectionRadius to find
    /// the node before ANY Forager can target it. Deliberately shared
    /// state, not per-ant memory: a trail is a real structure other ants
    /// can sense, so one find unlocks the site for the whole colony.
    /// Tests that are not about discovery pre-set this to true.</summary>
    public bool Discovered { get; set; }

    public ResourceNode(int x, int y, double amount)
    {
        X = x;
        Y = y;
        Remaining = amount;
        Cap = amount;
    }

    /// <summary>Phase 9 resource sustain: gathered-down nodes slowly regrow
    /// toward their cap (leaf regrowth), never beyond it.</summary>
    public void Regenerate(double perTick)
    {
        if (Remaining < Cap) Remaining = Math.Min(Cap, Remaining + perTick);
    }
}
