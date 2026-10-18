# Regenerates the fringe cutout masks, fringe shapes and the blocktype patch under
# SmoothStonePaths/assets/smoothstonepaths. Run after changing the mask profiles or
# the lists of vanilla rocks and soil fertilities.
#
# The masks are 32x32 PNGs used with the engine's OverlayCutout blend mode: pixels with
# alpha > 0 are REMOVED from the base texture, so the fringe is the vanilla texture with
# dithered holes. Every base texture they are applied to must be 32x32.
#
# Strips are 3 px deep (6 texel rows). One side of some blocks gets a long strip instead,
# which bulges to 7 px in the middle and tapers back to 3 px at both ends so it meets the
# neighbouring blocks' strips at the same depth.
#
# cutout.png layout in texels (shape uv values are texels / 2):
#   edge A        cols  0-31, rows  0-5    short strip, first speckle pattern
#   edge B        cols  0-31, rows  6-11   short strip, second speckle pattern
#   edge long     cols  0-31, rows 12-25   long strip (bulge)
#   bleed east    cols  0-11, rows 26-31   x 10..16, density ramps up to 50% toward the east end
#   bleed west    cols 12-23, rows 26-31   x  0..6,  density ramps up to 50% toward the west end
#   inner corner  cols 24-27, rows 26-29   2x2 px quarter fade anchored at the north-east corner
#
# cutout-concave.png holds the same three strips (A, B, long) for a side whose counter-clockwise
# neighbour side is fringed with a different material. That neighbour's strip runs along this
# strip's west end, so the west end is thinned wherever the neighbour's strip is dense. This strip
# is drawn over the neighbour's, so the corner shows both materials roughly evenly (50/50 at the
# corner point) instead of the top strip winning every texel it keeps.

Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\SmoothStonePaths\assets\smoothstonepaths'
$rocks = 'andesite', 'basalt', 'bauxite', 'chalk', 'chert', 'claystone', 'conglomerate', 'granite', 'limestone', 'peridotite', 'phyllite', 'sandstone', 'shale', 'slate'
$fertilities = 'verylow', 'low', 'medium', 'compost', 'high'

$ShortRows = 6
$LongRows = 14

function Clamp01([double]$v) { [Math]::Max(0.0, [Math]::Min(1.0, $v)) }

# Probability that a texel keeps the base texture, by its distance in rows from the neighbour's side,
# for a strip that fades out over $depth rows.
function Profile([double]$row, [double]$depth) { Clamp01 (1.2 - $row / (11.0 * $depth / 14.0)) }

# Depth of the long strip at a column: 3 px at both ends, 7 px in the middle.
function LongDepth([int]$col) { $ShortRows + ($LongRows - $ShortRows) * [Math]::Sin([Math]::PI * $col / 31.0) }

# A 32x32 mask with every texel cut; Fill-Region then keeps texels at random.
function New-Canvas {
    $bmp = [System.Drawing.Bitmap]::new(32, 32, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $cut = [System.Drawing.Color]::FromArgb(255, 0, 0, 0)
    for ($x = 0; $x -lt 32; $x++) { for ($y = 0; $y -lt 32; $y++) { $bmp.SetPixel($x, $y, $cut) } }
    $bmp
}

# Keeps each texel of the region with the probability the script block returns for (col, row).
function Fill-Region($bmp, $rng, $x0, $y0, $w, $h, [scriptblock]$probability) {
    $keep = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
    for ($c = 0; $c -lt $w; $c++) {
        for ($r = 0; $r -lt $h; $r++) {
            if ($rng.NextDouble() -lt (& $probability $c $r)) { $bmp.SetPixel($x0 + $c, $y0 + $r, $keep) }
        }
    }
}

function New-Mask {
    $rng = [System.Random]::new(7)
    $bmp = New-Canvas

    Fill-Region $bmp $rng 0 0 32 $ShortRows { param($c, $r) Profile $r $ShortRows }
    Fill-Region $bmp $rng 0 6 32 $ShortRows { param($c, $r) Profile $r $ShortRows }
    Fill-Region $bmp $rng 0 12 32 $LongRows { param($c, $r) Profile $r (LongDepth $c) }
    Fill-Region $bmp $rng 0 26 12 $ShortRows { param($c, $r) 0.5 * (($c + 1) / 12.0) * (Profile $r $ShortRows) }
    Fill-Region $bmp $rng 12 26 12 $ShortRows { param($c, $r) 0.5 * ((12 - $c) / 12.0) * (Profile $r $ShortRows) }
    Fill-Region $bmp $rng 24 26 4 4 { param($c, $r) Clamp01 (1.15 - [Math]::Sqrt([Math]::Pow(4 - ($c + 0.5), 2) + [Math]::Pow($r + 0.5, 2)) / 3.75) }

    $bmp
}

# The neighbour's (short) strip has density Profile(col) at this column, and this strip shows only where the
# neighbour's texel is not: this strip is drawn on top, so scale it by (1 - 0.5 * the neighbour's density).
function New-ConcaveMask {
    $rng = [System.Random]::new(11)
    $bmp = New-Canvas

    Fill-Region $bmp $rng 0 0 32 $ShortRows { param($c, $r) (Profile $r $ShortRows) * (1 - 0.5 * (Profile $c $ShortRows)) }
    Fill-Region $bmp $rng 0 6 32 $ShortRows { param($c, $r) (Profile $r $ShortRows) * (1 - 0.5 * (Profile $c $ShortRows)) }
    Fill-Region $bmp $rng 0 12 32 $LongRows { param($c, $r) (Profile $r (LongDepth $c)) * (1 - 0.5 * (Profile $c $ShortRows)) }

    $bmp
}

# Only the up face is drawn. The strip sits just above the path's 15/16 top. BlockBlendedPath lifts
# each piece onto its own height layer at runtime so overlapping pieces are never coplanar.
function Write-Shape($name, $from, $to, $uv, $y, $zOffset) {
    $faceLines = foreach ($side in 'north', 'east', 'south', 'west', 'down') {
        "        `"$side`": { `"texture`": `"#fringe`", `"uv`": [0, 0, 1, 1], `"enabled`": false }"
    }
    $faceLines += "        `"up`": { `"texture`": `"#fringe`", `"uv`": [$($uv -join ', ')] }"
    $json = @"
{
  "editor": { "allAngles": false, "entityTextureMode": false },
  "textureWidth": 16,
  "textureHeight": 16,
  "textures": {},
  "elements": [
    {
      "name": "fringe",
      "from": [$($from[0]), $y, $($from[1])],
      "to": [$($to[0]), $y, $($to[1])],
      "renderPass": 8,
      "zOffset": $zOffset,
      "faces": {
$($faceLines -join ",`n")
      }
    }
  ]
}
"@
    $path = Join-Path $assets "shapes\block\fringe\$name.json"
    [IO.File]::WriteAllText($path, $json.Replace("`r`n", "`n") + "`n")
}

function Write-Patch {
    # Base textures need an explicit domain: a patch resolves a domain-less path in the patched block's own
    # domain, so on another mod's block 'block/...' would point into that mod's assets instead of the game's.
    $textureCodes = [ordered]@{ 'fringe-grass' = 'game:block/plant/grasscoverage/normal-top' }
    foreach ($rock in $rocks) { $textureCodes["fringe-sand-$rock"] = "game:block/stone/sand/$rock" }
    foreach ($rock in $rocks) { $textureCodes["fringe-gravel-$rock"] = "game:block/stone/gravel/$rock" }
    foreach ($fertility in $fertilities) { $textureCodes["fringe-soil-$fertility"] = "game:block/soil/fert$fertility" }

    $texture = { param($code, $base, $mask) "        `"$code`": { `"base`": `"$base`", `"blendedOverlays`": [ { `"base`": `"smoothstonepaths:block/fringe/$mask`", `"blendMode`": `"OverlayCutout`" } ] }" }
    $textureLines = foreach ($code in $textureCodes.Keys) {
        & $texture $code $textureCodes[$code] 'cutout'
        & $texture "$code-concave" $textureCodes[$code] 'cutout-concave'
    }
    $textures = $textureLines -join ",`n"

    # Full-height paths only: slabs sit half a block below the ground beside them, so they get no fringe.
    # Paths from other mods are patched only when that mod is installed. Natural sand and gravel are patched only
    # when TerrainBlending is on in the mod config (SmoothStonePathsModSystem copies it into the world config).
    $terrainKey = 'smoothStonePathsTerrainBlending'
    $targets = @(
        @{ File = 'game:blocktypes/stone/generic/stonepath.json'; Class = 'BlockBlendedPath' },
        @{ File = 'addsomepaths:blocktypes/gravelpath.json'; Class = 'BlockBlendedPath'; ModId = 'addsomepaths' },
        @{ File = 'addsomepaths:blocktypes/packedpath.json'; Class = 'BlockBlendedPath'; ModId = 'addsomepaths' },
        @{ File = 'game:blocktypes/stone/sand.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey },
        @{ File = 'game:blocktypes/stone/gravel.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey },
        # Bare soil is drawn as a cube, which no block hook can reach, so it is switched to a shape drawn the same way.
        # The soil class is shared by every fertility and grass coverage; BlockBlendedSoil only fringes the bare ones.
        @{ File = 'game:blocktypes/soil/soil.json'; Class = 'BlockBlendedSoil'; Condition = $terrainKey; Extra = @(@{ Path = '/drawtypeByType/*-none'; Value = 'Json' }) },
        @{ File = 'game:blocktypes/soil/bony.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; Extra = @(@{ Path = '/drawtype'; Value = 'Json' }) },
        # Built soil blocks are cube-drawn too. Rammed earth picks its textures per variant, so it is baked only.
        @{ File = 'game:blocktypes/soil/packeddirt.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; Extra = @(@{ Path = '/drawtype'; Value = 'Json' }) },
        @{ File = 'game:blocktypes/soil/drypackeddirt.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; Extra = @(@{ Path = '/drawtype'; Value = 'Json' }) },
        @{ File = 'game:blocktypes/soil/packeddirt-trampledearth.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; Extra = @(@{ Path = '/drawtype'; Value = 'Json' }) },
        @{ File = 'game:blocktypes/soil/rammed.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; NoTextures = $true; Extra = @(@{ Path = '/drawtype'; Value = 'Json' }) },
        # Wavy sand picks a tile of its texture per position, which the shape tessellator does as well.
        @{ File = 'game:blocktypes/stone/sand-wavy.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; Extra = @(@{ Path = '/drawtype'; Value = 'Json' }) },
        # Rock is mostly buried, and a block under another one is skipped before any fringe work is done.
        @{ File = 'game:blocktypes/stone/rock.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; NoTextures = $true; Extra = @(@{ Path = '/drawtype'; Value = 'Json' }) },
        # Already a shape; its textures are chosen per variant, so only the baked path is available there.
        @{ File = 'game:blocktypes/devastation/soil.json'; Class = 'BlockBlendedTerrain'; Condition = $terrainKey; NoTextures = $true }
    )
    $entries = foreach ($target in $targets) {
        $file = $target.File
        $class = $target.Class
        $dependsOn = if ($target.ModId) { "`n    `"dependsOn`": [ { `"modid`": `"$($target.ModId)`" } ]," } else { '' }
        if ($target.Condition) { $dependsOn += "`n    `"condition`": { `"when`": `"$($target.Condition)`", `"isValue`": `"true`" }," }
        $extra = foreach ($op in $target.Extra) {
@"
,
  {
    "op": "add",
    "path": "$($op.Path)",
    "value": "$($op.Value)",
    "file": "$file",$dependsOn
    "side": "Server"
  }
"@
        }
        $texturePatch = if ($target.NoTextures) { '' } else {
@"
,
  {
    "op": "addmerge",
    "path": "/textures",
    "value": {
$textures
    },
    "file": "$file",$dependsOn
    "side": "Server"
  }
"@
        }
@"
  {
    "op": "add",
    "path": "/class",
    "value": "$class",
    "file": "$file",$dependsOn
    "side": "Server"
  }$texturePatch$($extra -join '')
"@
    }
    $json = "[`n" + ($entries -join ",`n") + "`n]`n"
    [IO.File]::WriteAllText((Join-Path $assets 'patches\stonepath.json'), $json.Replace("`r`n", "`n"))
}

$maskDir = Join-Path $assets 'textures\block\fringe'
Get-ChildItem $maskDir -Filter *.png | Remove-Item
foreach ($mask in @{ 'cutout.png' = (New-Mask); 'cutout-concave.png' = (New-ConcaveMask) }.GetEnumerator()) {
    $mask.Value.Save((Join-Path $maskDir $mask.Key), [System.Drawing.Imaging.ImageFormat]::Png)
    $mask.Value.Dispose()
}

Get-ChildItem (Join-Path $assets 'shapes\block\fringe') -Filter *.json | Remove-Item
Write-Shape 'edge-a' @(0, 0) @(16, 3) @(0, 0, 16, 3) 15.03125 1
Write-Shape 'edge-b' @(0, 0) @(16, 3) @(0, 3, 16, 6) 15.03125 1
Write-Shape 'edge-long' @(0, 0) @(16, 7) @(0, 6, 16, 13) 15.03125 1
Write-Shape 'bleed-east' @(10, 0) @(16, 3) @(0, 13, 6, 16) 15.03125 2
Write-Shape 'bleed-west' @(0, 0) @(6, 3) @(6, 13, 12, 16) 15.03125 2
Write-Shape 'inner' @(14, 0) @(16, 2) @(12, 13, 14, 15) 15.03125 1
# The baked path draws all the grass as one overlay quad covering the whole block top.
Write-Shape 'overlay' @(0, 0) @(16, 16) @(0, 0, 16, 16) 15.03125 1

Write-Patch
Write-Host 'Generated 2 masks, 7 shapes and patch.'
