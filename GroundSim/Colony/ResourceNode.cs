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

    public ResourceNode(int x, int y, double amount)
    {
        X = x;
        Y = y;
        Remaining = amount;
    }
}
