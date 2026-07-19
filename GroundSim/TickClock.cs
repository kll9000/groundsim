namespace GroundSim;

/// <summary>
/// Fixed-rate simulation clock, decoupled from render frame rate. The
/// renderer reports wall-clock elapsed time each frame; the clock answers
/// how many fixed simulation ticks are due, accumulating fractional ticks
/// across frames so the sim advances at a consistent speed no matter how
/// fast or unevenly frames arrive.
/// </summary>
public sealed class TickClock
{
    private double _accumulatedTicks;

    /// <summary>Simulation speed. Default 30 ticks/second.</summary>
    public double TicksPerSecond { get; set; } = 30;

    public bool Paused { get; set; }

    /// <summary>
    /// Safety cap on ticks returned per frame, so a long stall (breakpoint,
    /// window drag) causes a slow-motion catchup instead of a huge burst.
    /// </summary>
    public int MaxTicksPerAdvance { get; set; } = 8;

    /// <summary>Returns how many simulation ticks to run for this frame.</summary>
    public int Advance(double elapsedSeconds)
    {
        if (Paused || elapsedSeconds <= 0) return 0;
        _accumulatedTicks += elapsedSeconds * TicksPerSecond;
        int due = (int)_accumulatedTicks;
        if (due > MaxTicksPerAdvance)
        {
            // Drop the excess rather than owing it — we want smooth realtime,
            // not a burst that visually teleports particles.
            due = MaxTicksPerAdvance;
            _accumulatedTicks = 0;
        }
        else
        {
            _accumulatedTicks -= due;
        }
        return due;
    }
}
