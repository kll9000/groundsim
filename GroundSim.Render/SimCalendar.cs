namespace GroundSim.Render;

/// <summary>
/// Phase 21 Part B: converts elapsed ticks into a human-readable "day"
/// number for the status bar. PRESENTATION ONLY — nothing in the
/// simulation reads this.
///
/// Conversion: 1 display-day = 43,200 ticks = 24 sim-minutes at the
/// canonical 30 ticks/sec (Phase 14 basis). INVENTED/ARBITRARY choice,
/// reasoned for narrative scale rather than realism: founding takes
/// ~47k ticks ≈ 1.1 days, a room ~0.6 days, a worker's lifespan ~1
/// day — so "Day N" advances at a rate that makes colony milestones
/// read as a diary ("founded on day 1, garden by day 2, first deaths
/// day 3"). At the 240 tps playback default, one display-day passes in
/// three real minutes of watching.
/// </summary>
public static class SimCalendar
{
    public const int TicksPerDay = 43_200;

    /// <summary>1-based fractional day number for display ("day 1.0" at
    /// tick zero).</summary>
    public static double DayNumber(int tick) => 1.0 + tick / (double)TicksPerDay;
}
