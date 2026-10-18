using System.Collections.Generic;

namespace SmoothStonePaths;

internal enum PieceKind
{
    /// <summary>Full-width strip along one side.</summary>
    Edge,
    /// <summary>
    /// Full-width strip along a side whose counter-clockwise neighbour side is fringed with a different material.
    /// Its counter-clockwise end is thinned where the neighbour's strip is dense, so the two materials share the corner.
    /// </summary>
    EdgeConcave,
    /// <summary>Half-density ramp at the clockwise end of a side, mixing in the diagonal neighbour's material.</summary>
    BleedEast,
    /// <summary>Half-density ramp at the counter-clockwise end of a side.</summary>
    BleedWest,
    /// <summary>Quarter fade in a corner whose two sides have no fringe.</summary>
    Inner
}

/// <param name="Index">Side index (N, E, S, W = 0-3) for edge, concave edge and bleed pieces; corner index (NE, SE, SW, NW = 0-3) for inner pieces.</param>
/// <param name="Variant">For edge and concave edge pieces, one of the <see cref="StripVariant"/> values; 0 for every other piece.</param>
internal readonly record struct Piece(PieceKind Kind, int Material, int Index, int Variant = 0);

internal static class StripVariant
{
    /// <summary>Short strip, first speckle pattern.</summary>
    public const int PatternA = 0;
    /// <summary>Short strip, second speckle pattern.</summary>
    public const int PatternB = 1;
    /// <summary>Long strip that bulges in the middle and tapers to the short depth at both ends.</summary>
    public const int Long = 2;
}

/// <summary>
/// Decides which fringe pieces a path block needs from its neighbour key. The key holds one material id per
/// neighbour: sides N, E, S, W in slots 0-3, then the corners NE, SE, SW, NW (corner c lies between side c and side c+1) in slots 4-7.
/// </summary>
internal static class FringePlan
{
    /// <summary>Rotation about Y, in degrees, that carries a piece authored for the north side (or north-east corner) onto side or corner <c>index</c>.</summary>
    public static readonly int[] RotationY = { 0, 270, 180, 90 };

    // Pieces can overlap, so each sits on its own layer. From the bottom up:
    //   plain edges, by side (adjacent same-material strips overlap in their corner),
    //   concave edges, by side (each is drawn over the neighbouring side's strip; only when all four sides are
    //     concave does the cycle force one corner to be drawn the other way round, which is harmless),
    //   bleeds (which overlap their own strip, plain or concave).
    // Inner corners overlap nothing.
    private const int ConcaveLayer = 4;
    private const int BleedLayer = 8;

    public static int Layer(Piece piece)
    {
        switch (piece.Kind)
        {
            case PieceKind.Edge: return piece.Index;
            case PieceKind.EdgeConcave: return ConcaveLayer + piece.Index;
            case PieceKind.BleedEast:
            case PieceKind.BleedWest: return BleedLayer;
            default: return 0;
        }
    }

    public static int Side(long key, int side) => Slot(key, side);

    public static int Corner(long key, int corner) => Slot(key, 4 + corner);

    /// <summary>A block gets one long side when a d2 rolls 1 and a coin toss then comes up heads (1 block in 4).</summary>
    public const int LongSideDie = 2;

    /// <summary>
    /// Picks the per-block variation from the block's position, so it is the same on every redraw and on every client:
    /// a speckle pattern for each side, and possibly one long side chosen from the sides that have a fringe.
    /// Bits 0-2 hold the long side + 1 (0 = none); bits 3-6 hold the pattern of sides N, E, S, W.
    /// </summary>
    public static int ChooseVariation(long key, int x, int y, int z)
    {
        uint hash = Hash(x, y, z);
        int patterns = (int)(hash & 0xF);

        int longSide = -1;
        bool rolledOne = ((hash >> 4) & 0xFF) % LongSideDie == 0;
        bool heads = ((hash >> 12) & 1) == 1;
        if (rolledOne && heads)
        {
            int fringedSides = 0;
            for (int side = 0; side < 4; side++)
            {
                if (Side(key, side) != FringeMaterials.None) fringedSides++;
            }
            if (fringedSides > 0)
            {
                int pick = (int)((hash >> 16) % (uint)fringedSides);
                for (int side = 0; side < 4; side++)
                {
                    if (Side(key, side) == FringeMaterials.None) continue;
                    if (pick-- == 0) { longSide = side; break; }
                }
            }
        }

        return (longSide + 1) | (patterns << 3);
    }

    public static List<Piece> Build(long key, int variation)
    {
        var pieces = new List<Piece>();
        int longSide = (variation & 7) - 1;

        // Two fringed sides meet in the corner between them, where their strips overlap. If the materials differ,
        // the strip that comes second clockwise is drawn over the other one using its concave variant.
        for (int side = 0; side < 4; side++)
        {
            int material = Side(key, side);
            if (material == FringeMaterials.None) continue;

            int counterClockwiseSide = Side(key, (side + 3) % 4);
            bool concave = counterClockwiseSide != FringeMaterials.None && counterClockwiseSide != material;
            int variant = side == longSide ? StripVariant.Long : (variation >> (3 + side)) & 1;
            pieces.Add(new Piece(concave ? PieceKind.EdgeConcave : PieceKind.Edge, material, side, variant));
        }

        // Where a fringed side meets a differently-fringed diagonal, both blocks mix in the other's material at
        // half density, so the two strips agree on the shared boundary instead of changing abruptly.
        for (int side = 0; side < 4; side++)
        {
            int material = Side(key, side);
            if (material == FringeMaterials.None) continue;

            int clockwise = Corner(key, side);
            if (clockwise != FringeMaterials.None && clockwise != material && Side(key, (side + 1) % 4) == FringeMaterials.None)
            {
                pieces.Add(new Piece(PieceKind.BleedEast, clockwise, side));
            }

            int counterClockwiseCorner = (side + 3) % 4;
            int counterClockwise = Corner(key, counterClockwiseCorner);
            if (counterClockwise != FringeMaterials.None && counterClockwise != material && Side(key, counterClockwiseCorner) == FringeMaterials.None)
            {
                pieces.Add(new Piece(PieceKind.BleedWest, counterClockwise, side));
            }
        }

        for (int corner = 0; corner < 4; corner++)
        {
            int diagonal = Corner(key, corner);
            if (diagonal == FringeMaterials.None) continue;
            if (Side(key, corner) != FringeMaterials.None || Side(key, (corner + 1) % 4) != FringeMaterials.None) continue;
            pieces.Add(new Piece(PieceKind.Inner, diagonal, corner));
        }

        return pieces;
    }

    private static uint Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h;
        }
    }

    private static int Slot(long key, int slot) => (int)((key >> (slot * FringeMaterials.Bits)) & ((1 << FringeMaterials.Bits) - 1));
}
