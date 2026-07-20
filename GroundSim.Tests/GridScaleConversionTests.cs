using GroundSim;

namespace GroundSim.Tests;

/// <summary>
/// Phase 15: pins every grid-scale conversion to its pre-scale basis by
/// FORMULA, not by re-stated magic number — the same self-verification
/// pattern Phase 14 used for its px→cell conversion (the haul-floor
/// distance coming out identical in both units). If a future retune
/// changes a value deliberately, the corresponding pin here must be
/// updated in the same commit, with the reasoning — that's the point.
/// </summary>
public class GridScaleConversionTests
{
    private const int S = ColonyConfig.GridScale;
    private readonly ColonyConfig _cfg = new();

    [Fact]
    public void LinearDistances_ScaleByGridScale()
    {
        // Phase 14 cell values × S (all linear physical distances).
        Assert.Equal(12 * S, _cfg.RoomBranchMinDistance);
        Assert.Equal(20 * S, _cfg.RoomBranchMaxDistance);
        Assert.Equal(2.0 * S, _cfg.TunnelWidthMin);
        Assert.Equal(3.0 * S, _cfg.TunnelWidthMax);
        Assert.Equal(8 * S, _cfg.ShaftMinLength);
        Assert.Equal(12 * S, _cfg.ShaftMaxLength);
        Assert.Equal(5 * S, _cfg.MoundDropRange);
        Assert.Equal(7 * S, _cfg.MoundMaxHeight);
        Assert.Equal(1 * S, _cfg.RoomOverlapBuffer);
    }

    [Fact]
    public void Areas_ScaleByGridScaleSquared()
    {
        Assert.Equal(80 * S * S, _cfg.ChamberMinArea);
        Assert.Equal(130 * S * S, _cfg.ChamberMaxArea);
        Assert.Equal(40 * S * S, _cfg.HomeChamberMinArea);
        Assert.Equal(55 * S * S, _cfg.HomeChamberMaxArea);
    }

    [Fact]
    public void GatherFalloff_RederivesFromThePixelBasis_NotByCompounding()
    {
        // game.js: 0.02 per px; 1 cell = 8 px / GridScale. The falloff must
        // equal the fresh px-basis derivation (0.02 × 8/S), which for S=2 is
        // also 0.16/S — both routes agreeing is the anti-double-compounding
        // check the handoff asked for.
        Assert.Equal(0.02 * (8.0 / S), _cfg.GatherDistanceFalloff, 10);

        // The physical haul-floor distance is invariant: (15−5)/falloff
        // cells must equal Colony Builder's 500 px in every unit system.
        double floorDistanceCells = (_cfg.GatherChunkBase - _cfg.GatherChunkMin) / _cfg.GatherDistanceFalloff;
        Assert.Equal(500.0, floorDistanceCells * (8.0 / S), 6); // px
        Assert.Equal(62.5 * S, floorDistanceCells, 6);          // cells
    }

    [Fact]
    public void ScaleInvariants_DidNotChange()
    {
        // Masses, probabilities, tick durations, angles, fractions — all
        // dimensionless in cells; pinned to their Phase 14 values so an
        // accidental "scale everything" sweep can't silently touch them.
        Assert.Equal(15.0, _cfg.GatherChunkBase);
        Assert.Equal(5.0, _cfg.GatherChunkMin);
        Assert.Equal(14, _cfg.StarterResource);
        Assert.Equal(165, _cfg.EggLayIntervalTicks);
        Assert.Equal(165, _cfg.EggMaturationTicks);
        Assert.Equal(9, _cfg.ProcessTicks);
        Assert.Equal(45, _cfg.GardenTriggerThreshold);
        Assert.Equal(4_200, _cfg.NurseryBroodPressureThreshold);
        Assert.Equal(0.15, _cfg.TunnelTurnJitter);
        Assert.Equal(0.55, _cfg.TunnelMaxDeviation);
        Assert.Equal(0.4, _cfg.ChamberEdgeNoise);
        Assert.Equal(4, _cfg.CaGenerations);
        Assert.Equal(5, _cfg.CaThreshold);
        // RockDigTicks deliberately unscaled: the designed semantic is the
        // 4×-dirt RATIO, which survives rescaling; scaling it to 1 would
        // have silently erased Phase 13's rock-hardness property.
        Assert.Equal(4, Agent.RockDigTicks);
    }

    [Fact]
    public void AppWorld_SamePhysicalFootprint_FinerCells()
    {
        // Part A decision pinned: the world did not grow — 200×120 old
        // cells re-discretized as (200×S)×(120×S), same Colony Builder
        // pixel footprint (1600×960 px at 8 px/old-cell).
        Assert.Equal(200 * S, Render.ColonyScenario.Width);
        Assert.Equal(120 * S, Render.ColonyScenario.Height);
        Assert.Equal(70 * S, Render.ColonyScenario.GroundLevel);
        Assert.Equal(1600, Render.ColonyScenario.Width * 8 / S);
    }
}
