using System.Diagnostics;
using GroundSim;

namespace GroundSim.Tests;

public class TerrainRuleTests
{
    [Fact]
    public void SurfaceOpen_And_Supported_ClassifyCorrectly()
    {
        var grid = new Grid(20, 20);
        for (int x = 0; x < 20; x++) grid[x, 15] = CellMaterial.Dirt; // ground at y=15
        grid[5, 10] = CellMaterial.Rock; // a roof cell over column 5

        Assert.True(Terrain.IsSurfaceOpen(grid, 10, 14));  // open sky above
        Assert.False(Terrain.IsSurfaceOpen(grid, 5, 12));  // under the roof: enclosed
        Assert.False(Terrain.IsSurfaceOpen(grid, 10, 15)); // solid, not air

        Assert.True(Terrain.IsSupported(grid, 10, 14));    // standing on ground
        Assert.False(Terrain.IsSupported(grid, 10, 8));    // free open air: falls
        // Phase 11 update: support is pure 3×3 contact. A cell two rows below
        // a roof has nothing to touch — no longer supported (the old
        // enclosed-branch behavior); the cell DIRECTLY under the roof clings
        // to the ceiling and is.
        Assert.False(Terrain.IsSupported(grid, 5, 12));
        Assert.True(Terrain.IsSupported(grid, 5, 11));     // ceiling cling
        // Wall-cling (the flagged deviation): surface-open but beside solid.
        grid[8, 8] = CellMaterial.Rock;
        Assert.True(Terrain.IsSupported(grid, 9, 8));
        Assert.True(Terrain.IsSupported(grid, 7, 8));
    }

    [Fact]
    public void WorldBorder_IsNotAClimbableWall_Phase20Regression()
    {
        // Phase 20: the old "out-of-bounds reads as wall" convention made
        // the world border a supported climbing highway (measured: foragers
        // strung along x=0 up to y=0 — Kevin's "ants on the window edge").
        // The border is open void: only REAL solid cells grant contact.
        var grid = new Grid(20, 20);
        for (int x = 0; x < 20; x++) grid[x, 15] = CellMaterial.Dirt; // floor

        // Mid-air on the left border column, no in-bounds solid in reach:
        // unsupported (falls), and the oracle agrees it's visibly floating.
        Assert.False(Terrain.IsSupported(grid, 0, 8), "border column must not support");
        Assert.True(Terrain.IsVisiblyFloating(grid, 0, 8), "oracle must call the bare border floating");
        // Same on the right border and the top row.
        Assert.False(Terrain.IsSupported(grid, 19, 8));
        Assert.False(Terrain.IsSupported(grid, 10, 0), "top row must not be a walkable ceiling");
        // Real contact at the border still works: standing on the floor.
        Assert.True(Terrain.IsSupported(grid, 0, 14), "floor contact at the border is real support");
        // Grid bottom remains supported by rule.
        Assert.True(Terrain.IsSupported(grid, 0, 19));
    }

    [Fact]
    public void OpenPitWalls_RemainClimbable()
    {
        // The exact geometry that motivated the wall-cling amendment: a dug
        // open pit. Interior wall-adjacent cells must stay climbable or
        // agents could never exit their own excavations.
        var grid = new Grid(20, 20);
        for (int y = 10; y < 20; y++)
        {
            for (int x = 0; x < 20; x++) grid[x, y] = CellMaterial.Dirt;
        }
        for (int y = 10; y < 16; y++) grid[8, y] = CellMaterial.Air; // vertical shaft, open top

        for (int y = 10; y < 16; y++)
        {
            Assert.True(Terrain.IsSurfaceOpen(grid, 8, y), $"shaft cell y={y} is under open sky");
            Assert.True(Terrain.IsSupported(grid, 8, y), $"shaft cell y={y} must be climbable (wall-cling)");
        }
    }
}

public class SurfaceMovementTests
{
    [Fact]
    public void UnsupportedSurfaceAgent_FallsExactlyOneCellPerTick_UntilGrounded()
    {
        var grid = new Grid(20, 20);
        for (int x = 0; x < 20; x++) grid[x, 15] = CellMaterial.Dirt;

        var walker = new PathWalker(10, 3); // high in open air, no walls near
        for (int expectedY = 4; expectedY <= 14; expectedY++)
        {
            walker.MoveTowards(grid, (10, 3)); // target is irrelevant while falling
            Assert.Equal((10, expectedY), (walker.X, walker.Y));
        }
        // Grounded now: no further falling.
        walker.MoveTowards(grid, (10, 14));
        Assert.Equal((10, 14), (walker.X, walker.Y));
    }

    [Fact]
    public void EnclosedAgent_StillClimbsFreely_Phase4Regression()
    {
        // Sealed cavity (roof intact): all-air interior must remain fully
        // traversable including "ceiling" cells, exactly as before Phase 9.
        var grid = new Grid(30, 30);
        for (int y = 10; y < 30; y++)
        {
            for (int x = 0; x < 30; x++) grid[x, y] = CellMaterial.Dirt;
        }
        for (int y = 12; y <= 18; y++)
        {
            for (int x = 5; x <= 25; x++) grid[x, y] = CellMaterial.Air; // cavity, roofed
        }

        var walker = new PathWalker(6, 18);
        var target = (X: 24, Y: 12); // far corner, just under the ceiling
        int guard = 0;
        while (!walker.MoveTowards(grid, target) && ++guard < 500) { }
        Assert.Equal(target, (walker.X, walker.Y));
    }

    [Fact]
    public void TunnelToSurfaceTransition_SwitchesModes_WithoutSticking()
    {
        // A roofed tunnel opens onto a cliff face above the valley floor: the
        // agent must walk out enclosed, then FALL to the valley floor once in
        // open air, then walk to the target — no oscillation, no floating.
        var grid = new Grid(30, 30);
        for (int y = 10; y < 30; y++)
        {
            for (int x = 0; x < 15; x++) grid[x, y] = CellMaterial.Dirt; // plateau (left)
        }
        for (int y = 20; y < 30; y++)
        {
            for (int x = 15; x < 30; x++) grid[x, y] = CellMaterial.Dirt; // valley floor (right)
        }
        for (int x = 5; x < 15; x++) grid[x, 14] = CellMaterial.Air; // roofed tunnel exiting the cliff at (14,14)

        var walker = new PathWalker(5, 14);
        var target = (X: 25, Y: 19); // on the valley floor
        var positions = new List<(int, int)>();
        int guard = 0;
        bool arrived = false;
        while (!arrived && ++guard < 800)
        {
            arrived = walker.MoveTowards(grid, target);
            positions.Add((walker.X, walker.Y));
            Assert.True(grid.IsAir(walker.X, walker.Y), "agent must never enter solid terrain");
        }
        Assert.True(arrived, "agent should reach the valley target");
        // No floating: once past the cliff lip in open air, it descended to
        // the floor; final position is supported by definition of arrival.
        Assert.True(Terrain.IsSupported(grid, walker.X, walker.Y));
    }

    [Fact]
    public void ColonyRun_NoWorkerEndsUpVisiblyFloating_ByIndependentOracle()
    {
        // Phase 9.5 origin; Phase 11.5 honest relabel: since the Phase 11
        // unification the oracle coincides with the rule, so this is no
        // longer an independent-PREDICATE check — but it remains a
        // meaningful BEHAVIORAL check (no agent persists in open-air cells;
        // movement must keep resolving falls). The primary independent net
        // is now TrajectoryAuditTests.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = Colony.Found(grid, sim, new ColonyConfig(),
            ColonyTestWorld.Chamber, startX: 112, startY: 59, seed: 5);
        colony.Nodes.Add(new ResourceNode(30, 59, 10_000));
        colony.Nodes.Add(new ResourceNode(210, 59, 10_000));

        ColonyTestWorld.Run(colony, sim, 12_000);

        // Sweep window: every worker must be non-floating (by the oracle) at
        // some point in the next 150 ticks — an agent can legitimately be
        // mid-fall at any single instant; PERSISTENT floating is the bug.
        var everGrounded = new bool[colony.WorkerCount];
        for (int t = 0; t < 150; t++)
        {
            int i = 0;
            foreach (var w in colony.Minims.Select(x => (x.X, x.Y))
                         .Concat(colony.Gardeners.Select(x => (x.X, x.Y)))
                .Concat(colony.Foragers.Select(x => (x.X, x.Y)))
                .Concat(colony.Soldiers.Select(x => (x.X, x.Y))))
            {
                if (i < everGrounded.Length && !Terrain.IsVisiblyFloating(grid, w.X, w.Y)) everGrounded[i] = true;
                i++;
            }
            colony.Tick();
            sim.Tick();
        }
        for (int i = 0; i < everGrounded.Length; i++)
        {
            Assert.True(everGrounded[i], $"worker #{i} was visibly floating for 150 consecutive ticks");
        }
    }

    [Fact]
    public void OracleAndProductionRule_AgreeOnDistantRoof_DivergenceResolved()
    {
        // History: Phase 9.5 documented this exact geometry as the one KNOWN
        // divergence between IsSupported (whose enclosed/roof branch called a
        // contact-free cell under a distant overhang "supported") and the
        // visual-floating oracle — harmless while all excavations were open
        // pits, and explicitly designated the marker for re-opening the
        // decision "if room shapes ever gain real roofs." Phase 11's organic
        // chambers ARE real roofed rooms, which made the divergence live, so
        // the rule was unified to pure 3×3 contact. This test now pins the
        // AGREEMENT (and remains meaningful because the two functions are
        // implemented independently).
        var grid = new Grid(20, 20);
        for (int x = 0; x < 20; x++) grid[x, 15] = CellMaterial.Dirt; // floor
        grid[10, 5] = CellMaterial.Rock; // overhang, seven rows above (10,12)

        Assert.False(Terrain.IsSupported(grid, 10, 12),
            "distant roof provides no contact — unsupported");
        Assert.True(Terrain.IsVisiblyFloating(grid, 10, 12));

        // Directly under the overhang: ceiling contact — both agree, again.
        Assert.True(Terrain.IsSupported(grid, 10, 6));
        Assert.False(Terrain.IsVisiblyFloating(grid, 10, 6));

        // One column over (no overhang): unchanged agreement.
        Assert.False(Terrain.IsSupported(grid, 11, 12));
        Assert.True(Terrain.IsVisiblyFloating(grid, 11, 12));
    }

    [Fact]
    public void SupportChecks_DoNotReintroduceGridScaleCost()
    {
        // Threshold rationale: support checks are O(column height) per agent
        // per tick, NOT per grid cell. Phase 15: run 8k → 60k ticks (the
        // finer grid founds ~8× slower, and the "did real work" guard below
        // needs workers gathering); limit 5 s → 30 s, scaled with the tick
        // count — still failing hard on an accidental per-tick O(grid)
        // sweep (28,800 cells × 60k ticks of even trivial per-cell work
        // busts it by orders of magnitude).
        var (grid, sim) = ColonyTestWorld.Create();
        // Phase 25: Forager gate zeroed — this is a movement-COST test whose
        // "did real work" guard needs gathering inside its 60k-tick window,
        // which the day-6 emergence gate would push entirely out of frame.
        var colony = Colony.Found(grid, sim, new ColonyConfig
        {
            ForagerMinEmergenceTick = 0,
        },
            ColonyTestWorld.Chamber, startX: 112, startY: 59, seed: 6);
        colony.Nodes.Add(new ResourceNode(30, 59, 10_000));
        colony.Nodes.Add(new ResourceNode(210, 59, 10_000));

        var sw = Stopwatch.StartNew();
        ColonyTestWorld.Run(colony, sim, 60_000);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 30_000,
            $"60000-tick colony run took {sw.ElapsedMilliseconds} ms (limit 30000 ms)");
        Assert.True(colony.Stats.RawGatheredByForagers > 0, "run should have done real work");
    }
}

public class ResourceSustainTests
{
    [Fact]
    public void DepletedNode_RegeneratesToCap_AndNoFurther()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim,
            new ColonyConfig { EggSurvivalChance = 0, NodeRegenPerTick = 0.5 });
        var node = new ResourceNode(80, 59, 10);
        colony.Nodes.Add(node);
        node.Remaining = 0;

        ColonyTestWorld.Run(colony, sim, 10);
        Assert.Equal(5.0, node.Remaining, 3); // 10 ticks x 0.5

        ColonyTestWorld.Run(colony, sim, 1000);
        Assert.Equal(node.Cap, node.Remaining, 3); // bounded at cap, not beyond
    }

    [Fact]
    public void Gathering_KeepsFlowing_LongAfterNodesWouldHaveDepleted()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        // Tiny caps: without regen these nodes are dry within a few trips.
        // Room triggers and egg-laying disabled — otherwise the long run
        // triggers the Nursery and drafts the Foragers into excavation duty,
        // which stalls gathering for reasons unrelated to resource sustain.
        var colony = ColonyTestWorld.Founded(grid, sim, new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            GardenTriggerThreshold = double.MaxValue,
            NurseryBroodPressureThreshold = double.MaxValue,
            // Phase 18: the Food-storage trigger would draft the foragers
            // into excavation exactly like the room triggers this test
            // already disables, and for the same documented reason.
            FoodStorageTriggerThreshold = double.MaxValue,
            NodeRegenPerTick = 0.05,
        });
        colony.Nodes.Add(new ResourceNode(60, 59, 25)); // Phase 15: ×GridScale, on the new surface
        colony.Nodes.Add(new ResourceNode(180, 59, 25));
        colony.Spawn(Caste.Forager, colony.HomeCenter.X, colony.HomeCenter.Y);
        colony.Spawn(Caste.Forager, colony.HomeCenter.X + 1, colony.HomeCenter.Y);

        ColonyTestWorld.Run(colony, sim, 8000);
        double midway = colony.Stats.RawGatheredByForagers;
        Assert.True(midway > 50, $"nodes should be well past their initial 50 total by now (gathered {midway})");

        ColonyTestWorld.Run(colony, sim, 8000);
        double final = colony.Stats.RawGatheredByForagers;

        // The colony does NOT flatline: meaningful gathering continued in the
        // second half, sustained purely by regeneration.
        Assert.True(final - midway > 100,
            $"gathering flatlined: only {final - midway:0.0} gathered in the second half");
    }
}
