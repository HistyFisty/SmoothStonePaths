using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SmoothStonePaths;

/// <summary>Resolves every texture code in a fringe shape to one atlas position.</summary>
internal sealed class FringeTextureSource : ITexPositionSource
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

/// <summary>
/// Turns a block's fringe pieces into one texture that replaces the block's top face (everything but grass) and, if any
/// grass shows, one tinted overlay quad. Textures are built on the CPU and added to the block atlas the first time a
/// combination is seen. Anything unexpected makes it give up, and the block falls back to drawing each piece as a quad.
/// </summary>
internal sealed class FringeBaker
{
    // Texels of repeated edge texels around every image in the atlas, so sampling just outside it stays clear of its neighbours.
    private const int AtlasBorder = 1;

    private readonly ICoreClientAPI capi;
    private readonly Block block;
    private readonly FringeAssetSources sources;
    private List<(int subId, AssetLocation name)> topTextures;
    private readonly Dictionary<int, int[]> baseTextures = new Dictionary<int, int[]>();

    public FringeBaker(ICoreClientAPI capi, Block block, FringeAssetSources sources)
    {
        this.capi = capi;
        this.block = block;
        this.sources = sources;
    }

    /// <summary>
    /// The block mesh with the non-grass fringe baked into its top face and the grass added as one tinted overlay, or
    /// null with the reason in <paramref name="failure"/> if this mesh cannot be handled that way.
    /// </summary>
    public MeshData TryBake(MeshData baseMesh, IReadOnlyList<Piece> pieces, float surfaceLift, out string failure)
    {
        failure = null;
        bool hasNonGrass = pieces.Any(p => p.Material != FringeMaterials.Grass);

        int face = -1, baseSubId = 0;
        TopFace.Mapping mapping = default;
        int[] baseTop = new int[FringeCompositor.Size * FringeCompositor.Size];
        if (hasNonGrass)
        {
            face = TopFace.Find(baseMesh.xyz, baseMesh.Flags, baseMesh.VerticesCount, baseMesh.VerticesPerFace);
            if (face < 0) { failure = "the block mesh has no full-size top face"; return null; }
            if (IsColorMapped(baseMesh, face)) { failure = "the top face is tinted by a colour map"; return null; }
            if (!TryFindBaseTexture(baseMesh, face, out baseSubId, out TextureAtlasPosition basePos, out AssetLocation baseName))
            {
                failure = "could not tell which texture the top face uses";
                return null;
            }
            if (!TopFace.TryGetMapping(baseMesh.xyz, baseMesh.Uv, face, basePos.x1, basePos.y1, basePos.x2, basePos.y2, out mapping))
            {
                failure = "could not read how the top texture is laid over the face";
                return null;
            }
            baseTop = TopFace.Orient(LoadBase(baseSubId, baseName), mapping);
        }

        FringeImage image = FringeCompositor.Compose(pieces, baseTop, sources);

        MeshData result = baseMesh;
        if (hasNonGrass && !image.Baked.AsSpan().SequenceEqual(baseTop))
        {
            string path = "bake/" + baseSubId + "_" + Signature(mapping) + "/" + Describe(pieces.Where(p => p.Material != FringeMaterials.Grass));
            if (!Insert(path, image.Baked, FringeCompositor.Size, out TextureAtlasPosition baked)) { failure = "could not add the baked top texture to the atlas"; return null; }
            result = baseMesh.Clone();
            SetTopTexture(result, face, baked);
        }

        if (image.Grass != null)
        {
            int[] overlayImage = FringeCompositor.WithMirroredMargin(image.Grass, OverlayQuad.MarginTexels);
            if (!Insert("overlay/" + Describe(pieces), overlayImage, FringeCompositor.Size + 2 * OverlayQuad.MarginTexels, out TextureAtlasPosition overlay)) { failure = "could not add the grass overlay texture to the atlas"; return null; }
            MeshData overlayMesh = OverlayMesh(overlay, surfaceLift, OverlayQuad.SidesToExtend(pieces));
            if (overlayMesh == null) { failure = "the grass overlay shape is missing"; return null; }
            if (ReferenceEquals(result, baseMesh)) result = baseMesh.Clone();
            result.AddMeshData(overlayMesh);
        }
        return result;
    }

    private static bool IsColorMapped(MeshData mesh, int face)
    {
        return (mesh.ClimateColorMapIds != null && face < mesh.ClimateColorMapIds.Length && mesh.ClimateColorMapIds[face] != 0)
            || (mesh.SeasonColorMapIds != null && face < mesh.SeasonColorMapIds.Length && mesh.SeasonColorMapIds[face] != 0);
    }

    // The base mesh only holds atlas coordinates, so the texture behind the top face is found by which of the block's
    // own textures contains them.
    private bool TryFindBaseTexture(MeshData mesh, int face, out int subId, out TextureAtlasPosition position, out AssetLocation name)
    {
        subId = 0; position = null; name = null;
        if (mesh.Uv == null || mesh.TextureIndices == null || face >= mesh.TextureIndices.Length) return false;

        topTextures ??= CollectTextures();
        int page = mesh.TextureIds[mesh.TextureIndices[face]];
        float u = 0, v = 0;
        for (int i = 0; i < 4; i++)
        {
            u += mesh.Uv[(face * 4 + i) * 2];
            v += mesh.Uv[(face * 4 + i) * 2 + 1];
        }
        u /= 4; v /= 4;

        TextureAtlasPosition[] positions = capi.BlockTextureAtlas.Positions;
        foreach ((int id, AssetLocation texture) in topTextures)
        {
            if (id < 0 || id >= positions.Length) continue;
            TextureAtlasPosition candidate = positions[id];
            if (candidate == null || candidate.atlasTextureId != page || !candidate.ContainsUV(u, v)) continue;
            subId = id; position = candidate; name = texture;
            return true;
        }
        return false;
    }

    private List<(int, AssetLocation)> CollectTextures()
    {
        var list = new List<(int, AssetLocation)>();
        var seen = new HashSet<BakedCompositeTexture>(ReferenceEqualityComparer.Instance);
        foreach (CompositeTexture texture in block.Textures.Values) Flatten(texture.Baked, list, seen);
        return list;
    }

    /// <summary>
    /// Lists a baked texture and all its variants and tiles. The graph is not a tree: the engine makes the first variant
    /// of a texture the texture itself, so each object is visited once or this would never finish.
    /// </summary>
    internal static void Flatten(BakedCompositeTexture baked, List<(int, AssetLocation)> into, HashSet<BakedCompositeTexture> seen)
    {
        if (baked == null || !seen.Add(baked)) return;
        if (baked.BakedName != null) into.Add((baked.TextureSubId, baked.BakedName));
        if (baked.BakedVariants != null) foreach (BakedCompositeTexture variant in baked.BakedVariants) Flatten(variant, into, seen);
        if (baked.BakedTiles != null) foreach (BakedCompositeTexture tile in baked.BakedTiles) Flatten(tile, into, seen);
    }

    private int[] LoadBase(int subId, AssetLocation name)
    {
        if (baseTextures.TryGetValue(subId, out int[] cached)) return cached;

        IBitmap bitmap = capi.BlockTextureAtlas.LoadCompositeBitmap(new AssetLocationAndSource(name));
        try
        {
            if (bitmap == null || bitmap.Width != FringeCompositor.Size || bitmap.Height != FringeCompositor.Size)
            {
                throw new InvalidOperationException("base texture " + name + " is not 32x32");
            }
            int[] pixels = bitmap.Pixels;
            baseTextures[subId] = pixels;
            return pixels;
        }
        finally
        {
            (bitmap as IDisposable)?.Dispose();
        }
    }

    /// <summary>Points the top face at a baked texture: block position (x, z) maps straight to texture position.</summary>
    private static void SetTopTexture(MeshData mesh, int face, TextureAtlasPosition inner)
    {
        SetTopUv(mesh.xyz, mesh.Uv, face, inner.x1, inner.y1, inner.x2, inner.y2);
        mesh.TextureIndices[face] = PageSlot(ref mesh.TextureIds, inner.atlasTextureId);
    }

    internal static void SetTopUv(float[] xyz, float[] uv, int face, float x1, float y1, float x2, float y2)
    {
        for (int i = 0; i < 4; i++)
        {
            int vertex = face * 4 + i;
            uv[vertex * 2] = x1 + xyz[vertex * 3] * (x2 - x1);
            uv[vertex * 2 + 1] = y1 + xyz[vertex * 3 + 2] * (y2 - y1);
        }
    }

    /// <summary>The index of an atlas page in a mesh's page list, adding it if the mesh does not use that page yet.</summary>
    internal static byte PageSlot(ref int[] textureIds, int page)
    {
        int slot = Array.IndexOf(textureIds, page);
        if (slot < 0)
        {
            slot = textureIds.Length;
            Array.Resize(ref textureIds, slot + 1);
            textureIds[slot] = page;
        }
        return (byte)slot;
    }

    private MeshData OverlayMesh(TextureAtlasPosition texture, float surfaceLift, bool[] extend)
    {
        var location = new AssetLocation("smoothstonepaths", "shapes/block/fringe/overlay.json");
        Vintagestory.API.Common.Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, location);
        if (shape == null) return null;

        capi.Tesselator.TesselateShape("smoothstonepaths", OverlayQuad.Configure(shape, extend), out MeshData mesh, new FringeTextureSource(capi, texture));
        mesh.Translate(0f, surfaceLift, 0f);
        return mesh;
    }

    /// <summary>
    /// Adds a square image to the block atlas under this name, once. It is stored with a border of repeated edge texels
    /// around it, and the returned position is the image itself without that border.
    /// </summary>
    private bool Insert(string path, int[] image, int size, out TextureAtlasPosition inner)
    {
        inner = null;
        int allocated = size + 2 * AtlasBorder;
        int[] padded = FringeCompositor.WithBorder(image, size, AtlasBorder);
        bool added = capi.BlockTextureAtlas.GetOrInsertTexture(new AssetLocation("smoothstonepaths", path), out _, out TextureAtlasPosition position, () => ToBitmap(padded, allocated));
        if (!added || position == null) return false;

        float atlasWidth = capi.BlockTextureAtlas.Size.Width, atlasHeight = capi.BlockTextureAtlas.Size.Height;
        if (Math.Abs((position.x2 - position.x1) * atlasWidth - allocated) > 0.5f || Math.Abs((position.y2 - position.y1) * atlasHeight - allocated) > 0.5f) return false;

        inner = new TextureAtlasPosition
        {
            atlasTextureId = position.atlasTextureId,
            atlasNumber = position.atlasNumber,
            reloadIteration = position.reloadIteration,
            x1 = position.x1 + AtlasBorder / atlasWidth,
            y1 = position.y1 + AtlasBorder / atlasHeight,
            x2 = position.x2 - AtlasBorder / atlasWidth,
            y2 = position.y2 - AtlasBorder / atlasHeight
        };
        return true;
    }

    private static IBitmap ToBitmap(int[] argb, int size)
    {
        var bitmap = new BitmapExternal(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bitmap.bmp.SetPixel(x, y, new SKColor((uint)argb[y * size + x]));
            }
        }
        return bitmap;
    }

    // Names for the atlas cache: the same pieces always give the same name, and different pieces never do.
    private static string Describe(IEnumerable<Piece> pieces)
    {
        return string.Join("_", pieces
            .OrderBy(FringePlan.Layer).ThenBy(p => p.Kind).ThenBy(p => p.Material).ThenBy(p => p.Index).ThenBy(p => p.Variant)
            .Select(p => (int)p.Kind + "-" + p.Material + "-" + p.Index + "-" + p.Variant));
    }

    private static string Signature(TopFace.Mapping m)
    {
        return string.Join("_", new[] { m.U00, m.V00, m.U10, m.V10, m.U01, m.V01 }.Select(f => ((int)Math.Round(f * 1000f)).ToString("x")));
    }
}
