# Putting a custom rigged armor into Mount & Blade II: Bannerlord (v1.5.7) - what worked, and every trap

This repo documents how a custom, fully rigged, textured female armor (a model that is **not** from Bannerlord) was written into a
Bannerlord `.tpac` asset package, loaded by the real game engine, and inspected **headlessly** with a scripted free camera.
It contains the tooling and the write-up only. **No game assets, textures or models are included** - bring your own.

Verified in the real engine (not just a viewer): geometry, skinning on the human skeleton, own texture with alpha-masked trims,
custom material, item XML, character equipment, a baked hair pose, weapons held in the hands.
**Not solved:** in-game cloth/jiggle (see the end).

---

## 1. The pipeline in one picture

```
your model (GLB, rigged to the 28-bone human skeleton)
   |  transplant/dump_slots.py      per armor slot: pos / normal / uv / bone idx / weights / vertex-alpha  ->  slots.bin
   |  enginesim/mk_atlas.py         atlas PNGs -> one DXT5 texture with mips                              ->  cirith_main.dxt5
   v
tool armorpack  (tool/Program.cs + patched TpacTool)               ->  AssetPackages/cirith_armor.tpac
   |   (cloned vanilla metamesh per slot, own VertexStream, own Texture + Material)
   v
Modules/<YourModule>/{SubModule.xml, ModuleData/*.xml, AssetPackages/*.tpac, bin/.../YourSim.dll}
   v
enginesim/run_engine_sim.ps1  -> boots the real game on a hidden desktop, auto-starts a Custom Battle with ONE test unit,
                                 drives a free camera through a view list, writes PNGs
```

Tools used: [TpacTool](https://github.com/szszss/TpacTool) (Lib only, patched - see `patches/`), .NET 8 for the packer, netstandard2.0 for the in-game mod
(the game runs .NET 6 with a custom host - a net6.0 mod fails with *"Could not load System.Runtime 6.0.0.0"*), Python + numpy + Pillow.

## 2. The traps (this is the valuable part)

### A. TpacTool's writer was wrong for vertex streams (three separate bugs)
The reader round-trips fine, so these hide until the *engine* reads the file.
1. **Offsets.** In a `VertexStreamData` block the 14 (offset,size) pairs come first (224 bytes with the Q-tangent array). Offsets are relative to the start of
   that table, so the first array starts at `224`, not `0`.
2. **No count prefix.** `WriteStructArray` writes a 4-byte count before each array (right for `MeshEditData`, wrong for vertex streams). Use a raw writer.
3. **`Material.WriteMetadata` was `NotImplemented`** - you cannot save a new material without it.
Check with a byte diff: re-serialize a vanilla stream and compare to the decompressed original. After the fixes the diff is **0 bytes**.
(`patches/tpactool_writer_fixes.patch`; `tool rawstream <mesh>` is the byte-diff check.)

### B. Names, GUIDs, segments
- **The metamesh `Name` must equal the mesh name your item XML references.** If it silently keeps the template's name the engine finds nothing and you get an
  *invisible* armor with no error in any log. (This cost hours.)
- New GUIDs for the metamesh, every mesh, and **remap every segment's `OwnerGuid`** to the new mesh GUIDs.
- A vertex-stream segment needs `_unknownUint = 1` and the `hasqtangent` user-data flag; copy the vanilla `_unknownUlong`.
- **Replace segments in place** (same list index) - do not remove+append.
- Keep each mesh's `MeshEditData` consistent with its vertex stream. Leaving a *vanilla* edit-data behind is dangerous: a vanilla mitten's 26 grip-morph frames get applied
  to your hand mesh and deform it into black blobs.
- **Replace EVERY LOD, not just LOD0.** If LOD1+ keep the template's vanilla geometry, small parts (hands, feet) get drawn from a vanilla LOD even at close range:
  you see the *vanilla silhouette* (a boxy mitten) textured with *your* atlas through the vanilla UVs - orange/black garbage that looks like a texture bug but is really
  the wrong mesh. Give each LOD its own `VertexStream` + `MeshEditData` (same data is fine; decimate later) and keep the vanilla LOD list/order.

### C. Vertex stream encoding (all measured against vanilla meshes)
| array | encoding |
|---|---|
| positions, "another positions" | float3, both = position |
| normals | float3 and `X11Y11Z10` compressed |
| tangents | float4 (w = handedness) and `X10Y11Z10W1` |
| compressed positions | `Half4(pos, 1)` |
| **TangentTransform (Q-tangent)** | snorm16 x4 = quaternion of the matrix with **columns [N, T, N x T]**, normalized, w >= 0, multiplied by **-tangent.w** (mean error 3e-5) |
| bone weights/indices | 4 bytes each; weights sum ~255; **indices are the human_skeleton bone index** (not the skin-local index of your GLB) |
| colors | color1 = (255,255,255, **A**), color2 = 0, uv2 = (0,1) |
`UnknownInt2` on the mesh = number of distinct bones used; `PositionCount`/`SkinDataSize` = number of unique positions.

### D. Skeleton space
- Game space is **Z-up, +Y forward**. A glTF with a `Root` node rotated -90 deg about X is already Z-up in its raw vertex data - do **not** convert again
  (I did once and everything exploded).
- **The skeleton's rest arms point ~27 deg downward**, not T-pose. If your arms are modelled horizontal the hands float ~18 cm above the hand bones and
  weapons are not held. Fix: rotate each arm vertex about the upper-arm bone by the angle between your arm chain and the bone chain, scaled by the vertex's
  arm-bone weight (`angle_arms` in `transplant/dump_slots.py`). Hands then land within 1-3 cm of the hand bones.
  **Measure the angle once and apply the identical rotation to EVERY slot** (body, armlets, trims). My first version silently skipped slots without hand
  vertices, which left the upper arm and the orange forearm bracers ~19 deg out of place - they stuck out like wings.
  Then do a **grip fit**: the engine puts the weapon in the palm of *its* hand, so compare your hand's proximal end with the native `hands_female_a` along the arm
  direction and slide yours until they match (here ~5 cm) - otherwise the hand floats above the grab point.

### E. Texture + material
- Alpha-masked parts (filigree, trims) need a **DXT5** texture and an alpha-tested material. Clone a vanilla one
  (`imperial_lamellar_a_alpha`: `alpha_test`, two-sided, `disable_vertex_color_alpha`) and swap slot 0; use the engine's own `plain_black` (colour mask),
  `flat_n` (flat normal) and `default_specular` for slots 1/2/4.
- Merge several atlases into one POT atlas (here 512x1024) and remap UVs, so every slot is one mesh + one material. **Leave a gutter** (replicate the edge rows of
  the upper atlas for >=16 px) between atlases, or bilinear/mip filtering bleeds one into the other at shared edges.
- **Sanitize garbage UVs.** Source meshes can contain a handful of vertices with UVs like (-71, 117). A viewer's REPEAT sampler hides them; in a merged atlas they
  smear colours across their triangles. Replace each by the mean UV of its in-range neighbours (`fix_uv` in `dump_slots.py`).
- Write your own DXT1/DXT5 + mip chain (`enginesim/mk_atlas.py`); the texture pixel segment is just the mip blobs concatenated.
- Material `ShaderMaterialFlags` containing `disable_vertex_color_alpha` lets you reuse vertex alpha for cloth weights without making the mesh transparent.

### F. Module XML
```xml
<Xmls>
  <XmlNode><XmlName id="Items" path="your_items"/><IncludedGameTypes><GameType value="CustomGame"/></IncludedGameTypes></XmlNode>
  <XmlNode><XmlName id="NPCCharacters" path="your_chars"/>...</XmlNode>
</Xmls>
```
Item `mesh="<metamesh name>"`, `Type="BodyArmor|LegArmor|HandArmor|HeadArmor"`. Equipment slots in the character: `Head`, `Body`, `Leg`, `Gloves`.

## 3. Testing in the REAL engine, headless and fast

`enginesim/CirithSim` is a tiny `MBSubModuleBase` mod: waits for the main menu, `MBGameManager.StartNewGame(new CustomGameManager())`, then
`CustomBattleHelper.PrepareBattleData(...)` with one unit, then drives a camera and calls `Utilities.TakeScreenshot` (writes a **BMP**).
~45 s boot + ~7 s battle.

- **Hidden desktop:** `run_engine_sim.ps1` creates a private Win32 desktop (`CreateDesktopW`) and `CreateProcess`es the game on it (`lpDesktop`). No window on your
  desktop, and the game cannot grab your mouse (moving the window off-screen does **not** work - the engine re-centres the cursor every frame).
- **Safe Mode prompt:** killing the game leaves `safely_exited = 0` in `Documents/.../Configs/engine_config.txt`; set it back to 1 before/after each run.
- **Motion blur / DOF** ruin screenshots: set `postfx_motion_blur = 0`, `postfx_dof = 0` for the run and restore afterwards.
- **Free camera:** `MissionScreen.CustomCamera = Camera.CreateCamera()` with your own `MatrixFrame`. Build the frame with `Mat3.CreateMat3WithForward(dir)` then
  `RotateAboutSide(+pi/2)` (cameras look along the frame's UP axis; the wrong sign gives an upside-down image). The built-in chase camera cannot do close
  full-body shots; `SetPhotoModeEnabled(true)` froze the game when hidden.
- `views_full.txt` lists `name az elev dist targetHeight fov` per line -> `-Views views_full.txt` writes one PNG per line.
- A killed game can leave a zombie process that locks the mod DLL - check `Process.HasExited`, not just existence.

## 4. Bisecting an "invisible / exploded / wrong-looking" armor (the method that finally worked)
Never change several things at once. Start from a **positive control** (vanilla metamesh cloned under a new name renders), then change one thing:
1. clone untouched -> renders. 2. repack the *untouched* vertex stream through your writer -> found the writer bugs (explosion).
3. re-encode vanilla data through your encoder -> invisible -> found the metamesh-name bug. 4. only then swap in your own geometry. 5. If one part looks like a *different object* with your texture on it, suspect LODs (see B).
Isolate a part with its own test character that wears only that item, and render a numpy/GL preview of the source mesh with its texture to compare against the engine.
Also compare packages field-by-field (`tool pkgdiff a.tpac b.tpac`) and diff serialized bytes (`tool rawstream`).

## 5. Not solved: in-game cloth / jiggle
Setting the mesh flag `uses_cloth_simulation`, the per-mesh `ClothingMaterial` parameters and vertex-alpha free weights is **not enough**. Vanilla cloth armor also has
(a) a separate simplified `clo_<name>` metamesh whose mesh carries an extra compiled data segment (type `b97e0f10-...`, ~80 KB for a dress) and
(b) a `ClothMappingData` segment (`af41613e-...`, ~22 bytes per render vertex) on the render mesh plus a dependency link. Both are compiled binary formats that this
project has not decoded yet. PRs / findings welcome.

## 6. Layout
- `tool/` - .NET 8 console: `armorpack`, `rawstream`, `pkgdiff`, `matinfo`, `seginfo`, `vsinfo`, `blobdump`, ... (expects the game's `Modules/Native/AssetPackages` as arg 0)
- `patches/` - TpacTool writer fixes (apply to a clone of szszss/TpacTool; the tool project references it as `TpacTool.Lib`)
- `transplant/` - GLB reading, skeleton posing, `dump_slots.py` (slot merge, arm angling, hair bake, vertex alpha)
- `enginesim/` - hidden-desktop runner, free-cam mod, atlas/DXT encoder, example module XML

Legal: Bannerlord is (c) TaleWorlds. This repo contains only original tooling and notes; do not redistribute game or third-party model assets.

### Wrist sleeve
Sliding a hand to the grab point leaves a gap at the wrist. Do not stretch anything: add a short tube of the *same radius* as the two rings it joins
(forearm distal ring -> hand proximal ring), skinned over a few rings from the forearm bone to the hand bone (`dump_slots.py`).
