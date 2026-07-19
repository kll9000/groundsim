namespace GroundSim;

/// <summary>
/// A chunk of loose material in flight (dropped, not yet settled).
/// Mutable struct kept in the simulation's active list only while falling.
/// </summary>
public struct Particle
{
    public int X;
    public int Y;
    public CellMaterial Material;

    public Particle(int x, int y, CellMaterial material)
    {
        X = x;
        Y = y;
        Material = material;
    }
}
