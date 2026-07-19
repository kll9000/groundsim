namespace GroundSim;

/// <summary>An unhatched egg. Matures over ticks; a tending Tender speeds it up.</summary>
public sealed class Egg
{
    public int X { get; }
    public int Y { get; }
    public int RemainingTicks { get; private set; }

    /// <summary>Set by a Tender standing at the egg this tick; consumed by Tick().</summary>
    public bool TendedThisTick { get; set; }

    public Egg(int x, int y, int maturationTicks)
    {
        X = x;
        Y = y;
        RemainingTicks = maturationTicks;
    }

    public void Tick(int tendedSpeed)
    {
        RemainingTicks -= TendedThisTick ? tendedSpeed : 1;
        TendedThisTick = false;
    }

    public bool IsMature => RemainingTicks <= 0;
}
