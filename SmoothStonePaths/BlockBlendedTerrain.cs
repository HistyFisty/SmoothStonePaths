using Vintagestory.API.Common;

namespace SmoothStonePaths;

/// <summary>
/// Natural sand, gravel, soil, rock or another block patched in for it that shows the fringe of the terrain that flows onto it: grass onto all of them,
/// and sand onto gravel. Only patched in when terrain blending is turned on in the mod config.
/// </summary>
public class BlockBlendedTerrain : BlockFringed
{
    // A full block's top is 1/16 above a lowered path's top, where the fringe pieces are authored.
    protected override float SurfaceLift => 1f / 16f;

    protected override bool AcceptsFringe(int material)
    {
        // Devastated soil and rock are not sand, gravel or soil by material, so they are recognised by their code.
        string kind = Code?.FirstCodePart();
        if (kind == "devastatedsoil" || kind == "rock") return material == FringeMaterials.Grass;
        return FringeMaterials.FlowsOnto(material, BlockMaterial);
    }

    // Under another block the top is hidden, and most sand and gravel is underground, so this also skips the
    // neighbour checks for nearly all of it.
    protected override bool ShowsFringe(Block[] chunkExtBlocks, int extIndex3d) => !IsCovered(chunkExtBlocks, extIndex3d);
}
