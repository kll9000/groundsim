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

    /// <summary>
    /// Loose surface rock debris. Unlike terrain Rock it is diggable/carryable,
    /// and as a particle it resists diagonal sliding (steeper piles than dirt).
    /// </summary>
    LooseRock,

    /// <summary>
    /// A stick. Falls straight down and settles where it lands — never slides
    /// diagonally, so stick drops stack rather than forming sloped piles.
    /// </summary>
    Stick,
}
