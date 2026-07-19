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
}

public readonly record struct OffspringOutcome(bool Survived, Caste Caste);

/// <summary>
/// The colony data model: queen, castes, eggs, resources, and the per-tick
/// orchestration. Resources (raw / farmed) are colony-level scalars, matching
/// Colony Builder — not physical grid cells.
/// Rendering-free and headless, like everything else in the core project.
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
    public int TotalEggsLaid { get; private set; }

    /// <summary>The Home Room region (inclusive rect). In Phase 6 this is the
    /// queen's founding excavation; Phase 7 turns rooms into first-class
    /// labeled regions.</summary>
    public (int X0, int Y0, int X1, int Y1) HomeRoom { get; }

    /// <summary>Where excavation is currently happening (Majors assist here).
    /// Null when nothing is being dug. Phase 7's room-trigger logic will set
    /// this; Phase 6 sets it manually in tests.</summary>
    public (int X0, int Y0, int X1, int Y1)? ActiveDigSite { get; set; }

    public int SpoilDropX { get; set; }
    public HashSet<(int X, int Y)> DigClaims { get; } = new();

    /// <summary>Where Tenders process. Home Room center for now; a delegate so
    /// Phase 7 can point it at the Fungus Garden without touching Tender code.</summary>
    public Func<Colony, (int X, int Y)> ProcessingSiteProvider { get; set; } = c => c.HomeCenter;
    public (int X, int Y) ProcessingSite => ProcessingSiteProvider(this);

    public (int X, int Y) HomeCenter
        => ((HomeRoom.X0 + HomeRoom.X1) / 2, (HomeRoom.Y0 + HomeRoom.Y1) / 2);

    private readonly Random _rng;

    private Colony(Grid grid, Simulation sim, ColonyConfig config,
        (int X0, int Y0, int X1, int Y1) homeRoom, Queen queen, int seed)
    {
        Grid = grid;
        Sim = sim;
        Config = config;
        HomeRoom = homeRoom;
        Queen = queen;
        _rng = new Random(seed);
        SpoilDropX = Math.Max(0, homeRoom.X0 - 12);
    }

    /// <summary>A founding colony: the queen starts at (startX, startY) and
    /// excavates the home chamber herself before settling.</summary>
    public static Colony Found(Grid grid, Simulation sim, ColonyConfig config,
        (int X0, int Y0, int X1, int Y1) chamber, int startX, int startY, int seed = 1234)
    {
        int spoilDropX = Math.Max(0, chamber.X0 - 12);
        var queen = new Queen(grid, sim, chamber, startX, startY, spoilDropX);
        return new Colony(grid, sim, config, chamber, queen, seed);
    }

    /// <summary>An already-founded colony (chamber carved instantly, starter
    /// deposited, queen laying) — for tests and later phases that don't need
    /// to replay the founding sequence.</summary>
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
        var center = ((chamber.X0 + chamber.X1) / 2, (chamber.Y0 + chamber.Y1) / 2);
        var colony = new Colony(grid, sim, config, chamber, new Queen(center.Item1, center.Item2), seed);
        colony.FarmedResource = config.StarterResource;
        return colony;
    }

    public void Tick()
    {
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

    /// <summary>Called by the laying queen on her cadence.</summary>
    public void LayEgg()
    {
        var (x, y) = RandomAirCellInHomeRoom();
        Eggs.Add(new Egg(x, y, Config.EggMaturationTicks));
        TotalEggsLaid++;
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

    private (int X, int Y) RandomAirCellInHomeRoom()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            int x = _rng.Next(HomeRoom.X0, HomeRoom.X1 + 1);
            int y = _rng.Next(HomeRoom.Y0, HomeRoom.Y1 + 1);
            if (Grid.IsAir(x, y)) return (x, y);
        }
        return HomeCenter;
    }
}
