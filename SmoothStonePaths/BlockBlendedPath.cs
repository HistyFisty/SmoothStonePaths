namespace SmoothStonePaths;

/// <summary>A path block that shows the fringe of the grass, sand, gravel or dirt bordering it.</summary>
public class BlockBlendedPath : BlockFringed
{
    protected override bool ShowsFringe(Vintagestory.API.Common.Block[] chunkExtBlocks, int extIndex3d)
    {
        return !(Variant.TryGetValue("cover", out string cover) && cover == "snow");
    }
}
