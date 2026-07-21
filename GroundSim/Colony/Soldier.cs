namespace GroundSim;

/// <summary>
/// Phase 19 (new outline): the largest caste, replacing Major outright.
/// Inherits Major's two duties unchanged — excavation speed-boost (via
/// DigAssist, the same full Agent dig-carry-drop composition, so its spoil
/// is physically hauled and conserved like everyone else's) and corpse
/// hauling to the Graveyard (Phase 18 Part C, verified logic untouched) —
/// and adds guard POSITIONING as its resting posture: a Soldier with
/// nothing to dig and nothing to bury stands at the colony's guard post
/// (the entrance mouth). No combat, no threat detection, no enemies —
/// positioning only, per the standing Phase 5 scoping note.
///
/// Priority: dig > burial > guard. Reasoning: dig work is the colony's
/// time-critical expansion path and was always Major's first duty; burial
/// is finite, queued work with its own safety nets (Phase 18's
/// verification specifically confirmed dig-preempts-burial-acquisition
/// plus always-deliver-a-carried-corpse, and that logic transfers
/// unchanged); guarding is by definition the posture for a Soldier with
/// nothing else to do — it must never compete with real work, so it is
/// strictly the idle default, not a third competing priority.
/// </summary>
public sealed class Soldier
{
    public int X { get; private set; }
    public int Y { get; private set; }
    public CellMaterial? Carrying => _dig.Carrying;

    /// <summary>Phase 18 Part C: tick at which this worker dies of old age
    /// (int.MaxValue = death disabled). Assigned by Colony.Spawn.</summary>
    public int DiesAtTick { get; init; } = int.MaxValue;

    /// <summary>True while hauling a corpse to the Graveyard.</summary>
    public bool CarryingCorpse { get; private set; }

    /// <summary>True while standing at (or walking to) the guard post.</summary>
    public bool OnGuard { get; private set; }

    private readonly DigAssist _dig = new();
    private PathWalker? _burialWalker;
    private PathWalker? _guardWalker;
    private Colony.Corpse? _corpseTarget;
    private int _burialLegTicks;

    /// <summary>Ticks a burial leg may take before the safety net fires.
    /// INVENTED: generous vs. measured cross-world walks (~400 cells max
    /// route at 1 cell/tick), so it only fires on genuinely blocked routes.</summary>
    public const int BurialLegBudgetTicks = 2_000;

    /// <summary>Cooldown before an abandoned corpse is retried.</summary>
    public const int CorpseRetryCooldownTicks = 5_000;

    public Soldier(int x, int y)
    {
        X = x;
        Y = y;
    }

    public void Tick(Colony colony, Grid grid)
    {
        int x = X, y = Y;
        // Dig work preempts burial-target ACQUISITION and guarding, but a
        // carried corpse is always delivered first (same finish-what-you-
        // carry discipline as DigAssist's spoil rule).
        if (!CarryingCorpse && _dig.Tick(colony, grid, ref x, ref y, wantDig: true))
        {
            (X, Y) = (x, y);
            AbandonBurialTarget(); // dig took over: release any claimed corpse
            OnGuard = false;
            _guardWalker = null;
            return;
        }

        if (TickBurial(colony, grid))
        {
            OnGuard = false;
            _guardWalker = null;
            return;
        }

        // Nothing to dig, nothing to bury: take up the guard post.
        TickGuard(colony, grid);
    }

    private void TickGuard(Colony colony, Grid grid)
    {
        var post = colony.GuardPost;
        _guardWalker ??= new PathWalker(X, Y);
        OnGuard = _guardWalker.MoveTowards(grid, post) || (X, Y) == post;
        (X, Y) = (_guardWalker.X, _guardWalker.Y);
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
            // Phase 21: the corpse may have DECAYED while we walked — only
            // pick up what is genuinely still there, or the ledger would
            // count a phantom burial on top of the decay.
            if (colony.Corpses.Remove(_corpseTarget))
            {
                CarryingCorpse = true;
                _burialWalker = new PathWalker(X, Y);
                _burialLegTicks = 0;
            }
            _corpseTarget = null;
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
