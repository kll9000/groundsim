using System.Text;

namespace GroundSim;

/// <summary>Renders a rectangular window of the grid as ASCII for eyeballing pile shapes.</summary>
public static class AsciiRenderer
{
    public static char Glyph(CellMaterial m) => m switch
    {
        CellMaterial.Air => '.',
        CellMaterial.Dirt => '#',
        CellMaterial.Rock => '@',
        CellMaterial.Grass => '"',
        CellMaterial.Fungus => '%',
        CellMaterial.LooseRock => 'o',
        CellMaterial.Stick => '/',
        _ => '?',
    };

    public static string Render(Grid grid, int x0, int y0, int width, int height)
    {
        var sb = new StringBuilder();
        int x1 = Math.Min(grid.Width, x0 + width);
        int y1 = Math.Min(grid.Height, y0 + height);
        for (int y = Math.Max(0, y0); y < y1; y++)
        {
            for (int x = Math.Max(0, x0); x < x1; x++)
            {
                sb.Append(Glyph(grid[x, y]));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
