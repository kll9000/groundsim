namespace GroundSim;

// Phase 18 Part A (new outline): Tender split into Minim (tends eggs) and
// Gardener (processes fungus) — separate rolls from egg maturation, no
// caste-maturation mechanic, per Kevin's decision.
public enum Caste { Minim, Gardener, Forager, Major }

public sealed class ColonyStats
{
    /// <summary>Raw material deposited home by Foragers — the ONLY legal
    /// source of raw material. Role purity: node depletion must equal this.</summary>
    public double RawGatheredByForagers { get; set; }

    /// <summary>Raw converted to farmed by Gardeners (Phase 18: was Tenders)
    /// — the ONLY legal source of farmed resource besides the queen's
    /// one-time starter deposit.</summary>
    public double RawProcessedByGardeners { get; set; }

    /// <summary>Of the processed total, how much completed while the Gardener
    /// was physically standing inside an excavated Fungus Garden.</summary>
    public double ProcessedInGarden { get; set; }

    /// <summary>Times the garden processing-site provider had to fall back
    /// to the Home Room because the garden had no usable air cell at that
    /// instant (Phase 12.5 accounting — burial fallback must never be a
    /// silent permanent state).</summary>
    public long ProcessingFellBackToHome { get; set; }

    // ---- Phase 18 Part C: death/burial accounting. Conservation invariant
    // (tested): Deaths == Corpses-in-world + corpses-in-jaws + Burials. ----

    /// <summary>Workers that have died (Queen exempt).</summary>
    public int Deaths { get; set; }

    /// <summary>Corpses laid to rest as Remains material (includes
    /// emergency lay-downs, counted separately below).</summary>
    public int Burials { get; set; }

    /// <summary>Of the burials, how many were the emergency safety net
    /// (hauler couldn't reach the Graveyard within its attempt budget and
    /// laid the corpse down where it stood — the Phase 9 emergency-dump
    /// pattern, so a blocked route can never deadlock a hauler).</summary>
    public int EmergencyBurials { get; set; }

    /// <summary>Raw material in a Forager's jaws at her death — accounted
    /// (not silently lost) so gather-pipeline audits can close their books.</summary>
    public double RawLostToDeaths { get; set; }
}

/// <summary>Tick numbers at which colony-stage milestones occurred (null = not yet).</summary>
public sealed class ColonyMilestones
{
    public int? HomeFoundedTick { get; set; }
    public int? FirstWorkerTick { get; set; }
    public int? GardenTriggeredTick { get; set; }
    public int? GardenExcavatedTick { get; set; }
    public int? NurseryTriggeredTick { get; set; }
    public int? NurseryExcavatedTick { get; set; }
}

public readonly record struct OffspringOutcome(bool Survived, Caste Caste);

/// <summary>The outline's colony stages 1–4 (5–6 are deferred scope).</summary>
public enum ColonyStage { Founding, FirstBrood, Establishment, Expansion }

/// <summary>
/// The colony data model: queen, castes, eggs, resources, first-class rooms
/// with trigger conditions, and per-tick orchestration. Resources are
/// colony-level scalars, matching Colony Builder. Headless and rendering-free.
/// </summary>
public sealed class Colony
{
    public ColonyConfig Config { get; }
    public Grid Grid { get; }
    public Simulation Sim { get; }

    public double RawMaterial { get; set; }
    public double FarmedResource { get; set; }

    public Queen Queen { get; private set; }
    public List<Egg> Eggs { get; } = new();
    public List<Minim> Minims { get; } = new();
    public List<Gardener> Gardeners { get; } = new();
    public List<Forager> Foragers { get; } = new();
    public List<Major> Majors { get; } = new();

    /// <summary>Total living workers across all castes (the population that
    /// gates population-locked caste rolls, Phase 18).</summary>
    public int WorkerCount => Minims.Count + Gardeners.Count + Foragers.Count + Majors.Count;
    public List<ResourceNode> Nodes { get; } = new();
    public ColonyStats Stats { get; } = new();
    public ColonyMilestones Milestones { get; } = new();
    public int TotalEggsLaid { get; private set; }
    public int TickCount { get; private set; }

    /// <summary>Rooms, Home first. Rooms[0] always exists.</summary>
    public List<Room> Rooms { get; } = new();

    /// <summary>Integral of egg count over ticks since founding — the
    /// Nursery's brood-pressure trigger accumulator.</summary>
    public double BroodPressure { get; private set; }

    /// <summary>Where excavation is currently happening (Majors and assigned
    /// Foragers dig here). Null when nothing is being dug. Room triggers set
    /// this automatically; tests may set it manually. Phase 11: a DigSite
    /// (arbitrary cell mask — tunnel + chamber), no longer a rect.</summary>
    public DigSite? ActiveDigSite { get; set; }

    /// <summary>Test override: when set, ALL spoil goes to this fixed column
    /// (pre-Phase-12 behavior). Null (default) = mound mode: deliveries
    /// alternate sides of the entrance at randomized offsets.</summary>
    public int? SpoilDropX { get; set; }

    /// <summary>The surface entrance column (shaft mouth for organic
    /// founding; chamber center for rect founding).</summary>
    public int EntranceX { get; }

    private readonly int _entranceHalfWidth;
    private readonly int _originalSurfaceY;
    private bool _spoilLeft;

    /// <summary>Per-delivery spoil drop column (Phase 12): alternates left/
    /// right of the entrance at (opening half-width + 1 + rand offset), so
    /// the existing falling-sand physics builds a symmetric mound around the
    /// hole the material came out of. Colony-wide — founding Queen, room
    /// crews, and Majors all target the same mound. Adaptive spread: columns
    /// already piled MoundMaxHeight above the original surface are skipped
    /// outward, so the mound widens instead of feeding its inner slope back
    /// down the shaft.</summary>
    public int NextSpoilDropX()
    {
        if (SpoilDropX is { } fixedX) return fixedX;
        _spoilLeft = !_spoilLeft;
        int baseOffset = _entranceHalfWidth + 1 + _rng.Next(Config.MoundDropRange);
        // Phase 15: outward-search span 40 → 40 × GridScale — a physical
        // spread distance; unscaled it would cap the mound at half its
        // intended physical width and pile too steeply at the shaft.
        int span = 40 * ColonyConfig.GridScale;
        for (int o = baseOffset; o < baseOffset + span; o++)
        {
            int x = Math.Clamp(EntranceX + (_spoilLeft ? -o : o), 1, Grid.Width - 2);
            int surf = 0;
            while (surf < Grid.Height && Grid.IsAir(x, surf)) surf++;
            if (_originalSurfaceY - surf < Config.MoundMaxHeight) return x;
        }
        return Math.Clamp(EntranceX + (_spoilLeft ? -(baseOffset + span) : baseOffset + span), 1, Grid.Width - 2);
    }

    /// <summary>Standing maintenance digs (Phase 12): every completed
    /// excavation — the founding shaft+chamber AND each room's tunnel+chamber
    /// — stays registered here. Mound spill that rolls back down the entrance
    /// re-plugs these passages, and no active room site covers them; without
    /// maintenance the colony's arteries close permanently (measured twice:
    /// first the shaft, then the garden's connecting tunnel — which froze
    /// processing at the garden forever). Maintenance preempts room digs.</summary>
    public List<DigSite> MaintenanceSites { get; } = new();

    public HashSet<(int X, int Y)> DigClaims { get; } = new();

    /// <summary>Where Gardeners process (Phase 18: was Tenders). Home Room center until the Fungus
    /// Garden excavates; OnRoomExcavated repoints it at the Garden.</summary>
    public Func<Colony, (int X, int Y)> ProcessingSiteProvider { get; set; } = c => c.HomeCenter;
    public (int X, int Y) ProcessingSite => ProcessingSiteProvider(this);

    /// <summary>Phase 18 Part B: where Foragers deposit raw material and
    /// Gardeners withdraw it. Home Room center until the Food-storage room
    /// excavates; OnRoomExcavated repoints it (with the same live-floor +
    /// Home-fallback resilience as the processing site).</summary>
    public Func<Colony, (int X, int Y)> RawDepositSiteProvider { get; set; } = c => c.HomeCenter;
    public (int X, int Y) RawDepositSite => RawDepositSiteProvider(this);

    /// <summary>True once the Food-storage room exists and is excavated —
    /// the flag that switches Forager deposit and Gardener withdrawal from
    /// the abstract at-home flow to the spatial storage-room flow.</summary>
    public bool FoodStorageActive => GetRoom(RoomType.FoodStorage) is { Excavated: true };

    /// <summary>Derived, read-only stage indicator (added in Phase 8 for the
    /// status display — no behavior reads it). Stage boundaries follow the
    /// outline: Founding until the Home Room exists, First Brood until the
    /// first worker matures, Establishment until the first purpose-built room
    /// triggers, Expansion from then on.</summary>
    public ColonyStage CurrentStage =>
        Milestones.HomeFoundedTick is null ? ColonyStage.Founding
        : Milestones.FirstWorkerTick is null ? ColonyStage.FirstBrood
        : Milestones.GardenTriggeredTick is null && Milestones.NurseryTriggeredTick is null
            ? ColonyStage.Establishment
        : ColonyStage.Expansion;

    public (int X0, int Y0, int X1, int Y1) HomeRoom => Rooms[0].Rect;

    /// <summary>The Home Room's current usable floor site (live-computed:
    /// mound spill can bury any fixed cell). Fixed colony sites live on room
    /// FLOORS — with terrain-following movement a mid-air "center" would be
    /// a cell agents fall out of.</summary>
    public (int X, int Y) HomeCenter => Rooms[0].FloorSite(Grid);

    private readonly Random _rng;

    private Colony(Grid grid, Simulation sim, ColonyConfig config,
        Room homeRoom, Queen queen, int seed, int entranceX, int entranceHalfWidth,
        int originalSurfaceY, DigSite? homeMaintenanceSite)
    {
        Grid = grid;
        Sim = sim;
        Config = config;
        Rooms.Add(homeRoom);
        Queen = queen;
        _rng = new Random(seed);
        EntranceX = entranceX;
        _entranceHalfWidth = entranceHalfWidth;
        _originalSurfaceY = originalSurfaceY;
        if (homeMaintenanceSite is not null) MaintenanceSites.Add(homeMaintenanceSite);
    }

    /// <summary>A founding colony (Phase 12): the queen excavates an
    /// entrance shaft + organic home chamber below entranceX (planner
    /// fallback: the old simple rect), then settles.</summary>
    public static Colony Found(Grid grid, Simulation sim, ColonyConfig config,
        int entranceX, int seed = 1234)
    {
        var plan = OrganicPlanner.PlanFounding(grid, entranceX, config, new Random(seed));
        var queen = new Queen(plan.Site, plan.HomeRoom.FloorCenter, plan.Entrance.X, plan.Entrance.Y);
        return new Colony(grid, sim, config, plan.HomeRoom, queen, seed,
            plan.Entrance.X, plan.EntranceHalfWidth,
            originalSurfaceY: plan.Entrance.Y + 1, homeMaintenanceSite: plan.Site);
    }

    /// <summary>Rect-founding overload (the fallback shape, dug for real) —
    /// kept for regression tests of the founding state machine.</summary>
    public static Colony Found(Grid grid, Simulation sim, ColonyConfig config,
        (int X0, int Y0, int X1, int Y1) chamber, int startX, int startY, int seed = 1234)
    {
        var room = new Room(RoomType.Home, chamber.X0, chamber.Y0, chamber.X1, chamber.Y1);
        var rectCells = new HashSet<(int, int)>(room.Cells);
        OrganicPlanner.AddChimney(rectCells, grid, (chamber.X0 + chamber.X1) / 2, chamber.Y0);
        var site = new DigSite(rectCells);
        var queen = new Queen(site,
            ((chamber.X0 + chamber.X1) / 2, chamber.Y1), startX, startY);
        return new Colony(grid, sim, config, room, queen, seed,
            entranceX: (chamber.X0 + chamber.X1) / 2,
            entranceHalfWidth: (chamber.X1 - chamber.X0) / 2 + 1,
            originalSurfaceY: chamber.Y0, homeMaintenanceSite: site);
    }

    /// <summary>An already-founded colony (chamber carved instantly, starter
    /// deposited, queen laying) — for tests and phases that don't need to
    /// replay the founding sequence.</summary>
    public static Colony CreateFounded(Grid grid, Simulation sim, ColonyConfig config,
        (int X0, int Y0, int X1, int Y1) chamber, int seed = 1234)
    {
        for (int y = chamber.Y0; y <= chamber.Y1; y++)
        {
            for (int x = chamber.X0; x <= chamber.X1; x++)
            {
                grid[x, y] = CellMaterial.Air;
            }
        }
        // Queen rests on the chamber floor (Phase 9 terrain-following).
        var room = new Room(RoomType.Home, chamber.X0, chamber.Y0, chamber.X1, chamber.Y1, excavated: true);
        var colony = new Colony(grid, sim, config, room,
            new Queen((chamber.X0 + chamber.X1) / 2, chamber.Y1), seed,
            entranceX: (chamber.X0 + chamber.X1) / 2,
            entranceHalfWidth: (chamber.X1 - chamber.X0) / 2 + 1,
            originalSurfaceY: chamber.Y0,
            homeMaintenanceSite: MaintenanceSiteForRect(grid, chamber));
        colony.FarmedResource = config.StarterResource;
        colony.Milestones.HomeFoundedTick = 0;
        return colony;
    }

    public void Tick()
    {
        TickCount++;

        foreach (var node in Nodes) node.Regenerate(Config.NodeRegenPerTick);

        UpdateRoomTriggers();
        ManageExcavation();
        AssignDiggers();

        Queen.Tick(this);
        foreach (var m in Minims) m.Tick(this, Grid);
        foreach (var g in Gardeners) g.Tick(this, Grid);
        foreach (var f in Foragers) f.Tick(this, Grid);
        foreach (var m in Majors) m.Tick(this, Grid);

        for (int i = Eggs.Count - 1; i >= 0; i--)
        {
            var egg = Eggs[i];
            egg.Tick(Config.TendedMaturationSpeed);
            if (!egg.IsMature) continue;
            Eggs.RemoveAt(i);
            var outcome = RollOffspring();
            if (outcome.Survived) Spawn(outcome.Caste, egg.X, egg.Y);
        }

        ReapDeadWorkers();
    }

    // ---- Phase 18 Part C: worker death (the Queen is EXEMPT — her death
    // and the outline's succession detail are explicitly deferred scope) ----

    /// <summary>Unburied dead workers: where each fell, plus a retry gate so
    /// an unreachable corpse is periodically re-attempted instead of either
    /// stalling a hauler forever or being silently forgotten.</summary>
    public sealed class Corpse
    {
        public int X { get; init; }
        public int Y { get; init; }
        public int NextAttemptTick { get; set; }
        public bool Claimed { get; set; }
    }

    public List<Corpse> Corpses { get; } = new();

    private void ReapDeadWorkers()
    {
        if (Config.WorkerLifespanMeanTicks <= 0) return; // death disabled

        void Die(int x, int y)
        {
            Corpses.Add(new Corpse { X = x, Y = y });
            Stats.Deaths++;
        }
        for (int i = Minims.Count - 1; i >= 0; i--)
        {
            if (TickCount < Minims[i].DiesAtTick) continue;
            Die(Minims[i].X, Minims[i].Y);
            Minims.RemoveAt(i);
        }
        for (int i = Gardeners.Count - 1; i >= 0; i--)
        {
            if (TickCount < Gardeners[i].DiesAtTick) continue;
            var g = Gardeners[i];
            // Conservation: a withdrawn-but-unprocessed raw unit goes back
            // to the colony pool rather than vanishing.
            if (g.CarryingRaw) RawMaterial += 1;
            Die(g.X, g.Y);
            Gardeners.RemoveAt(i);
        }
        for (int i = Foragers.Count - 1; i >= 0; i--)
        {
            if (TickCount < Foragers[i].DiesAtTick) continue;
            var f = Foragers[i];
            // Carried raw dies with her (leaves are dropped where she fell,
            // abstractly) — tracked, not silently lost.
            Stats.RawLostToDeaths += f.Carrying;
            f.OnDeath(this);
            Die(f.X, f.Y);
            Foragers.RemoveAt(i);
        }
        for (int i = Majors.Count - 1; i >= 0; i--)
        {
            if (TickCount < Majors[i].DiesAtTick) continue;
            Majors[i].OnDeath(this);
            Die(Majors[i].X, Majors[i].Y);
            Majors.RemoveAt(i);
        }
    }

    /// <summary>Claims the nearest unclaimed, retry-eligible corpse for a
    /// hauler (one hauler per corpse — same claim discipline as dig cells).</summary>
    public Corpse? ClaimNearestCorpse(int x, int y)
    {
        Corpse? best = null;
        int bestDist = int.MaxValue;
        foreach (var c in Corpses)
        {
            if (c.Claimed || TickCount < c.NextAttemptTick) continue;
            int d = Math.Abs(c.X - x) + Math.Abs(c.Y - y);
            if (d < bestDist) { bestDist = d; best = c; }
        }
        if (best is not null) best.Claimed = true;
        return best;
    }

    /// <summary>The Graveyard's live burial spot (its floor site), or null
    /// until the room is excavated.</summary>
    public (int X, int Y)? BurialSite =>
        GetRoom(RoomType.Graveyard) is { Excavated: true } g ? g.FloorSite(Grid) : null;

    /// <summary>A carried corpse is laid to rest: dropped as a Remains
    /// particle (settles under normal physics) and counted. Used both for
    /// real graveyard burials and for the emergency lay-down safety net.</summary>
    public void BuryRemains(int x, int y, bool emergency = false)
    {
        Sim.Drop(x, y, CellMaterial.Remains);
        Stats.Burials++;
        if (emergency) Stats.EmergencyBurials++;
    }

    // ---- rooms & triggers ----

    public Room? GetRoom(RoomType type) => Rooms.FirstOrDefault(r => r.Type == type);

    private static DigSite MaintenanceSiteForRect(Grid grid, (int X0, int Y0, int X1, int Y1) chamber)
    {
        var cells = new HashSet<(int, int)>(DigSite.FromRect(chamber.X0, chamber.Y0, chamber.X1, chamber.Y1).Cells);
        OrganicPlanner.AddChimney(cells, grid, (chamber.X0 + chamber.X1) / 2, chamber.Y0);
        return new DigSite(cells);
    }

    private void UpdateRoomTriggers()
    {
        if (!Rooms[0].Excavated) return; // nothing triggers before founding completes

        BroodPressure += Eggs.Count; // integral of brood over time, not an instantaneous count

        if (GetRoom(RoomType.Garden) is null && FarmedResource >= Config.GardenTriggerThreshold)
        {
            QueueRoomPlan(RoomType.Garden);
            Milestones.GardenTriggeredTick ??= TickCount;
        }

        if (GetRoom(RoomType.Nursery) is null && BroodPressure >= Config.NurseryBroodPressureThreshold)
        {
            QueueRoomPlan(RoomType.Nursery);
            Milestones.NurseryTriggeredTick ??= TickCount;
        }

        // Phase 18 Part B: Food-storage triggers once a real foraging
        // economy exists; the Graveyard triggers on the first death (a
        // corpse needs somewhere to go).
        if (GetRoom(RoomType.FoodStorage) is null
            && Stats.RawGatheredByForagers >= Config.FoodStorageTriggerThreshold)
        {
            QueueRoomPlan(RoomType.FoodStorage);
        }
        if (GetRoom(RoomType.Graveyard) is null && Stats.Deaths >= 1)
        {
            QueueRoomPlan(RoomType.Graveyard);
        }

        ProcessRoomPlanQueue();
    }

    // ---- Phase 18.5: serialized room planning. Verification measured the
    // Nursery's rect-fallback rate jumping 3.5% → 86% once Food-storage
    // became a fourth competitor for the downward cone under Home: rooms
    // whose triggers fire while another room's PLANNED site still occupies
    // the cone must dodge it, and with three independent triggers racing,
    // usually can't. Fix: triggers LATCH immediately (milestones unchanged)
    // but planning is queued FIFO and released only when no other non-Home
    // room is planned-but-unexcavated — excavation was already serialized
    // (one ActiveDigSite), so waiting to PLAN costs almost nothing, and a
    // room planned after its predecessor excavates sees a clear cone (and
    // the Nursery usually gets its intended Phase 12 garden parent back).
    // Bounded wait: after MaxRoomPlanDeferralTicks in the queue the room
    // plans anyway (the pre-18.5 contention behavior as the fallback), so
    // a satisfied trigger can never starve behind a stuck dig. ----

    private readonly Queue<RoomType> _roomPlanQueue = new();
    private readonly Dictionary<RoomType, int> _roomQueuedAtTick = new();

    private void QueueRoomPlan(RoomType type)
    {
        if (_roomQueuedAtTick.ContainsKey(type)) return; // already queued
        _roomPlanQueue.Enqueue(type);
        _roomQueuedAtTick[type] = TickCount;
    }

    private void ProcessRoomPlanQueue()
    {
        if (_roomPlanQueue.Count == 0) return;
        bool coneBusy = Rooms.Any(r => !r.Excavated && r.Type != RoomType.Home);
        bool overdue = TickCount - _roomQueuedAtTick[_roomPlanQueue.Peek()] > Config.MaxRoomPlanDeferralTicks;
        if (coneBusy && !overdue) return;
        var type = _roomPlanQueue.Dequeue();
        _roomQueuedAtTick.Remove(type);
        AddPlannedRoom(type);
    }

    /// <summary>Phase 11: rooms are organic chambers at real distance,
    /// connected by winding tunnels. Tiered branching: the Garden branches
    /// from Home; the Nursery branches from the Garden once it's excavated
    /// (deeper tiers), else from Home. The planner's hardened fallback
    /// guarantees a valid dig plan always comes back.</summary>
    private void AddPlannedRoom(RoomType type)
    {
        var parent = type == RoomType.Nursery && GetRoom(RoomType.Garden) is { Excavated: true } garden
            ? garden
            : Rooms[0];
        var plan = OrganicPlanner.Plan(Grid, Rooms, parent, type, Config, _rng);
        plan.Room.PendingDig = plan.Site;
        Rooms.Add(plan.Room);
    }

    private void ManageExcavation()
    {
        // Maintenance preempts room digs: spill re-plugging any completed
        // passage (entrance shaft, room tunnels) must be cleared or the
        // colony's arteries close permanently (see MaintenanceSites).
        if (Rooms[0].Excavated)
        {
            var blocked = MaintenanceSites.FirstOrDefault(s => s.HasRemainingDiggable(Grid));
            if (blocked is not null && !ReferenceEquals(ActiveDigSite, blocked))
            {
                ActiveDigSite = blocked;
                return;
            }
        }
        if (ActiveDigSite is not null && MaintenanceSites.Contains(ActiveDigSite))
        {
            if (!ActiveDigSite.HasRemainingDiggable(Grid)) ActiveDigSite = null;
            return;
        }

        if (ActiveDigSite is null)
        {
            var pending = Rooms.FirstOrDefault(r => !r.Excavated && r.Type != RoomType.Home && r.PendingDig is not null);
            if (pending is not null) ActiveDigSite = pending.PendingDig;
            return;
        }

        var digging = Rooms.FirstOrDefault(r => !r.Excavated && ReferenceEquals(r.PendingDig, ActiveDigSite));
        // Phase 12.5: completion requires the site to be substantially OPEN,
        // not merely frontier-empty — a just-planned site whose single air
        // junction is transiently buried by spill has no accessible frontier
        // for a tick and was being marked excavated with ZERO cells dug
        // (measured: 3 of 10 seeds shipped born-dead gardens). A frontier-
        // empty-but-closed site simply stays active; maintenance re-opens
        // the junction and digging resumes.
        if (digging is not null && !ActiveDigSite.HasRemainingDiggable(Grid)
            && ActiveDigSite.AirFraction(Grid) >= 0.3)
        {
            digging.Excavated = true;
            // The room's dig site (tunnel + chamber) becomes a standing
            // maintenance responsibility from here on.
            MaintenanceSites.Add(ActiveDigSite);
            digging.PendingDig = null;
            ActiveDigSite = null;
            OnRoomExcavated(digging);
        }
    }

    private void OnRoomExcavated(Room room)
    {
        switch (room.Type)
        {
            case RoomType.Garden:
                // The Phase 6 seam doing its job: relocate processing with
                // zero Gardener code changes. Live floor site (Phase 12): the
                // fixed floor-center can be buried by mound spill settling
                // inside the chamber, which froze processing permanently.
                // Resilience: if the garden currently has NO usable air cell,
                // fall back to the Home Room rather than stalling the economy.
                ProcessingSiteProvider = c =>
                {
                    var s = room.FloorSite(c.Grid);
                    if (c.Grid.IsAir(s.X, s.Y)) return s;
                    // Tracked defense-in-depth (Phase 12.5): should be rare
                    // transients only; the e2e asserts this stays bounded.
                    c.Stats.ProcessingFellBackToHome++;
                    return c.HomeCenter;
                };
                Milestones.GardenExcavatedTick ??= TickCount;
                break;
            case RoomType.Nursery:
                Milestones.NurseryExcavatedTick ??= TickCount;
                break;
            case RoomType.FoodStorage:
                // Phase 18 Part B: the resource flow becomes spatial —
                // Foragers deposit here, Gardeners withdraw from here. Same
                // live-floor-site + Home fallback resilience as the garden.
                RawDepositSiteProvider = c =>
                {
                    var s = room.FloorSite(c.Grid);
                    return c.Grid.IsAir(s.X, s.Y) ? s : c.HomeCenter;
                };
                break;
                // Graveyard needs no site provider — BurialSite reads its
                // live floor directly.
        }
    }

    /// <summary>Test/setup helper: carve a room instantly, register it
    /// excavated, and apply its excavation effects.</summary>
    public Room AddExcavatedRoom(RoomType type, (int X0, int Y0, int X1, int Y1) rect)
    {
        for (int y = rect.Y0; y <= rect.Y1; y++)
        {
            for (int x = rect.X0; x <= rect.X1; x++)
            {
                Grid[x, y] = CellMaterial.Air;
            }
        }
        var room = new Room(type, rect.X0, rect.Y0, rect.X1, rect.Y1, excavated: true);
        Rooms.Add(room);
        OnRoomExcavated(room);
        return room;
    }

    /// <summary>Called by the queen when the founding excavation completes.</summary>
    public void NotifyHomeFounded()
    {
        Rooms[0].Excavated = true;
        Milestones.HomeFoundedTick ??= TickCount;
    }

    /// <summary>Communal excavation: up to WorkerDiggers idle Foragers assist
    /// the active dig (Majors always assist on their own). Rooms must be
    /// diggable before any Major exists — the outline has rooms in stage 4,
    /// Majors as a stage-5 phenomenon.</summary>
    private void AssignDiggers()
    {
        foreach (var f in Foragers) f.AssignedToDig = false;
        if (ActiveDigSite is null) return;
        int assigned = 0;
        foreach (var f in Foragers)
        {
            if (assigned >= Config.WorkerDiggers) break;
            if (f.Carrying == 0)
            {
                f.AssignedToDig = true;
                assigned++;
            }
        }
    }

    // ---- eggs & offspring ----

    /// <summary>Called by the laying queen on her cadence. Eggs go to the
    /// Nursery once it exists; the Home Room before that.</summary>
    public void LayEgg()
    {
        var room = GetRoom(RoomType.Nursery) is { Excavated: true } nursery ? nursery : Rooms[0];
        var (x, y) = RandomRestingCellIn(room);
        if (!Grid.IsAir(x, y)) (x, y) = HomeCenter; // room unusable: lay at home
        Eggs.Add(new Egg(x, y, Config.EggMaturationTicks));
        TotalEggsLaid++;
    }

    /// <summary>A random SUPPORTED air cell of the room (floor-resting) —
    /// organic-shape aware; falls back to the room's floor center.</summary>
    private (int X, int Y) RandomRestingCellIn(Room room)
    {
        var cells = room.Cells;
        (int, int)? anyAir = null;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var (x, y) = cells.ElementAt(_rng.Next(cells.Count));
            if (!Grid.IsAir(x, y)) continue;
            if (!Grid.IsAir(x, y + 1)) return (x, y); // resting on solid
            anyAir ??= (x, y);
        }
        return anyAir ?? room.FloorCenter;
    }

    /// <summary>
    /// Survival + caste rolls for one matured egg. Rarity-ordered, rarest
    /// first: Major roll, then Forager, then the caregiver split
    /// Gardener-vs-Minim with Minim as the most-common default. Phase 18:
    /// the Gardener roll is population-gated (the outline ties Gardener
    /// appearance to the operation scaling up; below the gate the roll
    /// falls through to Minim, so the earliest colony is Minim-heavy,
    /// matching the outline's "first and smallest workers"). (Soldier and
    /// New Queen rolls are deferred and deliberately absent.)
    /// </summary>
    public OffspringOutcome RollOffspring()
    {
        if (_rng.NextDouble() >= Config.EggSurvivalChance)
        {
            return new OffspringOutcome(false, default);
        }
        if (_rng.NextDouble() < Config.MajorChance)
        {
            return new OffspringOutcome(true, Caste.Major);
        }
        if (_rng.NextDouble() < Config.ForagerShareOfRemainder)
        {
            return new OffspringOutcome(true, Caste.Forager);
        }
        bool gardener = WorkerCount >= Config.GardenerUnlockPopulation
            && _rng.NextDouble() < Config.GardenerShareOfCaregivers;
        return new OffspringOutcome(true, gardener ? Caste.Gardener : Caste.Minim);
    }

    public void Spawn(Caste caste, int x, int y)
    {
        if (!Grid.IsAir(x, y)) (x, y) = HomeCenter;
        // Phase 18 Part C: each worker gets a lifespan at birth (Queen
        // exempt — she is not spawned through here and never dies).
        int diesAt = Config.WorkerLifespanMeanTicks <= 0
            ? int.MaxValue
            : TickCount + Config.WorkerLifespanMeanTicks
              + _rng.Next(-Config.WorkerLifespanJitterTicks, Config.WorkerLifespanJitterTicks + 1);
        switch (caste)
        {
            case Caste.Minim: Minims.Add(new Minim(x, y) { DiesAtTick = diesAt }); break;
            case Caste.Gardener: Gardeners.Add(new Gardener(x, y) { DiesAtTick = diesAt }); break;
            case Caste.Forager: Foragers.Add(new Forager(x, y) { DiesAtTick = diesAt }); break;
            case Caste.Major: Majors.Add(new Major(x, y) { DiesAtTick = diesAt }); break;
        }
        Milestones.FirstWorkerTick ??= TickCount;
    }

    public Egg? NearestEgg(int x, int y)
    {
        Egg? best = null;
        int bestDist = int.MaxValue;
        foreach (var egg in Eggs)
        {
            int d = Math.Abs(egg.X - x) + Math.Abs(egg.Y - y);
            if (d < bestDist) { bestDist = d; best = egg; }
        }
        return best;
    }

    public ResourceNode? NearestNodeWithMaterial(int x, int y)
    {
        ResourceNode? best = null;
        int bestDist = int.MaxValue;
        foreach (var node in Nodes)
        {
            if (node.Remaining <= 0) continue;
            int d = Math.Abs(node.X - x) + Math.Abs(node.Y - y);
            if (d < bestDist) { bestDist = d; best = node; }
        }
        return best;
    }

}
