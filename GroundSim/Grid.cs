namespace GroundSim;

/// <summary>
/// Dense 2D world grid. Y grows downward (row 0 is the sky), matching console
/// rendering order.
///
/// Storage is a flattened 1D array indexed as [y * Width + x]. Tradeoff vs a
/// 2D array: a 1D array is a single contiguous allocation with cheaper index
/// math and better cache behavior when scanning rows, and it makes future
/// SIMD/span-based bulk operations trivial. The cost is slightly less readable
/// indexing, which we hide behind the indexer below.
/// </summary>
public sealed class Grid
{
    public int Width { get; }
    public int Height { get; }

    private readonly CellMaterial[] _cells;

    public Grid(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        _cells = new CellMaterial[width * height]; // all Air by default
    }

    public CellMaterial this[int x, int y]
    {
        get => _cells[y * Width + x];
        set => _cells[y * Width + x] = value;
    }

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public bool IsAir(int x, int y) => InBounds(x, y) && this[x, y] == CellMaterial.Air;

    /// <summary>
    /// Creates a test world: air above, flat dirt floor below
    /// <paramref name="groundLevel"/>, with rock scattered at depth.
    /// </summary>
    /// <param name="groundLevel">First row (y) that is solid ground.</param>
    /// <param name="seed">Seed for deterministic rock scattering.</param>
    public static Grid CreateTestWorld(int width = 200, int height = 200, int? groundLevel = null, int seed = 12345)
    {
        var grid = new Grid(width, height);
        int ground = groundLevel ?? height / 2;
        var rng = new Random(seed);

        for (int y = ground; y < height; y++)
        {
            // Rock becomes more likely deeper below the surface.
            int depth = y - ground;
            double rockChance = depth < height / 8 ? 0.0 : Math.Min(0.25, depth / (double)height * 0.5);
            for (int x = 0; x < width; x++)
            {
                grid[x, y] = rng.NextDouble() < rockChance ? CellMaterial.Rock : CellMaterial.Dirt;
            }
        }
        return grid;
    }

    /// <summary>
    /// Digs the cell at (x, y): diggable material becomes Air and the removed
    /// material is returned so the caller can "carry" it. Returns null if the
    /// cell is out of bounds, already Air, or not diggable (Rock).
    /// </summary>
    public CellMaterial? Dig(int x, int y)
    {
        if (!InBounds(x, y)) return null;
        var material = this[x, y];
        if (material == CellMaterial.Air || material == CellMaterial.Rock) return null;
        this[x, y] = CellMaterial.Air;
        return material;
    }
}
