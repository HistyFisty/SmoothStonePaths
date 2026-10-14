using System;
using Vintagestory.API.Common;

namespace SmoothStonePaths;

/// <summary>
/// Maps neighbouring blocks to fringe material ids. Id 0 means no fringe; every other id indexes
/// <see cref="TextureCodes"/>, the texture codes the blocktype patch adds to the path blocks.
/// </summary>
internal static class FringeMaterials
{
    public const int None = 0;
    public const int Grass = 1;

    /// <summary>Bits needed to store one material id in a neighbour key.</summary>
    public const int Bits = 6;

    /// <summary>Appended to a texture code to get the variant masked with cutout-concave.png.</summary>
    public const string ConcaveSuffix = "-concave";

    private static readonly string[] Rocks =
    {
        "andesite", "basalt", "bauxite", "chalk", "chert", "claystone", "conglomerate",
        "granite", "limestone", "peridotite", "phyllite", "sandstone", "shale", "slate"
    };

    private static readonly string[] Fertilities = { "verylow", "low", "medium", "compost", "high" };

    private static readonly int FallbackRock = Array.IndexOf(Rocks, "granite");
    private static readonly int FallbackFertility = Array.IndexOf(Fertilities, "medium");

    private static readonly int SandBase = Grass + 1;
    private static readonly int GravelBase = SandBase + Rocks.Length;
    private static readonly int SoilBase = GravelBase + Rocks.Length;

    public static readonly string[] TextureCodes = BuildTextureCodes();

    /// <summary>Only the grass fringe is tinted with the climate and season colour maps.</summary>
    public static bool IsTinted(int material) => material == Grass;

    public static int Classify(Block neighbour)
    {
        if (neighbour == null || neighbour.Id == 0) return None;
        if (neighbour is BlockBlendedPath) return None;
        if (neighbour.Code != null && IsPath(neighbour.Code.Path)) return None;

        switch (neighbour.BlockMaterial)
        {
            case EnumBlockMaterial.Soil:
                // Many blocks share the Soil material (packed dirt, rammed earth, cob, mud bricks, peat, clay, farmland, ...).
                // They are built surfaces that behave like stone, so only natural soil is blended.
                if (neighbour.Code?.FirstCodePart() != "soil") return None;
                if (neighbour.Variant.TryGetValue("grasscoverage", out string coverage) && coverage != "none") return Grass;
                return SoilBase + VariantIndex(neighbour, "fertility", Fertilities, FallbackFertility);
            case EnumBlockMaterial.Sand:
                return SandBase + VariantIndex(neighbour, "rock", Rocks, FallbackRock);
            case EnumBlockMaterial.Gravel:
                return GravelBase + VariantIndex(neighbour, "rock", Rocks, FallbackRock);
            default:
                return None;
        }
    }

    // Path blocks never fringe each other, including ones this mod does not patch (slabs, stairs, full blocks):
    // vanilla stone paths and Add Some Paths' gravel and packed dirt paths.
    private static readonly string[] PathCodePrefixes = { "stonepath", "gravelpath", "packedpath" };

    private static bool IsPath(string codePath)
    {
        foreach (string prefix in PathCodePrefixes)
        {
            if (codePath.StartsWith(prefix)) return true;
        }
        return false;
    }

    private static int VariantIndex(Block block, string variantKey, string[] values, int fallback)
    {
        if (!block.Variant.TryGetValue(variantKey, out string value)) return fallback;
        int index = Array.IndexOf(values, value);
        return index >= 0 ? index : fallback;
    }

    private static string[] BuildTextureCodes()
    {
        var codes = new string[SoilBase + Fertilities.Length];
        codes[None] = "";
        codes[Grass] = "fringe-grass";
        for (int i = 0; i < Rocks.Length; i++)
        {
            codes[SandBase + i] = "fringe-sand-" + Rocks[i];
            codes[GravelBase + i] = "fringe-gravel-" + Rocks[i];
        }
        for (int i = 0; i < Fertilities.Length; i++)
        {
            codes[SoilBase + i] = "fringe-soil-" + Fertilities[i];
        }
        return codes;
    }
}
