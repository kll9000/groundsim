using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 27: the synthetic-harness verification the handoff asked for —
/// the trail system exercised in isolation, since no agent behavior
/// populates it until Phase 28. Everything here injects entries by hand,
/// advances ticks, and pins decay/reinforcement/capping/culling behavior
/// against the configured constants (never against re-derived copies of
/// the same math where avoidable — decay is checked against the
/// closed-form expectation, culling against observable Count).
/// </summary>
public class TrailMapTests
{
    private static ColonyConfig Cfg => new();

    [Fact]
    public void Reinforce_Accumulates_AndCapsAtMaxStrength()
    {
        var cfg = Cfg;
        var trails = new TrailMap(cfg);
        trails.Reinforce(10, 20);
        Assert.Equal(cfg.TrailReinforcePerVisit, trails.Strength(10, 20), 6);
        trails.Reinforce(10, 20);
        Assert.Equal(2 * cfg.TrailReinforcePerVisit, trails.Strength(10, 20), 6);

        // Hammer it far past the cap: strength saturates, never exceeds.
        for (int i = 0; i < 1000; i++) trails.Reinforce(10, 20);
        Assert.Equal(cfg.TrailMaxStrength, trails.Strength(10, 20), 6);
        Assert.Equal(1, trails.Count); // still one entry, not one per call
    }

    [Fact]
    public void Decay_IsExponential_MatchingTheClosedForm()
    {
        var cfg = Cfg;
        var trails = new TrailMap(cfg);
        for (int i = 0; i < 8; i++) trails.Reinforce(5, 5); // strength 8
        double s0 = trails.Strength(5, 5);

        const int ticks = 500;
        for (int t = 0; t < ticks; t++) trails.Tick();

        double expected = s0 * Math.Pow(cfg.TrailDecayFactor, ticks);
        Assert.Equal(expected, trails.Strength(5, 5), 6);

        // Half-life sanity: at 0.9995/tick, ~1386 ticks halves it. Advance
        // to 1386 total and confirm we're within 1% of s0/2 — pins the
        // CHOICE of exponential decay, not just "some decrease happened".
        for (int t = ticks; t < 1386; t++) trails.Tick();
        Assert.InRange(trails.Strength(5, 5), s0 * 0.495, s0 * 0.505);
    }

    [Fact]
    public void NegligibleEntries_AreRemovedOutright_NotKeptAtNearZero()
    {
        var cfg = Cfg;
        var trails = new TrailMap(cfg);
        trails.Reinforce(3, 3); // strength 1.0
        Assert.Equal(1, trails.Count);

        // Closed form: 1.0 × f^n < cull threshold  ⇒  n > ln(thr)/ln(f).
        int ticksToCull = (int)Math.Ceiling(
            Math.Log(cfg.TrailCullThreshold) / Math.Log(cfg.TrailDecayFactor)) + 1;
        for (int t = 0; t < ticksToCull; t++) trails.Tick();

        Assert.Equal(0, trails.Count);       // gone from the structure,
        Assert.Equal(0, trails.Strength(3, 3), 6); // not lingering near zero
    }

    [Fact]
    public void SparseStructure_StaysBounded_UnderLongChurn()
    {
        // A moving reinforcement window over a long run: the structure must
        // track ACTIVITY, not history — cells the window left behind decay
        // out, so Count stays bounded well below total-cells-ever-touched.
        var cfg = Cfg;
        var trails = new TrailMap(cfg);
        const int windowWidth = 50, totalTicks = 60_000;
        int maxCount = 0;
        for (int t = 0; t < totalTicks; t++)
        {
            int x = t / 10 % 2000; // slow forward creep, wraps
            for (int dx = 0; dx < windowWidth; dx += 10)
            {
                trails.Reinforce(x + dx, 0);
            }
            trails.Tick();
            maxCount = Math.Max(maxCount, trails.Count);
        }
        // ~6,000 distinct cells get touched over the run; the live set must
        // stay proportional to the decay horizon of the active window, far
        // below that. (Measured ~600; 1,500 is a generous structural bound
        // that still fails hard if culling silently stops working.)
        Assert.True(maxCount <= 1_500,
            $"sparse trail structure grew to {maxCount} entries — culling is not bounding it");
        // And once activity stops, everything drains to empty.
        for (int t = 0; t < 20_000; t++) trails.Tick();
        Assert.Equal(0, trails.Count);
    }

    [Fact]
    public void TrailMath_IsPureAndDeterministic_NoRandomness()
    {
        // Same operation sequence twice → identical state, tick for tick.
        // (There is no RNG anywhere in TrailMap — this pins that staying
        // true; if someone adds a roll later, this fails and forces the
        // seeded-RNG conversation the determinism convention requires.)
        static double[] Run()
        {
            var trails = new TrailMap(new ColonyConfig());
            var samples = new List<double>();
            for (int t = 0; t < 2_000; t++)
            {
                if (t % 7 == 0) trails.Reinforce(t % 40, t % 11);
                trails.Tick();
                if (t % 250 == 0) samples.Add(trails.Strength(t % 40, t % 11) + trails.Count);
            }
            return samples.ToArray();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void ColonyOwnsATrailMap_TickedButEmpty_UntilPhase28()
    {
        // The wiring pin: a founded colony has a live trail map, colony
        // ticks tick it, and nothing in current behavior populates it.
        var (grid, sim) = ColonyTestWorld.Create();
        var colony = ColonyTestWorld.Founded(grid, sim);
        colony.Trails.Reinforce(100, 50);
        double before = colony.Trails.Strength(100, 50);
        ColonyTestWorld.Run(colony, sim, 100);
        Assert.True(colony.Trails.Strength(100, 50) < before,
            "colony ticks must decay trails");
        Assert.Equal(1, colony.Trails.Count); // and nothing else appeared
    }
}
