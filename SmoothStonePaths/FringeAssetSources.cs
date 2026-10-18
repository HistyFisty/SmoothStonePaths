using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SmoothStonePaths;

/// <summary>
/// Reads the masks, the material textures and the piece shapes from the game's assets, each only once. One instance
/// is shared by every fringed block; <see cref="Generation"/> changes when the textures are reloaded, so blocks know
/// to drop what they built from the old ones.
/// </summary>
internal sealed class FringeAssetSources : IFringeSources
{
    private static FringeAssetSources instance;

    private readonly ICoreClientAPI capi;
    private readonly object sync = new object();
    private readonly Dictionary<bool, int[]> masks = new Dictionary<bool, int[]>();
    private readonly Dictionary<int, int[]> materials = new Dictionary<int, int[]>();
    private readonly Dictionary<(PieceKind, int), PieceGeometry> geometries = new Dictionary<(PieceKind, int), PieceGeometry>();

    /// <summary>Counts texture reloads.</summary>
    public int Generation { get; private set; }

    private FringeAssetSources(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.ReloadTextures += () =>
        {
            lock (sync)
            {
                masks.Clear();
                materials.Clear();
                geometries.Clear();
                Generation++;
            }
        };
    }

    public static FringeAssetSources For(ICoreClientAPI capi)
    {
        lock (typeof(FringeAssetSources))
        {
            if (instance == null || instance.capi != capi) instance = new FringeAssetSources(capi);
            return instance;
        }
    }

    public int[] Mask(bool concave)
    {
        lock (sync)
        {
            if (!masks.TryGetValue(concave, out int[] mask))
            {
                mask = Load(new AssetLocation("smoothstonepaths", "textures/block/fringe/" + (concave ? "cutout-concave" : "cutout") + ".png"));
                masks[concave] = mask;
            }
            return mask;
        }
    }

    public int[] Material(int material)
    {
        lock (sync)
        {
            if (!materials.TryGetValue(material, out int[] pixels))
            {
                var path = new AssetLocation(FringeMaterials.TexturePath(material));
                pixels = Load(new AssetLocation(path.Domain, "textures/" + path.Path + ".png"));
                materials[material] = pixels;
            }
            return pixels;
        }
    }

    public PieceGeometry Geometry(Piece piece)
    {
        lock (sync)
        {
            var key = (piece.Kind, piece.Variant);
            if (!geometries.TryGetValue(key, out PieceGeometry geometry))
            {
                var location = new AssetLocation("smoothstonepaths", "shapes/block/fringe/" + BlockFringed.ShapeName(piece) + ".json");
                // "Shape" alone would bind to Block.Shape (a CompositeShape) inside a block, so the type is fully qualified here too.
                Vintagestory.API.Common.Shape shape = Vintagestory.API.Common.Shape.TryGet(capi, location);
                PieceGeometry? read = PieceGeometry.FromShape(shape);
                if (read == null) throw new InvalidOperationException("Fringe shape " + location + " is missing or not in the expected form");
                geometry = read.Value;
                geometries[key] = geometry;
            }
            return geometry;
        }
    }

    private int[] Load(AssetLocation location)
    {
        IAsset asset = capi.Assets.TryGet(location);
        if (asset == null) throw new InvalidOperationException("Asset " + location + " not found");
        using (BitmapRef bitmap = asset.ToBitmap(capi))
        {
            if (bitmap.Width != FringeCompositor.Size || bitmap.Height != FringeCompositor.Size)
            {
                throw new InvalidOperationException("Asset " + location + " is " + bitmap.Width + "x" + bitmap.Height + ", not 32x32");
            }
            return bitmap.Pixels;
        }
    }
}
