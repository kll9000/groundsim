using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GroundSim.Render;

/// <summary>
/// Blits grid cells into a WriteableBitmap. Read-only over the simulation:
/// it never mutates Grid or Simulation state.
///
/// Rendering strategy: the full grid is drawn ONCE at startup; afterwards
/// only dirty cells (from DirtyTracker) are redrawn each frame, then active
/// particles are overlaid in a highlight color. Settled terrain costs zero
/// per-frame draw time.
/// </summary>
public sealed class GridRenderer
{
    public const int CellSize = 5; // pixels per cell

    private readonly Grid _grid;
    private readonly byte[] _cellPixels = new byte[CellSize * CellSize * 4];

    public WriteableBitmap Bitmap { get; }

    /// <summary>Debug overlay color for in-flight (awake) particles.</summary>
    private static readonly Color AwakeColor = Color.FromRgb(255, 230, 40);

    private static Color MaterialColor(CellMaterial m) => m switch
    {
        CellMaterial.Air => Color.FromRgb(24, 26, 34),
        CellMaterial.Dirt => Color.FromRgb(133, 94, 61),
        CellMaterial.Rock => Color.FromRgb(96, 98, 104),
        CellMaterial.Grass => Color.FromRgb(88, 148, 68),
        CellMaterial.Fungus => Color.FromRgb(154, 96, 176),
        CellMaterial.LooseRock => Color.FromRgb(150, 152, 158),
        CellMaterial.Stick => Color.FromRgb(186, 148, 92),
        _ => Colors.Magenta,
    };

    public GridRenderer(Grid grid)
    {
        _grid = grid;
        Bitmap = new WriteableBitmap(
            grid.Width * CellSize, grid.Height * CellSize, 96, 96, PixelFormats.Bgr32, null);
    }

    public void DrawFull()
    {
        for (int y = 0; y < _grid.Height; y++)
        {
            for (int x = 0; x < _grid.Width; x++)
            {
                DrawCell(x, y, MaterialColor(_grid[x, y]));
            }
        }
    }

    /// <summary>Redraws only the given dirty cells from grid state, then overlays particles.</summary>
    public void DrawFrame(IReadOnlyCollection<(int X, int Y)> dirtyCells, Simulation sim)
    {
        foreach (var (x, y) in dirtyCells)
        {
            DrawCell(x, y, MaterialColor(_grid[x, y]));
        }
        foreach (var p in sim.ActiveParticles)
        {
            DrawCell(p.X, p.Y, AwakeColor);
        }
    }

    private void DrawCell(int x, int y, Color c)
    {
        for (int i = 0; i < CellSize * CellSize; i++)
        {
            int o = i * 4;
            _cellPixels[o] = c.B;
            _cellPixels[o + 1] = c.G;
            _cellPixels[o + 2] = c.R;
            _cellPixels[o + 3] = 255;
        }
        var rect = new System.Windows.Int32Rect(x * CellSize, y * CellSize, CellSize, CellSize);
        Bitmap.WritePixels(rect, _cellPixels, CellSize * 4, 0);
    }
}
