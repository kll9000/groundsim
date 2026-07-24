using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 31: the Nectar pool in isolation — same synthetic-harness
/// discipline as Phase 27's trail math. Decay is verified against the
/// closed form (linear drain: rate × ticks), never as "some decrease
/// happened"; the zero floor, the population scaling, the ported-value
/// anchor, and the Gardener production hook each get their own pin.
/// No caste behavior beyond the production hook exists yet, by design.
/// </summary>
public class NectarTests
{
    /// <summary>Drain per tick for a config and worker count — the same
    /// closed form Colony.TickNectar implements. Written out here
    /// independently (from the ported spec: 0.18 mass/s ÷ 30 ticks/s,
    /// scaled by population over the reference), so the test checks the
    /// implementation against the SPEC, not against a call to itself.</summary>
    private static double DrainPerTick(ColonyConfig cfg, int workers) =>
        cfg.NectarDecayMassPerSecond / 30.0 * workers / cfg.NectarDecayReferencePopulation;

    [Fact]
    public void Decay_MatchesTheClosedForm_LinearInTicksAndPopulation()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var cfg = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
        var colony = ColonyTestWorld.Founded(grid, sim, cfg);
        for (int i = 0; i < 10; i++)
        {
            colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
        }
        // Soldiers with no corpses/dig work idle in place — no production
        // path touches Nectar, so decay is the only term.
        colony.Nectar = 50.0;

        const int ticks = 4_000;
        ColonyTestWorld.Run(colony, sim, ticks);

        double expected = 50.0 - ticks * DrainPerTick(cfg, 10);
        Assert.Equal(expected, colony.Nectar, 6);
    }

    [Fact]
    public void PortedAnchor_AtReferencePopulation_DrainsExactly018PerSecond()
    {
        // The whole point of porting: at the reference population (70),
        // one sim second (30 ticks) drains exactly 0.18 mass.
        var cfg = new ColonyConfig();
        Assert.Equal(0.18, 30 * DrainPerTick(cfg, cfg.NectarDecayReferencePopulation), 10);
        // And the implementation agrees: a colony with 70 workers loses
        // 0.18 over 30 ticks.
        var (grid, sim) = ColonyTestWorld.Create();
        var runCfg = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
        var colony = ColonyTestWorld.Founded(grid, sim, runCfg);
        for (int i = 0; i < runCfg.NectarDecayReferencePopulation; i++)
        {
            colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
        }
        colony.Nectar = 10.0;
        ColonyTestWorld.Run(colony, sim, 30);
        Assert.Equal(10.0 - 0.18, colony.Nectar, 6);
    }

    [Fact]
    public void PopulationScaling_DoubleWorkers_DoubleDrain()
    {
        double Drained(int workers)
        {
            var (grid, sim) = ColonyTestWorld.Create();
            var colony = ColonyTestWorld.Founded(grid, sim,
                new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
            for (int i = 0; i < workers; i++)
            {
                colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
            }
            colony.Nectar = 100.0;
            ColonyTestWorld.Run(colony, sim, 2_000);
            return 100.0 - colony.Nectar;
        }
        double d10 = Drained(10), d20 = Drained(20);
        Assert.True(d10 > 0, "sanity: some drain occurred");
        Assert.Equal(2.0, d20 / d10, 6);
    }

    [Fact]
    public void ZeroFloor_HoldsUnderSustainedDecay_AndPartialTickClamps()
    {
        var (grid, sim) = ColonyTestWorld.Create();
        var cfg = new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 };
        var colony = ColonyTestWorld.Founded(grid, sim, cfg);
        for (int i = 0; i < 50; i++)
        {
            colony.Spawn(Caste.Soldier, colony.HomeCenter.X, colony.HomeCenter.Y);
        }
        // Start with LESS than one tick's drain: the first tick must clamp
        // to exactly zero (never negative), and it must stay there.
        double oneTick = DrainPerTick(cfg, 50);
        colony.Nectar = oneTick * 0.4;
        ColonyTestWorld.Run(colony, sim, 1);
        Assert.Equal(0.0, colony.Nectar, 12);
        ColonyTestWorld.Run(colony, sim, 5_000);
        Assert.Equal(0.0, colony.Nectar, 12);
        // And nothing else happened at zero — the deliberately-open
        // design question stays open: no deaths beyond none (lifespans
        // disabled), population unchanged.
        Assert.Equal(50, colony.WorkerCount);
        Assert.Equal(0, colony.Stats.Deaths);
    }

    [Fact]
    public void GardenerProcessing_ProducesNectar_AtTheFarmedResourceMoment()
    {
        // Decay disabled → pure production: Nectar must equal processed
        // cycles × NectarPerProcessing exactly, moving in lockstep with
        // RawProcessedByGardeners (the same moment, not a parallel path).
        var (grid, sim) = ColonyTestWorld.Create();
        var cfg = new ColonyConfig
        {
            EggSurvivalChance = 0,
            EggLayIntervalTicks = 1_000_000,
            NectarDecayMassPerSecond = 0,
        };
        var colony = ColonyTestWorld.Founded(grid, sim, cfg);
        colony.RawMaterial = 25;
        colony.Spawn(Caste.Gardener, colony.HomeCenter.X, colony.HomeCenter.Y);
        double farmedBefore = colony.FarmedResource;

        ColonyTestWorld.Run(colony, sim, 5_000);

        Assert.True(colony.Stats.RawProcessedByGardeners > 0, "sanity: processing happened");
        Assert.Equal(colony.Stats.RawProcessedByGardeners * cfg.NectarPerProcessing,
            colony.Nectar, 6);
        // FarmedResource is untouched by the new mechanic: its delta over
        // the run is still exactly the processed count (the Phase 7
        // contract), not processed + nectar or any other coupling.
        Assert.Equal(colony.Stats.RawProcessedByGardeners,
            colony.FarmedResource - farmedBefore, 6);
    }

    [Fact]
    public void NectarMath_IsPureAndDeterministic_NoRandomness()
    {
        // Same colony run twice → identical Nectar at every sample. (The
        // mechanic is pure math — no RNG anywhere; this pins that, same
        // convention as the Phase 27 trail purity test.)
        static List<double> Run()
        {
            var (grid, sim) = ColonyTestWorld.Create();
            var colony = ColonyTestWorld.Founded(grid, sim,
                new ColonyConfig { EggSurvivalChance = 0, EggLayIntervalTicks = 1_000_000 });
            colony.RawMaterial = 50;
            colony.Spawn(Caste.Gardener, colony.HomeCenter.X, colony.HomeCenter.Y);
            colony.Spawn(Caste.Soldier, colony.HomeCenter.X + 1, colony.HomeCenter.Y);
            colony.Nectar = 5;
            var samples = new List<double>();
            for (int t = 0; t < 3_000; t++)
            {
                colony.Tick();
                sim.Tick();
                if (t % 250 == 0) samples.Add(colony.Nectar);
            }
            return samples;
        }
        Assert.Equal(Run(), Run());
    }
}
