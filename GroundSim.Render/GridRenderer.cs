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

    // Phase 22: single reusable backbuffer mirroring the whole bitmap. All
    // draw methods write here; one WritePixels per frame (the dirty bounding
    // rect) flushes it. Replaces a fresh byte[] + WritePixels call per dirty
    // cell and per circle-row. Dirty-MARKING is untouched — this changes how
    // pixel writes reach the bitmap, not what gets marked or when.
    private readonly byte[] _backbuffer;
    private readonly int _stride;
    private int _dirtyX0, _dirtyY0, _dirtyX1, _dirtyY1; // px, X1/Y1 exclusive
    private bool _dirtyAny;

    // Phase 22: cell→room-tint map, stamped once per room when it becomes
    // excavated (room cell membership is fixed at plan time and Excavated
    // flips once, so a stamp never needs undoing). CellColor drops from
    // O(rooms × Contains) per redrawn air cell to one array read.
    private readonly Color?[] _tintMap;
    private readonly HashSet<Room> _tintStamped = new();

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

    /// <summary>Phase 21 Part C: unit-to-pixel scale for the caste circles.
    /// Kevin found the Phase 19 sizes (1 unit = 1 full cell diameter) too
    /// large; halved per his request. Ratios between castes are untouched —
    /// this scales all circles together.</summary>
    public const double CasteCircleScale = 0.5;

    /// <summary>The on-bitmap pixel diameter for a circle of the given unit
    /// size — single source of truth for drawing and the size test.</summary>
    public static int CirclePixelDiameter(int units) =>
        Math.Max(1, (int)Math.Round(units * CellSize * CasteCircleScale));

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

    // Phase 23 item 5: day/night sky tint. Only ABOVE-GROUND air (the sky
    // band, y < SkyGroundLevel) shifts; underground tunnel air stays the
    // constant AirColor, and room tints are untouched. INVENTED palette,
    // same status as every other feel constant.
    private static readonly Color DaySkyColor = Color.FromRgb(58, 74, 110);
    private static readonly Color NightSkyColor = Color.FromRgb(10, 12, 20);

    // Phase 24 item 2: resource nodes were invisible (Phase 23 finding —
    // Foragers commuting to undrawn points). A diamond marker whose color
    // fades from full leaf-green toward a dim olive as the node depletes,
    // so a node running dry is finally something you can SEE. Full-color
    // constant exposed via the legend; presentation-only.
    private static readonly Color LeafNodeFullColor = Color.FromRgb(150, 240, 70);
    private static readonly Color LeafNodeEmptyColor = Color.FromRgb(58, 66, 40);
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
        ("Leaf node", LeafNodeFullColor),
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

    // Phase 23 item 5: rows above this y are "sky" and get the day/night
    // tint; everything at/below keeps constant colors. Null disables the
    // whole feature (renderer behaves exactly as before Phase 23).
    private readonly int? _skyGroundLevel;
    private Color _skyColor;
    private int _skyStep = -1;

    /// <summary>Number of discrete sky-tint levels per day. Each step change
    /// repaints the sky band once (~900 ticks apart at 48 steps/day) —
    /// smooth to the eye, negligible cost.</summary>
    public const int SkySteps = 48;

    public GridRenderer(Grid grid, int? skyGroundLevel = null)
    {
        _grid = grid;
        _skyGroundLevel = skyGroundLevel;
        _skyColor = AirColor;
        Bitmap = new WriteableBitmap(
            grid.Width * CellSize, grid.Height * CellSize, 96, 96, PixelFormats.Bgr32, null);
        _stride = Bitmap.PixelWidth * 4;
        _backbuffer = new byte[_stride * Bitmap.PixelHeight];
        _tintMap = new Color?[grid.Width * grid.Height];
    }

    /// <summary>Phase 23 item 5: brightness for a fractional day position.
    /// INVENTED phase mapping, chosen so the app doesn't launch into pitch
    /// dark: day N.0 = dawn (0.5, brightening), N.25 = noon (1), N.5 = dusk
    /// (0.5), N.75 = midnight (0). Exposed for the correspondence check.</summary>
    public static double SkyBrightness(double dayFraction) =>
        0.5 + 0.5 * Math.Sin(2 * Math.PI * dayFraction);

    /// <summary>Feeds the day counter into the sky tint. Presentation-only,
    /// like SimCalendar itself: reads the tick count, never influences the
    /// simulation. Quantized to SkySteps; on a step change the sky band is
    /// repainted into the backbuffer (flushed by the next DrawFrame).</summary>
    public void SetTimeOfDay(double dayNumber)
    {
        if (_skyGroundLevel is not { } ground) return;
        double frac = dayNumber - Math.Floor(dayNumber);
        int step = (int)(SkyBrightness(frac) * (SkySteps - 1));
        if (step == _skyStep) return;
        _skyStep = step;
        double b = step / (double)(SkySteps - 1);
        _skyColor = Color.FromRgb(
            (byte)(NightSkyColor.R + (DaySkyColor.R - NightSkyColor.R) * b),
            (byte)(NightSkyColor.G + (DaySkyColor.G - NightSkyColor.G) * b),
            (byte)(NightSkyColor.B + (DaySkyColor.B - NightSkyColor.B) * b));
        for (int y = 0; y < Math.Min(ground, _grid.Height); y++)
        {
            for (int x = 0; x < _grid.Width; x++)
            {
                if (_grid[x, y] == CellMaterial.Air) DrawCell(x, y, _skyColor);
            }
        }
        // Entities overlay after the dirty-cell pass each DrawFrame, so
        // anything the repaint painted over is restored the same frame.
    }

    public void DrawFull(Colony? colony = null)
    {
        if (colony is not null) RefreshRoomTints(colony);
        for (int y = 0; y < _grid.Height; y++)
        {
            for (int x = 0; x < _grid.Width; x++)
            {
                DrawCell(x, y, CellColor(x, y));
            }
        }
        Flush();
    }

    /// <summary>Stamps the tint map for any room that has become excavated
    /// since the last call. Lives in the renderer (not the window) so the
    /// headless smoke path gets identical tinting for free.</summary>
    private void RefreshRoomTints(Colony colony)
    {
        foreach (var room in colony.Rooms)
        {
            if (!room.Excavated || !_tintStamped.Add(room)) continue;
            var tint = room.Type switch
            {
                RoomType.Home => HomeTint,
                RoomType.Garden => GardenTint,
                RoomType.Nursery => NurseryTint,
                RoomType.FoodStorage => FoodStorageTint,
                RoomType.Graveyard => GraveyardTint,
                _ => AirColor,
            };
            foreach (var (x, y) in room.Cells)
            {
                if (_grid.InBounds(x, y)) _tintMap[y * _grid.Width + x] = tint;
            }
        }
    }

    /// <summary>Redraws only the given dirty cells from grid state (with room
    /// tinting), then overlays particles, eggs, workers, and the queen.</summary>
    public void DrawFrame(
        IReadOnlyCollection<(int X, int Y)> dirtyCells, Simulation sim, Colony colony)
    {
        RefreshRoomTints(colony);
        foreach (var (x, y) in dirtyCells)
        {
            DrawCell(x, y, CellColor(x, y));
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
        // Phase 24: node markers draw BEFORE caste circles so ants working a
        // node appear on top of it. Redrawn every frame like eggs/corpses —
        // markers never move, so no ghost-trail marking is needed; terrain
        // changes under one repaint as background first, marker on top.
        foreach (var n in colony.Nodes) DrawNodeMarker(n);
        // Phase 19 Part C: castes render as circles sized by SizeUnits —
        // purely visual; the agent's grid cell (all gameplay logic) is the
        // circle's CENTER and is unchanged.
        foreach (var m in colony.Minims) DrawCasteCircle(m.X, m.Y, SizeUnits(Caste.Minim), MinimColor);
        foreach (var g in colony.Gardeners) DrawCasteCircle(g.X, g.Y, SizeUnits(Caste.Gardener), GardenerColor);
        foreach (var f in colony.Foragers) DrawCasteCircle(f.X, f.Y, SizeUnits(Caste.Forager), ForagerColor);
        foreach (var s in colony.Soldiers) DrawCasteCircle(s.X, s.Y, SizeUnits(Caste.Soldier), SoldierColor);
        DrawCasteCircle(colony.Queen.X, colony.Queen.Y, QueenSizeUnits, QueenColor);
        Flush();
    }

    /// <summary>Background color for a cell: material color, with excavated
    /// rooms tinting their Air cells so "this is the Garden" reads visually.
    /// Phase 22: tint comes from the precomputed map (see RefreshRoomTints),
    /// not a per-cell scan over every room.</summary>
    private Color CellColor(int x, int y)
    {
        var m = _grid[x, y];
        if (m != CellMaterial.Air) return MaterialColor(m);
        // Phase 23: sky-band air uses the current day/night tint, so cells
        // the physics dirties up there repaint consistently with the band.
        if (_skyGroundLevel is { } ground && y < ground) return _skyColor;
        return _tintMap[y * _grid.Width + x] ?? AirColor;
    }

    /// <summary>Phase 19 Part C: a filled circle of the given diameter in
    /// dirt-cell units, centered on the agent's cell. Drawn as per-row
    /// horizontal spans, so pixels OUTSIDE the circle are never written —
    /// the square-corner background stays intact (the dirty-cell system
    /// restores everything underneath next frame via the widened entity
    /// footprint marks in MainWindow/App).</summary>
    /// <summary>Phase 24 item 2: a filled diamond centered on the node's
    /// cell (diamond, so it can't be mistaken for a caste circle). Color
    /// lerps from LeafNodeFullColor toward LeafNodeEmptyColor as Remaining
    /// falls — the visual explanation for Foragers abandoning a node.</summary>
    private void DrawNodeMarker(ResourceNode node)
    {
        double fullness = node.Cap > 0 ? Math.Clamp(node.Remaining / node.Cap, 0, 1) : 0;
        var c = Color.FromRgb(
            (byte)(LeafNodeEmptyColor.R + (LeafNodeFullColor.R - LeafNodeEmptyColor.R) * fullness),
            (byte)(LeafNodeEmptyColor.G + (LeafNodeFullColor.G - LeafNodeEmptyColor.G) * fullness),
            (byte)(LeafNodeEmptyColor.B + (LeafNodeFullColor.B - LeafNodeEmptyColor.B) * fullness));
        const int r = 4; // half-height in px: a 9px-tall diamond at CellSize 2
        int cx = node.X * CellSize + CellSize / 2;
        int cy = node.Y * CellSize + CellSize / 2;
        int bmpW = Bitmap.PixelWidth, bmpH = Bitmap.PixelHeight;
        for (int dy = -r; dy <= r; dy++)
        {
            int py = cy + dy;
            if (py < 0 || py >= bmpH) continue;
            int half = r - Math.Abs(dy);
            int x0 = Math.Max(0, cx - half);
            int x1 = Math.Min(bmpW, cx + half + 1);
            if (x1 - x0 > 0) WriteSpan(x0, py, x1 - x0, c);
        }
    }

    private void DrawCasteCircle(int cellX, int cellY, int units, Color c)
    {
        int d = CirclePixelDiameter(units); // Phase 21: halved scale
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
            if (x1 - x0 > 0) WriteSpan(x0, py, x1 - x0, c);
        }
    }

    private void DrawCell(int x, int y, Color c)
    {
        for (int row = 0; row < CellSize; row++)
        {
            WriteSpan(x * CellSize, y * CellSize + row, CellSize, c);
        }
    }

    /// <summary>A smaller centered marker (eggs), so stationary entities read
    /// as objects sitting in a cell rather than filling it. At very small
    /// cell sizes an inset would collapse to zero pixels, so it only applies
    /// when there's room for it.</summary>
    private void DrawDot(int x, int y, Color c)
    {
        const int inset = CellSize >= 4 ? 1 : 0;
        const int size = CellSize - 2 * inset;
        for (int row = 0; row < size; row++)
        {
            WriteSpan(x * CellSize + inset, y * CellSize + inset + row, size, c);
        }
    }

    /// <summary>Writes a horizontal pixel run into the backbuffer and grows
    /// the frame's dirty bounding rect. Callers pass in-bounds spans
    /// (DrawCell/DrawDot derive from in-bounds cells; the circle clips).</summary>
    private void WriteSpan(int px, int py, int w, Color c)
    {
        int o = py * _stride + px * 4;
        for (int i = 0; i < w; i++, o += 4)
        {
            _backbuffer[o] = c.B;
            _backbuffer[o + 1] = c.G;
            _backbuffer[o + 2] = c.R;
            _backbuffer[o + 3] = 255;
        }
        if (!_dirtyAny)
        {
            (_dirtyX0, _dirtyY0, _dirtyX1, _dirtyY1) = (px, py, px + w, py + 1);
            _dirtyAny = true;
            return;
        }
        if (px < _dirtyX0) _dirtyX0 = px;
        if (py < _dirtyY0) _dirtyY0 = py;
        if (px + w > _dirtyX1) _dirtyX1 = px + w;
        if (py + 1 > _dirtyY1) _dirtyY1 = py + 1;
    }

    /// <summary>One WritePixels for the frame: copies the dirty bounding rect
    /// of the backbuffer to the bitmap. The rect can cover pixels scattered
    /// writes didn't touch — harmless, since the backbuffer mirrors the
    /// bitmap's full content at all times.</summary>
    private void Flush()
    {
        if (!_dirtyAny) return;
        var rect = new System.Windows.Int32Rect(
            _dirtyX0, _dirtyY0, _dirtyX1 - _dirtyX0, _dirtyY1 - _dirtyY0);
        Bitmap.WritePixels(rect, _backbuffer, _stride, _dirtyY0 * _stride + _dirtyX0 * 4);
        _dirtyAny = false;
    }
}
