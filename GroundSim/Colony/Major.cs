namespace GroundSim;

/// <summary>
/// The largest worker. Narrowed post-Soldier-split definition: speeds
/// excavation of whatever site is currently being dug, by digging alongside
/// (via DigAssist, which composes the full Agent dig-carry-drop machinery so
/// its spoil is physically hauled and conserved like everyone else's).
///
/// Phase 18 Part C: with no active dig work, a Major hauls the colony's dead
/// to the Graveyard (communal duty, dig work still takes priority — the
/// outline's "workers" move the dead, and Majors are the ones with idle
/// capacity by design). With no dig work AND no burial work it is simply
/// idle — guard behavior is deliberately absent (the deferred Soldier's job).
///
/// Burial safety net (the Phase 9 emergency-dump pattern): every leg of a
/// burial has an attempt budget. An unreachable corpse is released and
/// retried later (never chased forever); an unreachable graveyard means the
/// carried corpse is laid down WHERE THE HAULER STANDS (an emergency burial,
/// tracked separately) — a blocked route can never deadlock the hauler or
/// lose the corpse.
/// </summary>
public sealed class Major
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public CellMaterial? Carrying => _dig.Carrying;

    /// <summary>Phase 18 Part C: tick at which this worker dies of old age
    /// (int.MaxValue = death disabled). Assigned by Colony.Spawn.</summary>
    public int DiesAtTick { get; init; } = int.MaxValue;

    /// <summary>True while hauling a corpse to the Graveyard.</summary>
    public bool CarryingCorpse { get; private set; }

    private readonly DigAssist _dig = new();
    private PathWalker? _burialWalker;
    private Colony.Corpse? _corpseTarget;
    private int _burialLegTicks;

    /// <summary>Ticks a burial leg may take before the safety net fires.
    /// INVENTED: generous vs. measured cross-world walks (~400 cells max
    /// route at 1 cell/tick), so it only fires on genuinely blocked routes.</summary>
    public const int BurialLegBudgetTicks = 2_000;

    /// <summary>Cooldown before an abandoned corpse is retried.</summary>
    public const int CorpseRetryCooldownTicks = 5_000;

    public Major(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void Tick(Colony colony, Grid grid)
    {
        int x = X, y = Y;
        // Dig work preempts burial-target ACQUISITION, but a carried corpse
        // is always delivered first (same finish-what-you-carry discipline
        // as DigAssist's spoil rule) — otherwise a corpse could be held
        // indefinitely while a long dig runs.
        if (!CarryingCorpse && _dig.Tick(colony, grid, ref x, ref y, wantDig: true))
        {
            (X, Y) = (x, y);
            AbandonBurialTarget(); // dig took over: release any claimed corpse
            return;
        }

        if (TickBurial(colony, grid)) return;

        // No dig site, no burial work, no leftover spoil: idle — no guard
        // stance, no other caste's work.
    }

    private bool TickBurial(Colony colony, Grid grid)
    {
        if (CarryingCorpse)
        {
            if (colony.BurialSite is not { } site)
            {
                // Graveyard vanished mid-haul (can't currently happen — rooms
                // are never removed — but never hold a corpse hostage on it).
                colony.BuryRemains(X, Y, emergency: true);
                CarryingCorpse = false;
                ResetBurialState();
                return true;
            }
            _burialWalker ??= new PathWalker(X, Y);
            bool arrived = _burialWalker.MoveTowards(grid, site);
            (X, Y) = (_burialWalker.X, _burialWalker.Y);
            if (arrived)
            {
                colony.BuryRemains(X, Y);
                CarryingCorpse = false;
                ResetBurialState();
            }
            else if (++_burialLegTicks > BurialLegBudgetTicks)
            {
                // Emergency lay-down: route to the graveyard is blocked.
                colony.BuryRemains(X, Y, emergency: true);
                CarryingCorpse = false;
                ResetBurialState();
            }
            return true;
        }

        if (colony.BurialSite is null) return false; // no graveyard yet
        if (_corpseTarget is null)
        {
            _corpseTarget = colony.ClaimNearestCorpse(X, Y);
            if (_corpseTarget is null) return false; // nothing to bury
            _burialWalker = new PathWalker(X, Y);
            _burialLegTicks = 0;
        }

        bool atCorpse = _burialWalker!.MoveTowards(grid, (_corpseTarget.X, _corpseTarget.Y));
        (X, Y) = (_burialWalker.X, _burialWalker.Y);
        if (atCorpse)
        {
            colony.Corpses.Remove(_corpseTarget);
            CarryingCorpse = true;
            _corpseTarget = null;
            _burialWalker = new PathWalker(X, Y);
            _burialLegTicks = 0;
        }
        else if (++_burialLegTicks > BurialLegBudgetTicks)
        {
            // Corpse unreachable right now: release it for a later retry.
            _corpseTarget.Claimed = false;
            _corpseTarget.NextAttemptTick = colony.TickCount + CorpseRetryCooldownTicks;
            ResetBurialState();
        }
        return true;
    }

    private void AbandonBurialTarget()
    {
        if (_corpseTarget is not null)
        {
            _corpseTarget.Claimed = false;
            _corpseTarget = null;
        }
        _burialWalker = null;
        _burialLegTicks = 0;
    }

    private void ResetBurialState()
    {
        _corpseTarget = null;
        _burialWalker = null;
        _burialLegTicks = 0;
    }

    /// <summary>Phase 18 Part C: called by the colony when this worker dies —
    /// releases dig claims, drops carried spoil (conservation), and if she
    /// was hauling a corpse, lays it down where she fell (a corpse can never
    /// vanish inside a dead hauler).</summary>
    public void OnDeath(Colony colony)
    {
        _dig.AbandonOnDeath(colony, X, Y);
        if (_corpseTarget is not null) { _corpseTarget.Claimed = false; }
        if (CarryingCorpse)
        {
            colony.BuryRemains(X, Y, emergency: true);
            CarryingCorpse = false;
        }
    }
}
