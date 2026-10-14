using Vintagestory.API.Common;

namespace SmoothStonePaths;

public class SmoothStonePathsModSystem : ModSystem
{
    // Runs on both sides: the client needs the class to tessellate the fringe,
    // and the server needs it to resolve the class named by the patched blocktypes.
    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("BlockBlendedPath", typeof(BlockBlendedPath));
    }
}
