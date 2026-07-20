namespace GroundSim;

public enum Caste { Tender, Forager, Major }

public sealed class ColonyStats
{
    /// <summary>Raw material deposited home by Foragers — the ONLY legal
    /// source of raw material. Role purity: node depletion must equal this.</summary>
    public double RawGatheredByForagers { get; set; }

    /// <summary>Raw converted to farmed by Tenders — the ONLY legal source of
    /// farmed resource besides the queen's one-time starter deposit.</summary>
    public double RawProcessedByTenders { get; set; }

    /// <summary>Of the processed total, how much completed while the Tender
    /// was physically standing inside an excavated Fungus Garden.</summary>
    public double ProcessedInGarden { get; set; }
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
    public List<Tender> Tenders { get; } = new();
    public List<Forager> Foragers { get; } = new();
    public List<Major> Majors { get; } = new();
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

    public int SpoilDropX { get; set; }
    public HashSet<(int X, int Y)> DigClaims { get; } = new();

    /// <summary>Where Tenders process. Home Room center until the Fungus
    /// Garden excavates; OnRoomExcavated repoints it at the Garden.</summary>
    public Func<Colony, (int X, int Y)> ProcessingSiteProvider { get; set; } = c => c.HomeCenter;
    public (int X, int Y) ProcessingSite => ProcessingSiteProvider(this);

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

    /// <summary>The Home Room's floor-center cell. Phase 9: fixed colony
    /// sites live on room FLOORS — with terrain-following movement a mid-air
    /// "center" would be a cell agents immediately fall out of.</summary>
    public (int X, int Y) HomeCenter => (Rooms[0].Center.X, Rooms[0].Y1);

    private readonly Random _rng;

    private Colony(Grid grid, Simulation sim, ColonyConfig config,
        (int X0, int Y0, int X1, int Y1) homeRect, bool homeExcavated, Queen queen, int seed)
    {
        Grid = grid;
        Sim = sim;
        Config = config;
        Rooms.Add(new Room(RoomType.Home, homeRect.X0, homeRect.Y0, homeRect.X1, homeRect.Y1, homeExcavated));
        Queen = queen;
        _rng = new Random(seed);
        SpoilDropX = Math.Min(grid.Width - 1, homeRect.X1 + 20);
    }

    /// <summary>A founding colony: the queen starts at (startX, startY) and
    /// excavates the home chamber herself before settling.</summary>
    public static Colony Found(Grid grid, Simulation sim, ColonyConfig config,
        (int X0, int Y0, int X1, int Y1) chamber, int startX, int startY, int seed = 1234)
    {
        int spoilDropX = Math.Min(grid.Width - 1, chamber.X1 + 20);
        var queen = new Queen(grid, sim, chamber, startX, startY, spoilDropX);
        return new Colony(grid, sim, config, chamber, homeExcavated: false, queen, seed);
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
        var colony = new Colony(grid, sim, config, chamber, homeExcavated: true,
            new Queen((chamber.X0 + chamber.X1) / 2, chamber.Y1), seed);
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
        foreach (var t in Tenders) t.Tick(this, Grid);
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
    }

    // ---- rooms & triggers ----

    public Room? GetRoom(RoomType type) => Rooms.FirstOrDefault(r => r.Type == type);

    private void UpdateRoomTriggers()
    {
        if (!Rooms[0].Excavated) return; // nothing triggers before founding completes

        BroodPressure += Eggs.Count; // integral of brood over time, not an instantaneous count

        if (GetRoom(RoomType.Garden) is null && FarmedResource >= Config.GardenTriggerThreshold)
        {
            AddPlannedRoom(RoomType.Garden);
            Milestones.GardenTriggeredTick ??= TickCount;
        }

        if (GetRoom(RoomType.Nursery) is null && BroodPressure >= Config.NurseryBroodPressureThreshold)
        {
            AddPlannedRoom(RoomType.Nursery);
            Milestones.NurseryTriggeredTick ??= TickCount;
        }
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
        if (ActiveDigSite is null)
        {
            var pending = Rooms.FirstOrDefault(r => !r.Excavated && r.Type != RoomType.Home && r.PendingDig is not null);
            if (pending is not null) ActiveDigSite = pending.PendingDig;
            return;
        }

        var digging = Rooms.FirstOrDefault(r => !r.Excavated && ReferenceEquals(r.PendingDig, ActiveDigSite));
        if (digging is not null && !ActiveDigSite.HasRemainingDiggable(Grid))
        {
            digging.Excavated = true;
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
                // zero Tender code changes. FloorCenter = the room's deepest
                // center-column cell (organic-shape aware, Phase 11).
                ProcessingSiteProvider = c => room.FloorCenter;
                Milestones.GardenExcavatedTick ??= TickCount;
                break;
            case RoomType.Nursery:
                Milestones.NurseryExcavatedTick ??= TickCount;
                break;
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
    /// first: Major roll, then Forager-vs-Tender with Tender as the
    /// most-common default. (Soldier and New Queen rolls are deferred and
    /// deliberately absent.)
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
        return new OffspringOutcome(true,
            _rng.NextDouble() < Config.ForagerShareOfRemainder ? Caste.Forager : Caste.Tender);
    }

    public void Spawn(Caste caste, int x, int y)
    {
        if (!Grid.IsAir(x, y)) (x, y) = HomeCenter;
        switch (caste)
        {
            case Caste.Tender: Tenders.Add(new Tender(x, y)); break;
            case Caste.Forager: Foragers.Add(new Forager(x, y)); break;
            case Caste.Major: Majors.Add(new Major(x, y)); break;
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
