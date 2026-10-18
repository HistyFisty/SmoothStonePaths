using System;
using Vintagestory.API.Common;

namespace SmoothStonePaths;

/// <summary>
/// Finds a block mesh's top face and works out how its texture is laid over it, so the base texture can be
/// re-sampled in block space (x east, z south) however the shape rotated or flipped it.
/// </summary>
internal static class TopFace
{
    /// <summary>The first face whose four vertices are one flat, upward-facing square covering the whole block top, or -1.</summary>
    public static int Find(float[] xyz, int[] flags, int verticesCount, int verticesPerFace)
    {
        if (xyz == null || flags == null || verticesPerFace != 4) return -1;

        var normal = new float[3];
        int best = -1;
        float bestY = float.MinValue;
        for (int face = 0; face < verticesCount / 4; face++)
        {
            int first = face * 4;
            float y = xyz[first * 3 + 1];
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            bool flat = true;
            for (int v = 0; v < 4; v++)
            {
                int i = (first + v) * 3;
                VertexFlags.UnpackNormal(flags[first + v], normal);
                if (normal[1] < 0.9f || Math.Abs(xyz[i + 1] - y) > 1e-4f) { flat = false; break; }
                minX = Math.Min(minX, xyz[i]); maxX = Math.Max(maxX, xyz[i]);
                minZ = Math.Min(minZ, xyz[i + 2]); maxZ = Math.Max(maxZ, xyz[i + 2]);
            }
            if (!flat || minX > 1e-3f || minZ > 1e-3f || maxX < 1f - 1e-3f || maxZ < 1f - 1e-3f) continue;
            if (y > bestY) { best = face; bestY = y; }
        }
        return best;
    }

    /// <summary>The texture coordinates of a face's four vertices as fractions of its texture, plus the rectangle they span.</summary>
    internal readonly record struct Mapping(float U00, float V00, float U10, float V10, float U01, float V01)
    {
        /// <summary>The texel of a 32x32 texture that shows at this point of the block top, x and z in 0..1.</summary>
        public void TexelAt(float x, float z, out int tx, out int ty)
        {
            float u = U00 + x * (U10 - U00) + z * (U01 - U00);
            float v = V00 + x * (V10 - V00) + z * (V01 - V00);
            tx = Math.Clamp((int)Math.Floor(u * 32f), 0, 31);
            ty = Math.Clamp((int)Math.Floor(v * 32f), 0, 31);
        }
    }

    /// <summary>
    /// Reads how the texture rectangle (u1..u2, v1..v2 in atlas units) is laid over the face: where the block
    /// corners (x, z) = (0, 0), (1, 0) and (0, 1) land in it. False if a corner is missing.
    /// </summary>
    public static bool TryGetMapping(float[] xyz, float[] uv, int face, float u1, float v1, float u2, float v2, out Mapping mapping)
    {
        mapping = default;
        float du = u2 - u1, dv = v2 - v1;
        if (Math.Abs(du) < 1e-9f || Math.Abs(dv) < 1e-9f) return false;

        bool has00 = false, has10 = false, has01 = false;
        float u00 = 0, v00 = 0, u10 = 0, v10 = 0, u01 = 0, v01 = 0;
        for (int v = 0; v < 4; v++)
        {
            int vertex = face * 4 + v;
            float x = xyz[vertex * 3], z = xyz[vertex * 3 + 2];
            float fu = (uv[vertex * 2] - u1) / du, fv = (uv[vertex * 2 + 1] - v1) / dv;
            bool atX0 = x < 0.5f, atZ0 = z < 0.5f;
            if (atX0 && atZ0) { u00 = fu; v00 = fv; has00 = true; }
            else if (!atX0 && atZ0) { u10 = fu; v10 = fv; has10 = true; }
            else if (atX0 && !atZ0) { u01 = fu; v01 = fv; has01 = true; }
        }
        if (!has00 || !has10 || !has01) return false;
        mapping = new Mapping(u00, v00, u10, v10, u01, v01);
        return true;
    }

    /// <summary>Re-samples a 32x32 texture into block space through a face's mapping.</summary>
    public static int[] Orient(int[] texture, Mapping mapping)
    {
        int[] oriented = new int[32 * 32];
        for (int tz = 0; tz < 32; tz++)
        {
            for (int tx = 0; tx < 32; tx++)
            {
                mapping.TexelAt((tx + 0.5f) / 32f, (tz + 0.5f) / 32f, out int sx, out int sy);
                oriented[tz * 32 + tx] = texture[sy * 32 + sx];
            }
        }
        return oriented;
    }
}
