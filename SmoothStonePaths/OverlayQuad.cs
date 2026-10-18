using System.Collections.Generic;

namespace SmoothStonePaths;

/// <summary>
/// Shape of the grass overlay quad. The overlay is lifted a hair above the block top, and seen from a low angle that
/// lift shifts it away from the camera, exposing a sliver of the block's own texture along the edges nearest to the
/// camera. On every side that borders grass the overlay therefore reaches 1 px out over the neighbouring grass block, so
/// the sliver is covered; the extra strip is grass over grass and does not show.
/// </summary>
/// <remarks>
/// The strip is real texture, built into the overlay image as a margin of mirrored texels, and the quad's texture
/// coordinates always stay inside that image. The engine's shape tessellator clips a face's far texture edge to the end
/// of the texture, so coordinates that run past it would silently squeeze the texture across the quad.
/// </remarks>
internal static class OverlayQuad
{
    /// <summary>Texels of margin around the overlay image, on every side.</summary>
    public const int MarginTexels = 2;

    /// <summary>How far the overlay reaches past a side that has grass beside it, in block pixels (a texel is half a pixel).</summary>
    public const float MarginPixels = MarginTexels / 2f;

    /// <summary>Width of the whole overlay image in block pixels: the block top plus a margin on both sides.</summary>
    public const float ImagePixels = 16f + 2f * MarginPixels;

    /// <summary>Which of the sides N, E, S, W have a grass neighbour whose fringe the overlay draws.</summary>
    public static bool[] SidesToExtend(IEnumerable<Piece> pieces)
    {
        var extend = new bool[4];
        foreach (Piece piece in pieces)
        {
            bool strip = piece.Kind == PieceKind.Edge || piece.Kind == PieceKind.EdgeConcave;
            if (strip && piece.Material == FringeMaterials.Grass) extend[piece.Index] = true;
        }
        return extend;
    }

    /// <summary>The quad's corners in block pixels (x east, z south) for the sides to extend.</summary>
    public static (float X0, float Z0, float X1, float Z1) Rect(bool[] extend)
    {
        return (extend[3] ? -MarginPixels : 0f, extend[0] ? -MarginPixels : 0f,
                extend[1] ? 16f + MarginPixels : 16f, extend[2] ? 16f + MarginPixels : 16f);
    }

    /// <summary>
    /// Texture coordinates for a quad of those corners, in the shape's 0..16 units spanning the whole overlay image.
    /// A block position of -1 px is the image's first texel and 17 px its last, so every value stays within 0..16.
    /// </summary>
    public static (float U0, float V0, float U1, float V1) Uv((float X0, float Z0, float X1, float Z1) rect)
    {
        return (ToUv(rect.X0), ToUv(rect.Z0), ToUv(rect.X1), ToUv(rect.Z1));
    }

    private static float ToUv(float blockPixel) => (blockPixel + MarginPixels) / ImagePixels * 16f;

    /// <summary>
    /// A copy of the overlay shape set up for these sides: tinted by the local climate and season, like the grass
    /// around it, and sized and mapped as above. The shape the engine loaded is left as it was.
    /// </summary>
    public static Vintagestory.API.Common.Shape Configure(Vintagestory.API.Common.Shape source, bool[] extend)
    {
        Vintagestory.API.Common.Shape shape = source.Clone();
        var rect = Rect(extend);
        var uv = Uv(rect);
        foreach (Vintagestory.API.Common.ShapeElement element in shape.Elements)
        {
            element.ClimateColorMap = "climatePlantTint";
            element.SeasonColorMap = "seasonalGrass";
            element.From = new[] { (double)rect.X0, element.From[1], (double)rect.Z0 };
            element.To = new[] { (double)rect.X1, element.To[1], (double)rect.Z1 };
            element.FacesResolved[4].Uv = new[] { uv.U0, uv.V0, uv.U1, uv.V1 };
        }
        return shape;
    }
}
