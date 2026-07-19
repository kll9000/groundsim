namespace GroundSim;

/// <summary>
/// The material occupying a grid cell. Extend freely; keep Air = 0 so that
/// default-initialized cells are empty.
/// </summary>
public enum CellMaterial : byte
{
    Air = 0,
    Dirt,
    Rock,
    Grass,
    Fungus,
}
