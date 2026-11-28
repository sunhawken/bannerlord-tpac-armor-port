# How the Cirith mod was built (full walk-through)

This is the end-to-end story of how the Kingdom Under Fire "Cirith" model (`C_DS_NZ`) became a working armor + weapon + animation mod in **real Bannerlord v1.5.7**, including the headless engine sim and the DLL that drives jiggle, animations and the sword. It is written so you can redo it, and so the reasons behind every odd decision are on record. Where something was *not* verified, it says so.

Game assets belong to TaleWorlds; the Cirith model belongs to its original owners. This repository is for personal modding/study.

---------------------------------------------------------------------------------------------------

## 0. The pieces and how they fit

```
KUF model (C_DS_NZ.glb, read only)                    Bannerlord packs (Modules\Native\AssetPackages\*.tpac)
        |                                                          |
 transplant/*.py  (rig, re-skin, slots, atlas)        src/tool  (TpacTool based exporter / armorpack writer)
        |                                                          |
        +----------------->  slots.bin + cirith_main.dxt5  ------->+--> cirith_armor.tpac (+ sword tpacs)
                                                                         |
 enginesim/CirithSim/module  (SubModule.xml, ModuleData xml/txt, AssetPackages)   <---------+
 enginesim/CirithSim/src     (CirithSim.dll: jiggle, weapon animation controller, charge, inventory keeper, test harness)
        |
 enginesim/run_engine_sim.ps1   boots the REAL game (hidden desktop or visible), screenshots, logs
 grip_editor/                   browser editor for sword placement + joint tweaks (sends back grip_edits.json)
```

Everything runs on the real engine. There is no emulation: every claim in the docs was checked from a real game run (log lines + screenshots), and `docs/TEST_RESULTS.md` lists what was and was not exercised.

---------------------------------------------------------------------------------------------------

## 1. Reading and writing Bannerlord assets (src/tool, TpacTool)

Bannerlord stores meshes, skeletons, textures and materials in `.tpac` packages. Reading them uses **TpacTool** (upstream `szszss/TpacTool`; the SaulHE fork cannot read TPAC v2) with local patches (see `patches/tpactool_writer_fixes.patch` and the README):

* packages that throw while loading are skipped; each asset's metadata is skipped by its declared size (the Texture reader drifted and broke `pack_*.tpac`/`core.tpac`);
* **writer fixes** (needed to *create* packages): vertex-stream offsets start after the 224-byte table, no count prefix, `Material.WriteMetadata` implemented. A MetaMesh name must equal the item's `mesh="..."` name.

`tool/Program.cs` is a .NET 8 console with verbs. The important ones:

| verb | what it does |
|---|---|
| `list`, `tex`, `seg`, `diag` | inspect packages, textures and segments |
| `export` | gallery GLB export (the 42-model gallery in this repo) |
| `armorpack <slots.bin> <out.tpac>` | **writes the mod's armor package**: clones vanilla armor templates (mesh + material + skeleton binding) and replaces geometry, UVs, skin weights, textures with Cirith's. Also writes the weapon packages (`cirith_sword`, `cirith_sword_hidden`, `cirith_probe`) |
| `wdiag` | weapon diagnostics (item/mesh/holster data) |

Env vars used by `armorpack`: `CIRITH_TEX` / `CIRITH_TEXNAME` / `CIRITH_TMPLMAT` (texture file, texture name, template material for weapons), `LOD0ONLY` (must be **unset**: all LODs are replaced, otherwise lower LODs show vanilla armor), `NOCLOTH`.

Templates: armor pieces clone `mail_mitten_a` (hands), `armor_merchant_helmet_a` (head), body/legs templates; the weapon clones `vlandia_onehanded_sword_a` with material `imperial_lamellar_a_alpha` (alpha-tested material, which is what makes cut-out textures work).

Build: `dotnet build -c Release --disable-build-servers` in `src/tool`. Run:

```
dotnet tool.dll "<game>\Modules\Native\AssetPackages" armorpack transplant\out_rig\slots.bin enginesim\CirithSim\module\AssetPackages\cirith_armor.tpac
```

---------------------------------------------------------------------------------------------------

## 2. From her model to Bannerlord armor (transplant/)

Goal: her **original shape, UVs and atlas**, rigged to Bannerlord's skeleton, delivered as four armor slots (head, body, legs, hands) plus trims.

1. **Rig** (`rigsim.py`, `rigfit.py`, `pose.py`, `glbio.py`): her skin weights are remapped bone by bone onto Bannerlord's 28-bone `human_skeleton` (pelvis, l/r thigh, calf, foot, toe0, spine, spine1, spine2, neck, head, l/r clavicle, upperarm_twist(+1), foretwist(+1), hand, finger0). Twist bones share the limb weight; weights are smoothed so transitions do not tear. Output `out_rig/cirith_armor_rigged.glb` (what the gallery viewer shows).
2. **Slots** (`dump_slots.py`): splits the mesh into head / body / legs / hands (+ trim) and writes `out_rig/slots.bin` for `armorpack`. Things done here on purpose:
   * **Arm angle**: her arms are rotated about the shoulder (≈ −18.4° right / +19.2° left) to sit on Bannerlord's lower-armed rest pose - one angle for every slot so there are no seams between pieces.
   * **Hands**: her converted clawed hands; the sword hand keeps a grip-fit slide (~5 cm) and a black wrist sleeve (`RIGHT_FIT=1` restores the same fit for the other hand; off by default).
   * **Spare-bone re-skin** (see jiggle below).
   * **Baked ponytail**: it hangs down in the mesh; the DLL swings it.
   * All LODs are replaced.
3. **Texture atlas** (`enginesim/mk_atlas.py`, `transplant/gem_mask.py`, `enginesim/seam_fix.py`, `tex_dxt.py`): her two source sheets (512x256 main, 512x512 orange trims) are combined into one 512x1024 RGBA atlas with 16 px gutters, encoded as **DXT5 with 11 mips** (`tex/cirith_main.dxt5`). `gem_mask.py` paints the dark areola on the front breast UV island (atlas px 138,115, radii 7.5x6 - it was on her back once, front is +y), pads gutters with skin colour, and runs `seam_fix.py` so island borders do not show dark seams.
   * **Texture source**: the textures the gallery viewer shows for `C_DS_NZ` (the viewer's own images with their own alpha). Black-is-transparent cut-outs were tried several times and reverted on request; `enginesim/blackkey.py` (`BLACKKEY=1 python mk_atlas.py`) is the kept, off-by-default recipe (alpha 0 where max(RGB)<16, one halo pass, colour bleed from the nearest opaque texel, face and areola exempt).
4. **Pack**: `armorpack` (above) -> `cirith_armor.tpac`.

Items and characters are plain XML (`module/ModuleData/cirith_items.xml`, `cirith_chars.xml`): `cirith_head/body/legs/hands` (culture vlandia, `Type="BodyArmor"` etc. with `mesh=` pointing at the packed metamesh), `cirith_sword` (see section 6), and test characters (`cirith_test`, `cirith_sword_test`, `cirith_w_main`, ...). `SubModule.xml` registers them as `<Xmls>` for CustomGame/Campaign.

---------------------------------------------------------------------------------------------------

## 3. The engine sim (enginesim/run_engine_sim.ps1)

A "sim" here is the **real Bannerlord**, started with a custom module and driven by the DLL:

1. The script installs the module (`SubModule.xml`, `ModuleData\*`, `CirithSim.dll`, `AssetPackages`) into `<game>\Modules\CirithSim`.
2. It launches `Bannerlord.exe /singleplayer _MODULES_*Native*SandBoxCore*CustomBattle*CirithSim*_MODULES_` with env `CIRITH_CHAR=<character id>` (the harness is only active when this variable is set) and `CIRITH_SCENE`.
3. Headless mode runs the game on **its own hidden Win32 desktop** (`CreateDesktopW`), so there is no window and it cannot grab the user's mouse or keyboard.
4. The DLL auto-starts a Custom Battle with one test unit, positions a camera, takes screenshots (`Utilities.TakeScreenshot`) into `Modules\CirithSim\shots\`, writes `cirithsim.log`, and exits. Scripted free-camera views come from `CIRITH_VIEWS=<file>` (`views_*.txt`, one line per view: name, azimuth, elevation, distance, target height, fov).
5. Housekeeping: it temporarily sets `postfx_motion_blur=0`, `postfx_dof=0`, `disable_sound=1` in `engine_config.txt` (headless only) and restores them, sets `safely_exited=1` so no Safe Mode prompt appears, and auto-dismisses the crash reporter window.

Gotchas: first boot is slow (1-5 min, pass `-Timeout 520+`); a leftover `Bannerlord.exe` must be killed before a rerun; environment variables must be set in the same shell call; the game folder must be on a **local** drive (.NET refuses DLLs from network paths).

`-Visible` boots a normal window straight into the battle for live play (free camera, test-harness off). With the camera mod installed (`Modules\StuffedHoleEnhancedThirdPerson` + Bannerlord.Harmony, ButterLib, UIExtenderEx, MBOptionScreen) the script loads it automatically; override with `-Pre` / `-Modules` / `-ExtraModules`. Headless boots with that extra stack did not reach the battle within 560-1000 s, so use `-Visible` for it.

Sim test modes (env): `CIRITH_MOTION=1` (slide 3 s + stop for cloth/jiggle), `CIRITH_WTEST=1` (+`CIRITH_WPREVIEW`, `CIRITH_WCAM=front`, `CIRITH_WSTRESS`) weapon animation tests, `CIRITH_ATTACKTEST=1` (+`CIRITH_FORCEHOLD=1`, `CIRITH_CAMR=1`) attack-hand check, `CIRITH_GIVETEST=1`, `CIRITH_BONETEST`, `CIRITH_RUNTIME=1` (run the real in-game behaviour inside the harness).

---------------------------------------------------------------------------------------------------

## 4. The DLL (enginesim/CirithSim/src)

Built as `netstandard2.0` against the game's own assemblies (`TaleWorlds.Core/Library/Engine/MountAndBlade/...`, see `CirithSim.csproj`; the `G` property is the game path). Build: `dotnet build -c Release -o bin --disable-build-servers`. It is a normal Bannerlord module (`SubModule.xml`, `SubModuleClassType = CirithSim.SimSubModule`).

| file | role |
|---|---|
| `SimSubModule.cs` | module entry. Normal game: **passive** (no auto-battle). With `CIRITH_CHAR` set: the sim harness (auto Custom Battle, cameras, screenshots, tests). Adds `CirithRuntimeBehavior` as a mission behaviour for real play |
| `RuntimeBehavior.cs` | real-play mission behaviour: attaches Jiggle to every agent wearing a Cirith piece, a `WeaponAnim` controller to every agent wielding the sword, auto-draws the sword after spawn, **G** draws/sheathes, **Ctrl+G** adds the sword to a free weapon slot if missing and draws it |
| `Jiggle.cs` | breast / glute / ponytail motion |
| `WeaponAnim.cs` | the idle / walk / run / sprint controller, sword placement, sprint speed boost |
| `Charge.cs` | earlier x1.5 speed ability (kept; the sprint boost now lives in WeaponAnim) |
| `InventoryKeeper.cs` | keeps at least one copy of the armor in the player's inventory |

### 4.1 The key engine fact: bone overrides stick
`Skeleton.SetBoneLocalFrame(i, frame)` followed by `UpdateEntitialFramesFromLocalFrames()` **sticks to the render** when called from `MissionBehavior.OnMissionTick` (verified with a probe that lifted the head 0.3 m). The engine's animation overwrites the pose every tick, so the DLL sets it again every tick. No Harmony patching is needed. This one fact enables everything below.

### 4.2 Jiggle with spare bones
The skeleton is fixed at 28 bones and the game's cloth needs compiled blobs we cannot generate, so jiggle uses **spare bones**: `l_finger0` (both breasts), `r_finger0` (ponytail), `l_toe0` / `r_toe0` (glutes) are not used by her meshes, so `dump_slots.py` re-skins the cloth zones onto them: weight = the vertex's painted cloth alpha (body 0..96, head 0..235), other influences scaled by `1-alpha`; legs/boots are re-skinned to the foot bone so the toes are free. The DLL then drives those bones: bone = `PfAnim * L * (PfRest^-1 * boneRest)` with `Pf` = spine2 / pelvis / head (rest from `GetBoneEntitialRestFrame`); breasts and glutes are spring-dampers (≈6.5 / 4.5 Hz) fed by smoothed world acceleration, the ponytail is a world-space pendulum about its pivot (`ModuleData/cirith_spare.txt`). Env: `CIRITH_JIG=<gain>` (0 = off), `CIRITH_JIGLOG`.

### 4.3 The sword
Facts established with probes and tests:
* the engine attaches a wielded weapon with its own fixed frame and **ignores bone overrides**, so the sword never follows custom arm poses;
* hiding the engine weapon failed;
* therefore the **item uses an invisible 1 mm twin mesh** (`cirith_sword_hidden`; stats, hitbox and physics stay) and the DLL creates its own `GameEntity` (`GameEntity.CreateEmpty` + `AddMultiMesh(MetaMesh "cirith_sword")`) and places it with `SetGlobalFrame` every tick on a hand bone;
* the item **must** have `body_name="bo_sword_one_handed"` and `item_holsters="sword_left_hip"`, otherwise the game crashes when the agent spawns;
* agent scale (≈1.055) makes `TransformToLocal` assume an orthonormal matrix wrongly - frames are composed with `TransformToParent` only.

Grip frames: `cirith_grip.txt` = the sword axes in the `l_hand` frame, derived from **her original geometry** (in her files the weapon is an identity child of her sword-hand node), plus per-pose edits (`cirith_grip_edits.txt`, from the grip editor). The right-hand versions (`cirith_grip_r.txt`, `cirith_grip_edits_r.txt`) are the left ones reflected across the body mid plane (guard axis flipped to keep a proper rotation).

### 4.4 Animations (WeaponAnim.cs + retarget)
Engine animation assets cannot be added by a module, so the four clips (idle, walk, run, sprint) are **bone overrides** baked from her KUF animations and played by the DLL:
* `transplant/retarget_anim.py`: world-rotation delta (anim · rest⁻¹) of each mapped KUF bone, conjugated by `S = diag(-1,1,-1)` (KUF faces +z, Bannerlord −z; KUF "R Hand" = Bannerlord `l_hand`), applied to the Bannerlord bone's rest world rotation, converted to parent-relative local rotations. Twist helper bones, toes and finger0 follow their parent at rest. Resampled to 60 fps with slerp and written as text (`ModuleData/cirith_anim_<name>.txt`: header `60 N nb rootSpeed natSpeed nwin ...`, per frame `dz rootFwd rootLat` + 28 3x3 matrices). Anim indices: idle 0, walk 1, run 2, sprint (charge/step-forward) 4.
* **Editor joint edits** (`grip_edits.json` `adj`) are baked in: `{bone, rot[deg XYZ], center (-1 = whole clip | frame), width, iso}` with a Gaussian window; `iso` ("move only this joint") makes the next real joints keep their world orientation, passing through the twist helper bones (conjugated by the helper's local rotation) so the limb stays one solid piece. `transplant/stress_adj.py` verifies this with random edits (helper bones rigid, iso keeps the next joints, non-iso keeps descendants relative).
* **Quick symmetry** (`transplant/mirror_clips.py`): the final baked clips are mirrored left↔right (world delta reflected with `M = diag(-1,1,1)`, applied to the counterpart bone) into `cirith_anim_*_m.txt`, so the sword is in the **right** hand; this is the default. `CIRITH_LEFT=1` uses the unmirrored clips with the sword in the left hand.
* Controller (`WeaponAnim.Update`, every tick, only while the Cirith sword is wielded): velocity is smoothed; state = Idle (<0.25 m/s), Walk (<2.4), Run, Sprint (Ctrl+M held + moving forward, >1.2 m/s); the engine's own attack/block/guard actions always win (`GetCurrentActionType(1)`); playback rate follows real speed so feet do not slide; blend weight fades ~0.12 s in / 0.20 s out against the engine pose. While the engine action runs, the visible sword moves to the right hand with the mirrored base grip. Sprint also gives the **x1.5 speed boost**, only when the full outfit is worn (`Boost` changes `MaxSpeedMultiplier` / `CombatMaxSpeedMultiplier`).
* Safety: on death, weapon change, mission end the controller releases the speed boost and removes the sword entity; stress tests (state changes every 0.25 s, weapon swaps every 0.9 s, death while active) are in `SimSubModule.WTest` (`CIRITH_WSTRESS=1`).

---------------------------------------------------------------------------------------------------

## 5. The grip editor (grip_editor/)

A static three.js page (`Start Grip Editor.bat` serves it) so sword placement and joint tweaks can be done by dragging instead of guessing numbers:
* shows **the exact in-game mesh** (`data/game_mesh.json` + `game_atlas.png`, exported by `transplant/export_game_mesh.py` from `slots.bin`) on the Bannerlord skeleton, with spare bones emulated, so editor = game;
* tabs: Idle / Walk / Run / Sprint / T-pose (rest pose; not baked into the game);
* movement balls for the sword and for each body joint; "whole animation" or "only around this moment" edits; **no limits**; "Move only this joint" mode; save writes `grip_edits.json` (version 3).
* `transplant/apply_grip_edits.py <file>` turns it into `cirith_grip_edits.txt`, regenerates the four clips with the joint edits, and runs `mirror_clips.py`. Version-2 files (made on the old gallery model) are carried onto the game mesh with the hand transform from `compute_hand_transform.py`.
Large generated inputs (`game_mesh.json`, `game_atlas.png`, `cirith_armor_rigged.glb`, `clips.json`) are rebuilt by `export_game_mesh.py` / `export_grip_editor.py`.

---------------------------------------------------------------------------------------------------

## 6. How it was debugged (things that cost time)

* Custom weapon crashed at agent spawn → missing `body_name` / `item_holsters`.
* Sword floated beside the hand → grip was measured from a static entity; replaced by the grip from her original geometry; then the editor used the wrong model (gallery arms/hands differ from the game mesh) → editor switched to the exact game mesh.
* Black wrist / mismatch between editor and game → same cause.
* "Worm" forearm after joint edits → the twist helper bone stayed behind when only the next joint compensated; helper bones now ride with their limb (verified by `stress_adj.py`, 736 checks).
* Sword between the legs during attacks → the engine weapon entity's frame is not updated for the hidden mesh; replaced by a mirrored grip on `r_hand`.
* Pressing **1** opens the order menu in a battle (not a weapon slot) → G / Ctrl+G hotkeys.
* Hidden boots with Sandbox + StoryMode + camera mods never reached the battle (slow or an invisible dialog); live `-Visible` launch works.
* Drive letters: the game moved from S: (USB drive that failed) to T:; all scripts use T:.

## 7. Known limits (honest list)
* The real **attack** hand placement was verified only with a forced state (sword sits in the right fist); a real swing was not captured headless. The jump between left-hand and combat-hand frames has no blend.
* Right-hand placement is a mirror of edits made on the left; it may need touch-ups.
* Engine animation assets cannot be added by a module: combat uses the engine's own animations.
* Jiggle amplitudes are hand-tuned; no positive-control comparison against vanilla cloth was done for the DLL rig.
* Enemy NPC armor appears black in the sim (pre-existing, not investigated).
* The sprint hotkey (Ctrl+M) uses `Input.IsKeyDown`; G / Ctrl+G are polled the same way. They were driven by overrides in tests and confirmed working in a live visible run by the user for the sword, but not every key path was exercised headless.

## 8. Quick start
```
# build the DLL
cd enginesim\CirithSim; dotnet build -c Release -o bin --disable-build-servers
# (re)generate clips after editing in the grip editor
cd transplant; python apply_grip_edits.py <grip_edits.json>
# pack textures + armor
cd enginesim; EDITED_TEXTURE=1 python mk_atlas.py ; cd ..; dotnet tool\bin\Release\net8.0\tool.dll "<game>\Modules\Native\AssetPackages" armorpack transplant\out_rig\slots.bin enginesim\CirithSim\module\AssetPackages\cirith_armor.tpac
# play it (boots straight into a battle as Cirith)
cd enginesim; .\run_engine_sim.ps1 -Visible -Char cirith_w_main     # G: draw/sheathe sword, Ctrl+G: get it back, Ctrl+M: sprint
```
