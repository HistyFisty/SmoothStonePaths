using System.Collections.Generic;
using System.Linq;

namespace SmoothStonePaths;

/// <summary>
/// Where a piece sits on the block top and where its texels live, in texels of the 32x32 block top authored for the
/// north side (or north-east corner): a piece covers x in [X0, X1) and z in [Z0, Z1), and the texel at (X0, Z0) is
/// the texel at (U0, V0) in the mask and in the material texture.
/// </summary>
internal readonly record struct PieceGeometry(int X0, int Z0, int X1, int Z1, int U0, int V0)
{
    /// <summary>
    /// Reads a fringe shape: one flat element whose up face shows a same-sized region of the mask. Shape units are
    /// pixels of a 16-pixel block, and the 32x32 mask holds two texels per pixel. Null if the shape is not of that form.
    /// </summary>
    public static PieceGeometry? FromShape(Vintagestory.API.Common.Shape shape)
    {
        if (shape?.Elements == null || shape.Elements.Length != 1) return null;
        Vintagestory.API.Common.ShapeElement element = shape.Elements[0];
        Vintagestory.API.Common.ShapeElementFace up = element.FacesResolved?[4];
        if (up?.Uv == null || up.Uv.Length != 4 || element.From == null || element.To == null) return null;

        var geometry = new PieceGeometry(
            (int)System.Math.Round(element.From[0] * 2), (int)System.Math.Round(element.From[2] * 2),
            (int)System.Math.Round(element.To[0] * 2), (int)System.Math.Round(element.To[2] * 2),
            (int)System.Math.Round(up.Uv[0] * 2), (int)System.Math.Round(up.Uv[1] * 2));

        int width = geometry.X1 - geometry.X0, depth = geometry.Z1 - geometry.Z0;
        bool sameSize = (int)System.Math.Round((up.Uv[2] - up.Uv[0]) * 2) == width && (int)System.Math.Round((up.Uv[3] - up.Uv[1]) * 2) == depth;
        bool inside = geometry.X0 >= 0 && geometry.Z0 >= 0 && geometry.X1 <= Size32 && geometry.Z1 <= Size32
            && geometry.U0 >= 0 && geometry.V0 >= 0 && geometry.U0 + width <= Size32 && geometry.V0 + depth <= Size32;
        return sameSize && inside && width > 0 && depth > 0 ? geometry : null;
    }

    private const int Size32 = 32;
}

/// <summary>Everything the compositor reads: the masks, the material textures and the piece geometry.</summary>
internal interface IFringeSources
{
    /// <summary>32x32 ARGB mask texels, row-major. Alpha 0 keeps the material texel, anything else cuts it.</summary>
    int[] Mask(bool concave);

    /// <summary>32x32 ARGB texels of the vanilla texture for a fringe material id.</summary>
    int[] Material(int material);

    PieceGeometry Geometry(Piece piece);
}

/// <summary>The block top split into what is baked into its own texture and what must stay a tinted overlay.</summary>
internal sealed class FringeImage
{
    /// <summary>The block's top with every non-grass fringe painted in. 32x32 ARGB in block space, x east and z south.</summary>
    public int[] Baked;

    /// <summary>
    /// The grass that ends up visible, 32x32 ARGB in block space; 0 where no grass shows. Null when none does.
    /// Grass is tinted live by season, frost and climate, so it cannot be baked and is drawn as a tinted overlay.
    /// </summary>
    public int[] Grass;
}

/// <summary>
/// Resolves a block's fringe pieces into texels. Pieces are painted from the bottom layer up, exactly as the stacked
/// quads would draw, so the result looks the same without any piece sitting above the surface.
/// </summary>
internal static class FringeCompositor
{
    public const int Size = 32;

    public static FringeImage Compose(IReadOnlyList<Piece> pieces, int[] baseTop, IFringeSources sources)
    {
        int[] nonGrass = new int[Size * Size];
        int[] grass = new int[Size * Size];
        bool[] grassOnTop = new bool[Size * Size];

        foreach (Piece piece in pieces.OrderBy(FringePlan.Layer))
        {
            PieceGeometry geometry = sources.Geometry(piece);
            int[] mask = sources.Mask(piece.Kind == PieceKind.EdgeConcave);
            int[] material = sources.Material(piece.Material);
            bool isGrass = piece.Material == FringeMaterials.Grass;

            for (int az = geometry.Z0; az < geometry.Z1; az++)
            {
                for (int ax = geometry.X0; ax < geometry.X1; ax++)
                {
                    int texel = (geometry.V0 + az - geometry.Z0) * Size + (geometry.U0 + ax - geometry.X0);
                    if (((mask[texel] >> 24) & 0xFF) != 0) continue;
                    int color = material[texel];
                    if (((color >> 24) & 0xFF) == 0) continue;

                    ToWorld(piece.Index, ax, az, out int wx, out int wz);
                    int at = wz * Size + wx;
                    if (isGrass) grass[at] = color; else nonGrass[at] = color;
                    grassOnTop[at] = isGrass;
                }
            }
        }

        var image = new FringeImage { Baked = new int[Size * Size] };
        bool anyGrass = false;
        for (int i = 0; i < image.Baked.Length; i++)
        {
            image.Baked[i] = nonGrass[i] != 0 ? nonGrass[i] : baseTop[i];
            if (grassOnTop[i]) anyGrass = true;
        }

        if (anyGrass)
        {
            image.Grass = new int[Size * Size];
            for (int i = 0; i < image.Grass.Length; i++)
            {
                if (grassOnTop[i]) image.Grass[i] = grass[i];
            }
        }
        return image;
    }

    /// <summary>Maps a texel authored for the north side onto the side or corner <c>index</c> (see FringePlan.RotationY).</summary>
    internal static void ToWorld(int index, int ax, int az, out int wx, out int wz)
    {
        switch (index)
        {
            case 0: wx = ax; wz = az; break;
            case 1: wx = Size - 1 - az; wz = ax; break;
            case 2: wx = Size - 1 - ax; wz = Size - 1 - az; break;
            default: wx = az; wz = Size - 1 - ax; break;
        }
    }

    /// <summary>
    /// Copies a square image into the middle of a larger one and repeats its edge texels around it, <c>border</c> texels
    /// deep, so sampling just outside the image never picks up a neighbouring texture.
    /// </summary>
    internal static int[] WithBorder(int[] image, int size, int border)
    {
        int padded = size + 2 * border;
        int[] result = new int[padded * padded];
        for (int y = 0; y < padded; y++)
        {
            int sy = System.Math.Clamp(y - border, 0, size - 1);
            for (int x = 0; x < padded; x++)
            {
                int sx = System.Math.Clamp(x - border, 0, size - 1);
                result[y * padded + x] = image[sy * size + sx];
            }
        }
        return result;
    }

    /// <summary>
    /// Copies a 32x32 image into the middle of a larger one and continues it outwards by mirroring, <c>margin</c> texels
    /// deep. Unlike repeating the edge texels this reads as texture, not as streaks.
    /// </summary>
    internal static int[] WithMirroredMargin(int[] image, int margin)
    {
        int padded = Size + 2 * margin;
        int[] result = new int[padded * padded];
        for (int y = 0; y < padded; y++)
        {
            int sy = Mirror(y - margin);
            for (int x = 0; x < padded; x++)
            {
                result[y * padded + x] = image[sy * Size + Mirror(x - margin)];
            }
        }
        return result;
    }

    // -1 -> 0 and -2 -> 1 on the low side; 32 -> 31 and 33 -> 30 on the high side.
    private static int Mirror(int i) => i < 0 ? -i - 1 : (i >= Size ? 2 * Size - 1 - i : i);
}
