using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GroundSim.Render;

/// <summary>
/// Blits grid cells into a WriteableBitmap. Read-only over the simulation:
/// it never mutates Grid, Simulation, or Colony state.
///
/// Rendering strategy (Phase 3): the full grid is drawn ONCE at startup;
/// afterwards only dirty cells are redrawn each frame, then dynamic entities
/// are overlaid. Settled terrain costs zero per-frame draw time.
///
/// Phase 8 visual language (flat colors, no art assets):
///   yellow = falling particle      pink   = Queen
///   green  = Gardener (mint = Minim)   blue   = Forager
///   red    = Soldier               pale dot = egg
///   excavated rooms get a subtle per-type background tint.
/// </summary>
public sealed class GridRenderer
{
    // Phase 13 Part C: doubled render resolution — cells draw in less than
    // half their former pixel footprint (5 → 2; exact halving of an odd size
    // is impossible in integer pixels, so this is slightly finer than
    // double). Same world dimensions; purely a rendering-density change.
    public const int CellSize = 2; // pixels per cell

    private readonly Grid _grid;
    private readonly byte[] _cellPixels = new byte[CellSize * CellSize * 4];

    public WriteableBitmap Bitmap { get; }

    private static readonly Color AwakeColor = Color.FromRgb(255, 230, 40);
    private static readonly Color QueenColor = Color.FromRgb(255, 80, 200);
    // Phase 18: Tender split — Gardener keeps the old Tender green (it
    // inherits the processing role Kevin has watched since Phase 8); Minim
    // gets a paler mint so caregivers read as their own, smaller caste.
    // Phase 19: Soldier inherits Major's red (it inherits the dig/burial
    // duties Kevin has watched under that color).
    private static readonly Color MinimColor = Color.FromRgb(170, 235, 190);
    private static readonly Color GardenerColor = Color.FromRgb(90, 200, 120);
    private static readonly Color ForagerColor = Color.FromRgb(80, 170, 255);
    private static readonly Color SoldierColor = Color.FromRgb(230, 65, 65);

    // Phase 19 Part C: Kevin's exact caste-size hierarchy, in units of one
    // dirt-cell diameter. Single source of truth — the renderer, the legend,
    // the dirty-marking footprint, and the correspondence test all read
    // THESE values. Queen and Forager sharing 4 is intentional per spec.
    public static int SizeUnits(Caste caste) => caste switch
    {
        Caste.Minim => 2,
        Caste.Gardener => 3,
        Caste.Forager => 4,
        Caste.Soldier => 5,
        _ => 2,
    };
    public const int QueenSizeUnits = 4;

    /// <summary>Phase 19: relative size (in the same dirt-cell units) for a
    /// legend entry, or null for non-caste entries (which keep the square
    /// swatch). Keyed by legend label so the legend stays single-source.</summary>
    public static int? LegendSizeUnits(string label) => label switch
    {
        "Minim" => SizeUnits(Caste.Minim),
        "Gardener" => SizeUnits(Caste.Gardener),
        "Forager" => SizeUnits(Caste.Forager),
        "Soldier" => SizeUnits(Caste.Soldier),
        "Queen" => QueenSizeUnits,
        _ => null,
    };

    private static readonly Color EggColor = Color.FromRgb(240, 240, 215);

    private static readonly Color AirColor = Color.FromRgb(24, 26, 34);
    private static readonly Color HomeTint = Color.FromRgb(44, 40, 56);
    private static readonly Color GardenTint = Color.FromRgb(32, 54, 40);
    private static readonly Color NurseryTint = Color.FromRgb(56, 46, 32);
    // Phase 18 Part B: new rooms — olive for leaf storage, muted mauve for
    // the graveyard.
    private static readonly Color FoodStorageTint = Color.FromRgb(60, 58, 24);
    private static readonly Color GraveyardTint = Color.FromRgb(58, 42, 50);

    /// <summary>Phase 16: the material palette, exposed so the legend (and
    /// tests) read the SAME constants the renderer draws with — the legend
    /// can never drift from the actual colors.</summary>
    public static Color ColorFor(CellMaterial m) => MaterialColor(m);

    /// <summary>Phase 16: the on-screen legend's content, derived directly
    /// from this class's color constants (single source of truth). Order is
    /// display order: terrain first, then entities, then room tints.</summary>
    public static IReadOnlyList<(string Label, Color Color)> LegendEntries { get; } = new[]
    {
        ("Dirt", MaterialColor(CellMaterial.Dirt)),
        ("Rock", MaterialColor(CellMaterial.Rock)),
        ("Loose rock", MaterialColor(CellMaterial.LooseRock)),
        ("Stick", MaterialColor(CellMaterial.Stick)),
        ("Grass", MaterialColor(CellMaterial.Grass)),
        ("Fungus", MaterialColor(CellMaterial.Fungus)),
        ("Falling particle", AwakeColor),
        ("Queen", QueenColor),
        ("Minim", MinimColor),
        ("Gardener", GardenerColor),
        ("Forager", ForagerColor),
        ("Soldier", SoldierColor),
        ("Egg", EggColor),
        ("Remains", MaterialColor(CellMaterial.Remains)),
        ("Home room", HomeTint),
        ("Garden room", GardenTint),
        ("Nursery room", NurseryTint),
        ("Food-storage room", FoodStorageTint),
        ("Graveyard room", GraveyardTint),
    };

    private static Color MaterialColor(CellMaterial m) => m switch
    {
        CellMaterial.Air => AirColor,
        CellMaterial.Dirt => Color.FromRgb(133, 94, 61),
        CellMaterial.Rock => Color.FromRgb(96, 98, 104),
        CellMaterial.Grass => Color.FromRgb(88, 148, 68),
        CellMaterial.Fungus => Color.FromRgb(154, 96, 176),
        CellMaterial.LooseRock => Color.FromRgb(150, 152, 158),
        CellMaterial.Stick => Color.FromRgb(186, 148, 92),
        CellMaterial.Remains => Color.FromRgb(214, 208, 186), // Phase 18: bone
        _ => Colors.Magenta,
    };

    public GridRenderer(Grid grid)
    {
        _grid = grid;
        Bitmap = new WriteableBitmap(
            grid.Width * CellSize, grid.Height * CellSize, 96, 96, PixelFormats.Bgr32, null);
    }

    public void DrawFull(Colony? colony = null)
    {
        for (int y = 0; y < _grid.Height; y++)
        {
            for (int x = 0; x < _grid.Width; x++)
            {
                DrawCell(x, y, CellColor(x, y, colony));
            }
        }
    }

    /// <summary>Redraws only the given dirty cells from grid state (with room
    /// tinting), then overlays particles, eggs, workers, and the queen.</summary>
    public void DrawFrame(
        IReadOnlyCollection<(int X, int Y)> dirtyCells, Simulation sim, Colony colony)
    {
        foreach (var (x, y) in dirtyCells)
        {
            DrawCell(x, y, CellColor(x, y, colony));
        }
        foreach (var p in sim.ActiveParticles)
        {
            DrawCell(p.X, p.Y, AwakeColor);
        }
        foreach (var egg in colony.Eggs)
        {
            DrawDot(egg.X, egg.Y, EggColor);
        }
        foreach (var c in colony.Corpses) DrawDot(c.X, c.Y, MaterialColor(CellMaterial.Remains));
        // Phase 19 Part C: castes render as circles sized by SizeUnits —
        // purely visual; the agent's grid cell (all gameplay logic) is the
        // circle's CENTER and is unchanged.
        foreach (var m in colony.Minims) DrawCasteCircle(m.X, m.Y, SizeUnits(Caste.Minim), MinimColor);
        foreach (var g in colony.Gardeners) DrawCasteCircle(g.X, g.Y, SizeUnits(Caste.Gardener), GardenerColor);
        foreach (var f in colony.Foragers) DrawCasteCircle(f.X, f.Y, SizeUnits(Caste.Forager), ForagerColor);
        foreach (var s in colony.Soldiers) DrawCasteCircle(s.X, s.Y, SizeUnits(Caste.Soldier), SoldierColor);
        DrawCasteCircle(colony.Queen.X, colony.Queen.Y, QueenSizeUnits, QueenColor);
    }

    /// <summary>Background color for a cell: material color, with excavated
    /// rooms tinting their Air cells so "this is the Garden" reads visually.</summary>
    private Color CellColor(int x, int y, Colony? colony)
    {
        var m = _grid[x, y];
        if (m != CellMaterial.Air || colony is null) return MaterialColor(m);
        foreach (var room in colony.Rooms)
        {
            if (room.Excavated && room.Contains(x, y))
            {
                return room.Type switch
                {
                    RoomType.Home => HomeTint,
                    RoomType.Garden => GardenTint,
                    RoomType.Nursery => NurseryTint,
                    RoomType.FoodStorage => FoodStorageTint,
                    RoomType.Graveyard => GraveyardTint,
                    _ => AirColor,
                };
            }
        }
        return AirColor;
    }

    /// <summary>Phase 19 Part C: a filled circle of the given diameter in
    /// dirt-cell units, centered on the agent's cell. Drawn as per-row
    /// horizontal spans, so pixels OUTSIDE the circle are never written —
    /// the square-corner background stays intact (the dirty-cell system
    /// restores everything underneath next frame via the widened entity
    /// footprint marks in MainWindow/App).</summary>
    private void DrawCasteCircle(int cellX, int cellY, int units, Color c)
    {
        int d = units * CellSize;
        double r = d / 2.0;
        double cx = cellX * CellSize + CellSize / 2.0;
        double cy = cellY * CellSize + CellSize / 2.0;
        int bmpW = Bitmap.PixelWidth, bmpH = Bitmap.PixelHeight;
        for (int py = (int)Math.Floor(cy - r); py < (int)Math.Ceiling(cy + r); py++)
        {
            if (py < 0 || py >= bmpH) continue;
            double dy = py + 0.5 - cy;
            double half = Math.Sqrt(Math.Max(0, r * r - dy * dy));
            int x0 = Math.Max(0, (int)Math.Floor(cx - half));
            int x1 = Math.Min(bmpW, (int)Math.Ceiling(cx + half));
            int w = x1 - x0;
            if (w <= 0) continue;
            var row = new byte[w * 4];
            for (int i = 0; i < w; i++)
            {
                int o = i * 4;
                row[o] = c.B;
                row[o + 1] = c.G;
                row[o + 2] = c.R;
                row[o + 3] = 255;
            }
            Bitmap.WritePixels(new System.Windows.Int32Rect(x0, py, w, 1), row, w * 4, 0);
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

    /// <summary>A smaller centered marker (eggs), so stationary entities read
    /// as objects sitting in a cell rather than filling it. At very small
    /// cell sizes an inset would collapse to zero pixels, so it only applies
    /// when there's room for it.</summary>
    private void DrawDot(int x, int y, Color c)
    {
        const int inset = CellSize >= 4 ? 1 : 0;
        const int size = CellSize - 2 * inset;
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < size * size; i++)
        {
            int o = i * 4;
            pixels[o] = c.B;
            pixels[o + 1] = c.G;
            pixels[o + 2] = c.R;
            pixels[o + 3] = 255;
        }
        var rect = new System.Windows.Int32Rect(x * CellSize + inset, y * CellSize + inset, size, size);
        Bitmap.WritePixels(rect, pixels, size * 4, 0);
    }
}
