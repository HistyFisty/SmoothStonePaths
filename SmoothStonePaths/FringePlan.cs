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
internal readonly record struct Piece(PieceKind Kind, int Material, int Index);

/// <summary>
/// Decides which fringe pieces a path block needs from its neighbour key. The key holds one material id per
/// neighbour: sides N, E, S, W in slots 0-3, then the corners NE, SE, SW, NW (corner c lies between side c and side c+1) in slots 4-7.
/// </summary>
internal static class FringePlan
{
    public static int Side(long key, int side) => Slot(key, side);

    public static int Corner(long key, int corner) => Slot(key, 4 + corner);

    public static List<Piece> Build(long key)
    {
        var pieces = new List<Piece>();

        // Two fringed sides meet in the corner between them, where their strips overlap. If the materials differ,
        // the strip that comes second clockwise is drawn over the other one using its concave variant.
        for (int side = 0; side < 4; side++)
        {
            int material = Side(key, side);
            if (material == FringeMaterials.None) continue;

            int counterClockwiseSide = Side(key, (side + 3) % 4);
            bool concave = counterClockwiseSide != FringeMaterials.None && counterClockwiseSide != material;
            pieces.Add(new Piece(concave ? PieceKind.EdgeConcave : PieceKind.Edge, material, side));
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

    private static int Slot(long key, int slot) => (int)((key >> (slot * FringeMaterials.Bits)) & ((1 << FringeMaterials.Bits) - 1));
}
