using GroundSim;
using GroundSim.Render;

namespace GroundSim.Tests;

/// <summary>
/// Phase 16 Part A: the legend's content is derived from GridRenderer's own
/// color constants, and this pins the correspondence — every material the
/// simulation can produce is represented, material swatches equal the exact
/// color the renderer draws, and the handoff's required entries are present.
/// (Rendering pixels stay outside unit-test scope per project stance; this
/// tests the legend's DATA, which is what could silently drift.)
/// </summary>
public class LegendTests
{
    [Fact]
    public void Legend_CoversEveryRequiredEntry()
    {
        var labels = GridRenderer.LegendEntries.Select(e => e.Label).ToList();
        // Handoff-required minimum: terrain, castes, particle, room tints.
        foreach (var required in new[]
        {
            "Dirt", "Rock", "Loose rock", "Stick",
            "Queen", "Minim", "Gardener", "Forager", "Major",
            "Falling particle",
            "Home room", "Garden room", "Nursery room",
        })
        {
            Assert.Contains(required, labels);
        }
        // No duplicate labels.
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void Legend_MaterialSwatches_MatchTheRendererExactly()
    {
        var byLabel = GridRenderer.LegendEntries.ToDictionary(e => e.Label, e => e.Color);
        Assert.Equal(GridRenderer.ColorFor(CellMaterial.Dirt), byLabel["Dirt"]);
        Assert.Equal(GridRenderer.ColorFor(CellMaterial.Rock), byLabel["Rock"]);
        Assert.Equal(GridRenderer.ColorFor(CellMaterial.LooseRock), byLabel["Loose rock"]);
        Assert.Equal(GridRenderer.ColorFor(CellMaterial.Stick), byLabel["Stick"]);
        Assert.Equal(GridRenderer.ColorFor(CellMaterial.Grass), byLabel["Grass"]);
        Assert.Equal(GridRenderer.ColorFor(CellMaterial.Fungus), byLabel["Fungus"]);
    }

    [Fact]
    public void Legend_SwatchColors_AreVisuallyDistinct()
    {
        // Sanity: no two legend entries share the same color — a legend with
        // two identical swatches can't disambiguate anything. (Mined rock
        // deliberately reuses LooseRock's MATERIAL, so it shares that row —
        // there is one "Loose rock" entry, not a duplicate color.)
        var colors = GridRenderer.LegendEntries.Select(e => e.Color).ToList();
        Assert.Equal(colors.Count, colors.Distinct().Count());
    }
}
