# Bannerlord - Rigged Female Gallery

Double-click **Launch Gallery.bat** (needs Python), or `python gallery\serve.py 8766` and open http://127.0.0.1:8766/ .
Models: `gallery/models/*.glb` (skinned + animated, 28-bone `human_skeleton`).

## What is in it (42 models)
- `female_nude` - base female body + head/hands/feet/hair. **The game's only body texture has underwear painted in.**
- `female_nude_bare` - same, with the underwear paint removed from the 512px texture (derived, `src/texcache/body_female_a_d_bare.png`).
- `female_armored_<culture>` x6 - highest-armor full set per culture (helmet, body, boots, gauntlets, shoulders), female `_converted_slim` meshes.
- `female_flagbearer_<culture>` x6 - the armored set holding that culture's tier-3 battle banner (attached to `r_hand`),
  clips: `pledge_troop_walk_with_banner_loop`, `anim_rider_walk_withbanner`, `anim_rider_canter_withbanner`, idle.
- 18 standalone banners (6 cultures x tier 1-3) + 10 flag props (flagpole_a/b, campaign_flag, flagbearer_a/b map icons, ...).
- Viewer QoL: search (/), category chips, favorites, animated-only filter, animation picker + play/pause (Space) + speed + scrubber,
  parts toggles, wireframe (W), skeleton (K), rotate (R), backgrounds, PNG shot (P), GLB download, deep links `#<id>`, arrows to browse.

## Rebuild (game: S:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord, v1.3.x, TPAC v2)
1. `src/TpacTool` = upstream szszss/TpacTool (the SaulHE fork cannot read TPAC v2) with three local patches in `TpacTool.Lib`:
   `AssetManager.LoadDo` skips packages that throw; `AssetPackage.Load` seeks past each asset's metadata by its declared size
   (the Texture reader drifted and broke `pack_*.tpac`/`core.tpac`). Debug env var `TPDBG`.
2. `src/tool` (net8 console) = exporter. `dotnet build -c Release`, then
   `set TEXCACHE=<repo>\src\texcache` and `dotnet bin\Release\net8.0\tool.dll "<game>\Modules\Native\AssetPackages" export ..\config.json ..\..\gallery\models`
   (other verbs: `list`, `tex <name>`, `seg <tex>`, `diag`).
3. `src/gen_config.py` builds `config.json` from the item XMLs (needs `src/meshnames.txt` from the `list` verb).
4. Textures: the 4096/2048 skin & armor textures have NO full-res pixel data in the packs. The exporter decodes the 512px mip-tail segment
   instead. BC7 ones are dumped as DDS to `texcache/`, run `python src/dds2png.py`, then re-export.
5. `python src/make_index.py`; thumbnails: with the server running open `?thumbs=1` (`&redo=1` to redo).

## Notes / known limits
- Animation clips carry rotation keys only (no per-bone translation); root motion is not exported.
- Banner cloth is the default placeholder emblem (real emblem is composed in-game).
- Textures are 512px (that is all the packs contain). Normal/specular maps are not exported.
- Female armor variant = `<mesh>_converted_slim` (fallback `_slim`, `_converted`, base); verified visually only on Vlandia/Sturgia sets.
- Game assets are Taleworlds' property - personal modding/study use only.

## C_DS_NZ look (transplant/)
Goal: Bannerlord armor that LOOKS like Cirith (C_DS_NZ) - and nothing else changes. Kept from Bannerlord: mesh topology and part split,
skeleton, skin weights, animations. Taken from Cirith: the shape (vertices snapped onto her surface), her UV layout and her atlas as is.
Merge all armor parts -> reshape onto her (landmark RBF + per-bone-region snap, stacked layers kept apart) -> her UV per vertex
(closest point inside the right UV island; verts straddling islands are duplicated) -> unmerge back into the parts.
- `python transplant\looksim.py --armor vlandia|empire|sturgia|battania|khuzait|aserai|all [--no-render] [--smooth 10 --snap 0.8 --offset 0.25]`
  builds `transplant/out/look_<culture>.glb` and checks it in ~1-15 s: surface deviation from her, bad (flipped) tris, edge stretch, UV
  fidelity, contact sheet `out/looksheet_<culture>.png` (her | result at rest | Bannerlord walk frames). `python src\make_index.py` publishes.
- Target (read only): `C:\Users\Trey\Downloads\DFG\bare\C_DS_NZ.glb`. Never touch the 8777 gallery; test at http://127.0.0.1:8766/.
- Known: her atlas is 512x256 so detail is soft; large skirt/tabard triangles smear her texture; ~1-3% flipped tris where layers collapse.
- `transplant/old_cirith_format/` = the earlier (wrong) opposite approach, armor converted INTO her format; kept for reference only.

## Cirith rigged as a Bannerlord full-body armor (transplant/rigsim.py)
Her mesh keeps its ORIGINAL SHAPE, UVs, normals and atlas (nothing is reshaped; the sim reports shape_change_pct = 0). Only: a uniform
scale + shift into Bannerlord space, and the RIG - her own skin weights remapped bone by bone onto Bannerlord's 28-bone human_skeleton
(twist bones share the limb weight), then smoothed so transitions do not tear. Split into slots head / hands / legs / body (+ trim).
- `python transplantigsim.py [--no-render]` (~1-5 s) -> `out_rig/cirith_armor_rigged.glb` (+ `_on_body`), `rig_metrics.json`, `rig_sheet.png`.
- Known: her arms are ~19 deg off Bannerlord's bind-pose arms (shape is kept as asked); some elbow/shoulder triangles stretch in motion.
- Not written: Bannerlord item/slot XML. `looksim.py` (Bannerlord armor that looks like her) is the other direction and is kept separately.

## Cloth physics for Bannerlord (breast / glute / ponytail) - in-game compatible
Bannerlord has no jiggle bones (skeleton is fixed), its physics is CLOTH: a mesh flagged `uses_cloth_simulation` + a cloth material
(`Native/ModuleData/cloth_materials.xml`) + vertex colour ALPHA = per-vertex "free" weight (measured on `vlandian_woman_dress`: bodice 0 = pinned,
hem 255 = simulated; LODs without simulation are all 255). `rigfit.py` writes exactly that into `cirith_armor_rigged.glb`:
- `cirith_body_armor` (material `cirith_body_jiggle`): alpha painted by smooth falloff around breast (r 9.5 cm, max 40%) and glute (r 12 cm, max 34%) centres
  taken from her mesh surface; everything else 0 (pinned). Soft anchor spring (anchor_stiffness 0.30) => jiggle.
- `cirith_head_armor` (material `cirith_ponytail`): alpha ramps 0 (root) -> ~92% (tip) along her ponytail.
- `transplant/out_rig/cloth_materials_cirith.xml` = the two `<material>` entries to add to cloth_materials.xml. Skeleton unchanged (28 bones).
- Viewer: **Cloth physics (J)** + Strength slider = a preview solver that reads those same flags/params/alpha (spring to skinned pose, stretch/bend
  constraints, damping, gravity, inertia). It is NOT the game's PhysX solver: tune the numbers in the Modding Kit. Not done: collision capsules
  (cloth_bodies.xml), `clo_` proxy mesh/mapping (TPAC write), verification in the actual game.

## Animations, weapons, game look, rig test (latest)
- `cirith_armor_rigged.glb` carries **289 Bannerlord human clips** (all `anim_female_*`/`female_stand_*` locomotion + stances, plus combat / bow / crossbow / jump / death / rider samples),
  exported by the C# tool scene `anim_library` (`src/config_anims.json`, names in `src/anim_names.json`; rest-pose first frames are trimmed so loops do not snap to T-pose).
  Viewer: grouped list (Mounted/rider, Ranged, Combat, Idle/stance, Walk, Run, Jump/death) + filter box.
- **Weapons** (viewer dropdown, `gallery/weapons/w_*.glb`, 14 Bannerlord meshes: 1H swords, axes, 2H swords, 3 shields, bow, crossbow, javelin, banner): attached to `r_hand` / `l_hand`
  (grip at origin, blade along game +Z), the matching `female_stand_*` clip is selected automatically; "Grip adjust" sliders tune rotation/offset. Rider clips are included but there is no horse model.
- **Game look** (G) + "Render PNG" (1600x1200 to `gallery/renders/<id>_game.png`): sun + shadows, sky, ground, filmic tone mapping, idle pose with cloth settled.
- **Rig test** `python transplant\rigtest.py` poses the armor in every clip and counts cracks (edges stretched >1.6x and >2cm) and gaps (rest-coincident vertices that separate >0.5cm).
  Weights are now unified over ALL parts (UV-seam copies, slots, trim) and blended with distance-to-Bannerlord-bone weights (RIG_LAMBDA/RIG_SIGMA/RIG_ITERS/RIG_WRIST env vars).
  Before: gaps in 268/269 clips (up to 128 gaps, 20 cm), 47 cracks/clip. Now: gaps 0/289 (max 0.09 cm), cracks 41/clip; the rest sit at the wrist/elbow in extreme two-handed poses.

## Hands that actually hold things (Bannerlord grip morphs)
- Bannerlord closes hands with **25 morph frames inside the hand meshes** (`hands_female_a`: 26 frames, gauntlet armors 27): `frame = 1 + 5*leftLevel + rightLevel`, level 0 open .. 4 fist, frame 0 open.
  `transplant/handmorph.py` aligns each of her hands to Bannerlord's hand mesh (PCA start + ICP; palm/back ambiguity resolved by which alignment makes the fist most compact)
  and carries all 25 frames over as glTF morph targets (`grip_1..25`) on her glove/trim/body meshes (548 vertices, shared boundary verts get identical deltas).
  `src/morph/*.bin` come from `tool.dll morphdump <metamesh> <file>`.
- Weapon placement: `grip_presets.json` (from HER closed fist, in the hand-bone frame) + a per-weapon rotation in `gallery/app.js` `WEAPONS` (axes 90 deg, crossbows 180, shields 180, javelin/strong bow 90).
  Upright weapons choose the sign that points them up-and-forward in their stance clip. Archery = bow (left hand) + nocked arrow (right hand) + quiver on `spine2`.
- Review tool: in the browser console `await __sheet([weaponIdx, [idx,[rx,ry,rz]], ...], 'name', 'front'|'right'|'left'|'back')` writes a contact sheet to `gallery/renders/`.
- Grip levels per weapon (R,L): swords/axes/banner [4,0], 2H [4,4], shields [0,3], bows [2,3], crossbow [4,2], javelin [3,0]; manual sliders "Hand grip R/L" in the viewer.
