using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SmoothStonePaths;

/// <summary>
/// A block that draws a fringe of its neighbours' grass, sand, gravel or dirt over the edges of its top surface.
/// Subclasses decide which neighbours it accepts, where its surface is and when it shows the fringe at all.
/// </summary>
public abstract class BlockFringed : Block
{
    /// <summary>How far this block's top sits above a lowered path's top (15/16), in blocks.</summary>
    protected virtual float SurfaceLift => 0f;

    /// <summary>Whether this block shows the fringe of a neighbour with this material id.</summary>
    protected virtual bool AcceptsFringe(int material) => true;

    /// <summary>Whether this block shows any fringe right now, for example not while it is covered.</summary>
    protected virtual bool ShowsFringe(Block[] chunkExtBlocks, int extIndex3d) => true;

    private FringeRenderer renderer;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        renderer = new FringeRenderer(this, SurfaceLift, AcceptsFringe, ShowsFringe);
        renderer.Load(api);
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
        renderer.Tesselate(ref sourceMesh, pos, chunkExtBlocks, extIndex3d);
    }

    internal static string ShapeName(Piece piece) => FringeRenderer.ShapeName(piece);

    internal static long ComputeKey(Block[] chunkExtBlocks, int extIndex3d) => FringeRenderer.ComputeKey(chunkExtBlocks, extIndex3d);

    internal static long ComputeKey(Block[] chunkExtBlocks, int extIndex3d, System.Func<int, bool> accepts) => FringeRenderer.ComputeKey(chunkExtBlocks, extIndex3d, accepts);

    protected static bool IsCovered(Block[] chunkExtBlocks, int index) => FringeRenderer.IsCovered(chunkExtBlocks, index);

    protected static Block At(Block[] blocks, int index) => FringeRenderer.At(blocks, index);
}
