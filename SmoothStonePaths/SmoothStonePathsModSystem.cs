using System;
using Vintagestory.API.Common;

namespace SmoothStonePaths;

public class SmoothStonePathsModSystem : ModSystem
{
    public const string ConfigFile = "smoothstonepaths.json";

    /// <summary>World config value the terrain blocktype patches are conditioned on.</summary>
    public const string TerrainBlendingKey = "smoothStonePathsTerrainBlending";

    // Runs on both sides: the client needs the classes to tessellate the fringe,
    // and the server needs them to resolve the classes named by the patched blocktypes.
    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("BlockBlendedPath", typeof(BlockBlendedPath));
        api.RegisterBlockClass("BlockBlendedTerrain", typeof(BlockBlendedTerrain));
        api.RegisterBlockClass("BlockBlendedSoil", typeof(BlockBlendedSoil));

        // The server applies blocktype patches after every mod's Start, and the terrain patches only apply when this
        // world config value is true, so the server's mod config decides for every player.
        if (api.Side == EnumAppSide.Server)
        {
            bool terrainBlending = LoadConfig(api).TerrainBlending;
            if (api.World.Config == null)
            {
                Mod.Logger.Warning("World config is not available yet; terrain blending stays off.");
                return;
            }
            api.World.Config.SetBool(TerrainBlendingKey, terrainBlending);
        }
    }

    private SmoothStonePathsConfig LoadConfig(ICoreAPI api)
    {
        SmoothStonePathsConfig config;
        try
        {
            config = api.LoadModConfig<SmoothStonePathsConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            // Keep the player's file as it is so they can fix it.
            Mod.Logger.Error("Could not read ModConfig/{0}, using defaults: {1}", ConfigFile, e.Message);
            return new SmoothStonePathsConfig();
        }

        if (config == null)
        {
            config = new SmoothStonePathsConfig();
            api.StoreModConfig(config, ConfigFile);
        }
        return config;
    }
}
