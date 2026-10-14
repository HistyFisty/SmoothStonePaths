using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Client.Tesselation;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SmoothStonePaths;

public class BlockBlendedPath : Block
{
    // TileSideEnum order is N, E, S, W; pieces are authored for north / north-east.
    private static readonly int[] RotationY = { 0, 270, 180, 90 };

    private static string ShapeName(PieceKind kind)
    {
        switch (kind)
        {
            case PieceKind.BleedEast: return "bleed-east";
            case PieceKind.BleedWest: return "bleed-west";
            case PieceKind.Inner: return "inner";
            default: return "edge"; // concave strips share the plain strip's shape; only their texture differs
        }
    }

    // Fringe pieces are depth-written in the Decor pass and can overlap, so overlapping pieces must not be coplanar
    // or they z-fight. Each piece sits on its own layer, from the bottom up:
    //   plain edges, by side (adjacent same-material strips overlap in their corner),
    //   concave edges, by side (each is drawn over the neighbouring side's strip; only when all four sides are
    //     concave does the cycle force one corner to be drawn the other way round, which is harmless),
    //   bleeds (which overlap their own strip, plain or concave).
    // Inner corners overlap nothing.
    private const float LayerStep = 1f / 256f;
    private const int ConcaveLayer = 4;
    private const int BleedLayer = 8;

    internal static int Layer(Piece piece)
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

    private ICoreClientAPI capi;
    private readonly object cacheLock = new object();
    private readonly Dictionary<Piece, MeshData> pieces = new Dictionary<Piece, MeshData>();
    private readonly Dictionary<(MeshData baseMesh, long key), MeshData> composites = new Dictionary<(MeshData, long), MeshData>();

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        capi = api as ICoreClientAPI;

        // The path's own shape has no color map, so without this the tessellator would not sample the
        // local climate and the grass fringe could not be tinted to match the surrounding grass.
        LoadColorMapAnyway = true;
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

        if (capi == null) return;
        if (Variant.TryGetValue("cover", out string cover) && cover == "snow") return;

        long key = ComputeKey(chunkExtBlocks, extIndex3d);
        if (key == 0) return;

        lock (cacheLock)
        {
            var cacheKey = (sourceMesh, key);
            if (!composites.TryGetValue(cacheKey, out MeshData composite))
            {
                composite = Compose(sourceMesh, key);
                composites[cacheKey] = composite;
            }
            sourceMesh = composite;
        }
    }

    // See FringePlan for the key layout.
    internal static long ComputeKey(Block[] chunkExtBlocks, int extIndex3d)
    {
        int[] move = TileSideEnum.MoveIndex;
        long key = 0;
        for (int side = 0; side < 4; side++)
        {
            key |= (long)FringeMaterials.Classify(At(chunkExtBlocks, extIndex3d + move[side])) << (side * FringeMaterials.Bits);
        }
        for (int corner = 0; corner < 4; corner++)
        {
            int offset = move[corner] + move[(corner + 1) % 4];
            key |= (long)FringeMaterials.Classify(At(chunkExtBlocks, extIndex3d + offset)) << ((4 + corner) * FringeMaterials.Bits);
        }
        return key;
    }

    private MeshData Compose(MeshData baseMesh, long key)
    {
        MeshData result = null;
        foreach (Piece piece in FringePlan.Build(key))
        {
            MeshData mesh = GetPiece(piece);
            if (mesh == null) continue;
            result ??= baseMesh.Clone();
            result.AddMeshData(mesh);
        }
        return result ?? baseMesh;
    }

    private MeshData GetPiece(Piece piece)
    {
        if (pieces.TryGetValue(piece, out MeshData cached)) return cached;

        MeshData mesh = null;
        string textureCode = FringeMaterials.TextureCodes[piece.Material];
        if (piece.Kind == PieceKind.EdgeConcave) textureCode += FringeMaterials.ConcaveSuffix;
        TextureAtlasPosition texture = capi.BlockTextureAtlas.GetPosition(this, textureCode, true);
        var location = new AssetLocation("smoothstonepaths", "shapes/block/fringe/" + ShapeName(piece.Kind) + ".json");
        // "Shape" alone would bind to Block.Shape (a CompositeShape), so the type is fully qualified.
        Vintagestory.API.Common.Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, location);
        if (texture == null)
        {
            capi.Logger.Warning("[smoothstonepaths] Fringe texture {0} missing on {1}", textureCode, Code);
        }
        else if (shape == null)
        {
            capi.Logger.Warning("[smoothstonepaths] Fringe shape {0} not found", location);
        }
        else
        {
            if (FringeMaterials.IsTinted(piece.Material))
            {
                shape = shape.Clone();
                foreach (ShapeElement element in shape.Elements)
                {
                    element.ClimateColorMap = "climatePlantTint";
                    element.SeasonColorMap = "seasonalGrass";
                }
            }
            capi.Tesselator.TesselateShape("smoothstonepaths", shape, out mesh, new FringeTextureSource(capi, texture), new Vec3f(0f, RotationY[piece.Index], 0f));
            mesh.Translate(0f, Layer(piece) * LayerStep, 0f);
        }

        pieces[piece] = mesh;
        return mesh;
    }

    // The inside-block renderer calls this hook with a sparsely filled array, so nulls and bad indices are possible.
    private static Block At(Block[] blocks, int index)
    {
        return (uint)index < (uint)blocks.Length ? blocks[index] : null;
    }

    /// <summary>Resolves every texture code in a fringe shape to one atlas position.</summary>
    private sealed class FringeTextureSource : ITexPositionSource
    {
        private readonly ICoreClientAPI capi;
        private readonly TextureAtlasPosition texture;

        public FringeTextureSource(ICoreClientAPI capi, TextureAtlasPosition texture)
        {
            this.capi = capi;
            this.texture = texture;
        }

        public TextureAtlasPosition this[string textureCode] => texture;

        public Size2i AtlasSize => capi.BlockTextureAtlas.Size;
    }
}
