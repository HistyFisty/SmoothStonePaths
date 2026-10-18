using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SmoothStonePaths;

/// <summary>
/// Natural soil that shows the grass fringe of its neighbours while it is bare. Soil must stay a <see cref="BlockSoil"/>
/// for growth, spreading and decay, so the fringe comes from a composed renderer rather than the BlockFringed base class.
/// Only patched in when terrain blending is turned on in the mod config.
/// </summary>
public class BlockBlendedSoil : BlockSoil
{
    private FringeRenderer renderer;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        renderer = new FringeRenderer(this, 1f / 16f, material => material == FringeMaterials.Grass, ShowsFringe);
        renderer.Load(api);
    }

    // Soil with grass on it is drawn by its own top-soil renderer and already looks like grass; only bare soil is
    // patched to be drawn from a shape, and a covered top is hidden.
    private bool ShowsFringe(Block[] chunkExtBlocks, int extIndex3d)
    {
        return Variant.TryGetValue("grasscoverage", out string coverage) && coverage == "none"
            && !FringeRenderer.IsCovered(chunkExtBlocks, extIndex3d);
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);
        renderer.Tesselate(ref sourceMesh, pos, chunkExtBlocks, extIndex3d);
    }
}
