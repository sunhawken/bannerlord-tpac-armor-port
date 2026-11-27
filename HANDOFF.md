# HANDOFF - Bannerlord Cirith armor port (written 2026-11-26)

## Goal of the project
Port the "Cirith" model (KUF dark elf C_DS_NZ, her ORIGINAL shape, textures, UVs) into the REAL Bannerlord v1.5.7 engine as a full-body rigged armor (slots head/body/legs/hands), test it with a headless engine sim, and make
the ponytail + breast + glute jiggle behave like the viewer http://127.0.0.1:8766/#cirith_armor_rigged (user's own viewer; NEVER touch the user's gallery on 8777).

## State: DONE and verified in the real engine
Own geometry rigged to the 28-bone human skeleton, own atlas (512x1024 DXT5, alpha-cut trims, gutters), own material, items/characters, baked hanging ponytail, arms angled to skeleton rest (~19 deg, one angle for ALL slots),
her converted clawed hands (grip fit ~5 cm + wrist sleeve), all LODs replaced, gem cut-out (black around hip/breast gems transparent), lifted chest skin, a dark painted areola (very dark, bigger) at atlas px (218,76).
Areola decal geometry experiments were ABANDONED on request (code kept behind env AREOLA_DECAL=1; pink debug probes were removed; package is clean).

## State: NOT done
1. **Jiggle / hair simulation in-game (top task).** Engine cloth needs two compiled data blobs (cook = .tcc type b97e0f10, map = .tcm type af41613e). Layout parsed exactly (enginesim/cookparse.py); zeroing tests on a cloned vanilla dress:
   arrays A, H, I essential; D partial; B, C, F, G optional (see README in the GitHub repo). A/H/I semantics unknown -> cannot generate yet.
2. The official Bannerlord Modding Kit (Steam tool 1393600, ~20 GB, Cloth Editor writes the blobs) was started installing to M:\SteamLibrary (user's Steam, license accepted by user). NOT confirmed finished. Docs: moddocs.bannerlord.com/editor/resource-editors/cloth_simulation.
3. **USER'S LATEST DIRECTION: do the jiggle/hair with a DLL mod (our CirithSim.dll hooking the engine), headless.** Plan being executed:
   - Engine skeleton has exactly 28 bones (same as my rig): pelvis, l/r_thigh, l/r_calf, l/r_foot, l/r_toe0, spine, spine1, spine2, neck, head, l/r_clavicle, l/r_upperarm_twist(+1), l/r_foretwist(+1), l/r_hand, l/r_finger0.
   - Spare bones: l_finger0, r_finger0 (her meshes don't use them); l_toe0/r_toe0 become free if the legs/boots mesh is re-skinned to the foot bones only. Proposal: l_finger0 = both breasts, r_finger0 = ponytail pendulum, l_toe0/r_toe0 = glutes.
   - Re-skin in transplant/dump_slots.py: add the spare bone as an influence with weight = the vertex's cloth alpha (breast/glute zones on the body slot, ponytail on the head slot; alpha is already painted: body 0..96, head 0..235), others scaled by (1-a).
   - DLL: each tick compute spring-damper from chest/pelvis/head motion; set the spare bone entitial frame = rest frame + offset (translation for jiggle, rotation about the root for the ponytail) via Skeleton.SetBoneLocalFrame(idx, parentEntitial.TransformToLocal(desired)) + UpdateEntitialFramesFromLocalFrames().
   - FIRST verify the hook sticks to the render: CirithSim.dll already has `BoneTest()` (env CIRITH_BONETEST="head:0,0,0.3" lifts a bone every tick from OnMissionTick). Build is done; it has NOT been run yet. If animation overwrites after our tick, try other hooks (MissionBehavior.OnAfterMissionTick, application tick, Harmony in Modules\Bannerlord.Harmony is available).
   - Verify motion with the existing motion test (CIRITH_MOTION=1; she slides 3 s at 3.5 m/s and stops; compare against vanilla cloth dress as positive control).

## How to run things (paths)
- Project root: C:\Users\Trey\Desktop\BannerlordGallery   (repo copy for GitHub: C:\Users\Trey\Desktop\bannerlord-tpac-armor-port -> https://github.com/sunhawken/bannerlord-tpac-armor-port, public; commit WITHOUT a Claude co-author trailer, user strips them from their repos)
- Game: S:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord  (S: is a LOCAL USB drive "Skyrim"; it once lost its letter - if S: is missing check Get-Volume; do NOT map it to a network share, .NET refuses to load DLLs from network paths)
- Pipeline: transplant\dump_slots.py -> transplant\out_rig\slots.bin ; enginesim\mk_atlas.py (+ transplant\gem_mask.py, venice_areola.py) -> tex\cirith_main.dxt5 ;
  `dotnet src\tool\bin\Release\net8.0\tool.dll "<game>\Modules\Native\AssetPackages" armorpack transplant\out_rig\slots.bin enginesim\CirithSim\module\AssetPackages\cirith_armor.tpac`  (build src\tool with `dotnet build -c Release --disable-build-servers`; bash env vars LOD0ONLY must be UNSET so all LODs are replaced)
- Engine sim (hidden desktop, no window, no mouse grab): `powershell -File enginesim\run_engine_sim.ps1 -Char cirith_test [-Views views_full.txt] [-Visible] [-Timeout 520]`
  Screenshots land in enginesim\out\shots\*.png. Mod source: enginesim\CirithSim\src\SimSubModule.cs, build `dotnet build -c Release -o bin --disable-build-servers` inside enginesim\CirithSim.
- **BOOT IS SLOW NOW (~4-5 min to the battle)** - always pass -Timeout 520+ (earlier 150-220 s timeouts looked like hangs but were not).
- Batch cloth experiments in ONE game session: enginesim\cloth_batch.py "<label>" "name:ZEROLIST;..." (runtime body-item swap).
- Free camera views: enginesim\views_full.txt (name az elev dist targetHeight fov).
- Characters: cirith_test (all 4 pieces + sword), cirith_t_h/l/b/hd (single slot), cirith_t_dress (vanilla-dress clone tests).

## Environment gotchas learned
- run_engine_sim.ps1 sets engine_config postfx_motion_blur=0, postfx_dof=0, disable_sound=1 (headless only) and restores them; sets safely_exited=1 to avoid the Safe Mode prompt.
- The user's VR stack (Oculus OVRServer, Virtual Desktop streamer) was running; I closed Streamer+OVRServer when they said "turn off VR". Module BannerlordVR exists (needs Bannerlord.Harmony, Sandbox, StoryMode); launch with `-Visible -ExtraModules 'Bannerlord.Harmony*BannerlordVR'` only if the user asks.
- Venice key: user pasted it into a popup once; it was NOT stored anywhere. Never write keys to disk/logs.
- TpacTool writer fixes are in src\TpacTool (git diff saved as enginesim\tpactool_writer_fixes.patch): vertex-stream offsets start after the 224-byte table; no count prefix; Material.WriteMetadata implemented. Metamesh Name must equal item mesh name.
- User style: very direct, frustrated by misreads - follow instructions literally, stop immediately when told to stop, do not ask unless truly blocked.

## Immediate next steps for the new session
1. Run the bone probe: `$env:CIRITH_BONETEST='head:0,0,0.3'` + a normal headless run (-Timeout 520), look at the screenshot: did the head move?
2. If yes: implement the jiggle DLL (spring dynamics + spare-bone re-skin in dump_slots.py + pack + test with the motion test).
3. If the hook does not stick: find the right hook point (after animation update) or use Harmony; report to the user.
4. Keep README/memory (memory file project_bannerlord_gallery.md) and the GitHub guide updated.

## UPDATE 2026-11-26 (session 2): DLL JIGGLE RIG WORKS IN THE ENGINE
- Bone probe result: `Skeleton.SetBoneLocalFrame` + `UpdateEntitialFramesFromLocalFrames` from `MissionBehavior.OnMissionTick` STICKS to the render (CIRITH_BONETEST=head:0,0,0.3 raised the head ~0.3 m; animation overwrites every tick, so set it every tick). No Harmony needed.
- Re-skin (transplant/dump_slots.py, env NOSPARE=1 disables): breasts (body z>1.15, cloth alpha/96) -> l_finger0; glutes (body z<1.1) -> l_toe0 (x<0) / r_toe0 (x>0); ponytail (head slot, alpha/235) -> r_finger0; legs+legs trim toe0 -> foot. Trim verts near cloth verts inherit the alpha. Pack with armorpack, LOD0ONLY unset. Writes enginesim/CirithSim/module/ModuleData/cirith_spare.txt (ponytail pivot).
- DLL: enginesim/CirithSim/src/Jiggle.cs (called every OnMissionTick from SimBehavior). Bone = PfAnim * L * (PfRest^-1 * boneRest) with Pf = spine2 / pelvis / head, rest from `Skeleton.GetBoneEntitialRestFrame`. Breast/glute = spring-dampers (6.5/4.5 Hz) driven by smoothed chest/pelvis world acceleration; ponytail = world pendulum (Leff 0.5, zeta 0.3, max 0.8 rad) about the root pivot. Env: CIRITH_JIG=<gain> (default 1, 0 = off), CIRITH_JIGLOG=1|2.
- Verified with CIRITH_MOTION=1: glutes/hip trims visibly move, ponytail swings. Frames in Modules\CirithSim\shots; montages enginesim\out\motion_montage3.png, motion_chest.png. Vanilla-dress positive control NOT re-run for the DLL rig. Breasts not yet inspected from a front/side close-up (use a CIRITH_VIEWS file + CIRITH_MOTION is exclusive - the motion camera is side-on, so add a front camera mode if needed).
- Gotchas: engine runs ~180 fps headless (dt 5.6 ms); screenshots cause frame hitches -> Jiggle re-syncs velocity when dt>0.1. Boot is now ~1 min. Environment variables must be set in the same shell call as the run.
- TODO: tune amplitudes (gain 1 = up to 3.5 cm), front/side breast close-up, positive control comparison, bake settings into a config file.

## UPDATE 2026-11-26 (areola fix)
Areola was painted at atlas (218,76) = vertices at y=-0.075 = her BACK (front is +y; the old session assumed -y). Moved to the front breast UV island (138,115), radii 12x9.5 px (gem_mask.py). Verified front/back with enginesimiews_areola.txt (CIRITH_VIEWS; target height is metres above feet, use 1.3 for chest). Nipple apex triangles have degenerate UVs (41-76,221-253, shared with other geometry) so the very tip is not painted.

## UPDATE 2026-11-26 (seamless arms/chest)
Cause: only the torso skin block of the atlas got the gamma lift (lum ~89); the arm/shoulder skin islands stayed at ~62 -> tone step at the shoulder, plus black island borders bleeding in at UV seams. Fix in transplant/gem_mask.py: same gamma lift on arm skin texels (arm triangles outside the torso block), 4 px skin gutter padding, and enginesim/seam_fix.py (repaints dark-grey texels at UV-seam edges whose other side is skin). enginesim/seam_scan.py measures seam colour differences (arm-region mean diff 24 -> 9.7). Minor leftover: faint darker patch on top of the shoulders.

## UPDATE 2026-11-26 (precise cut-outs, back restored, viewer skin)
- transplant/gem_mask.py: skin is now the ORIGINAL (viewer) skin - the gamma lift is off unless env LIFT=1 (areola kept). Precise "outside black" flood cut (seeded from the triangle-mask border, never crossing the gold outline) for the pendant diamond and the hoop earrings (_outside_black). The bead strips / 4-gem cluster (atlas x<95, y>=170: hip-band fringe and ponytail rings) are NOT cut any more: transparency + DXT5 blocks + mips made those thin beads/gold vanish on her back. Region at atlas left edge (chest teal gem) still cut. Gutter padding and seam_fix kept.

## UPDATE 2026-11-26 (hips + collar black transparency)
gem_mask.py _outside_black_tris(): flood the TRUE black (lum<24, sat<30 - the dark-grey skin of the same islands is lum ~40+ and must stay) from the border of the hip-band triangles (body z .78-1.2) and the collar triangles (head slot z 1.40-1.56) inwards, never across non-black texels, and keep a 2 px opaque rim around every gold/teal/bead texel (halo) so DXT5 alpha blocks and mips do not erase thin details. Result: see-through gaps between the fringe beads, gems and gold intact. Not touched: the black back panel (centre back, gold diamond top) and the black collar band inside its gold outline.

## UPDATE (breast gems)
Same protected outside-black cut applied to the breast bead net / gems (body tris z 1.2-1.52, |x|<0.19) in gem_mask.py.

## UPDATE (breast bead net fix)
Breast bead net: enclosed black between beads is now cut too (everywhere=True, only black within 4 px of bead/gold/gem texels, halo 0); true-black threshold is lum<14 for ALL cuts (dark skin shading is lum 15-60 and made holes at thr 24-48). Areola is painted after the cut so it stays opaque.

## UPDATE 2026-11-26 - CIRITH CHARGE (KUF animation 4 + x1.5 speed) - DONE, mod wrapped up
- Source clip: Cirith's own KUF animation 4 (CHARGE / STEP_FORWARD, 19 frames, 0.63 s) from C_DS_NZ.glb (read only). `transplant/retarget_anim.py [anim]` bakes it onto the 28-bone skeleton: per mapped bone the world-space rotation change (anim * rest^-1) is conjugated by Q=diag(-1,1,-1) (KUF faces +z, Bannerlord -z => her model is turned 180 deg about the vertical axis; Cirith L = Bannerlord r), applied to the Bannerlord rest rotation, converted to local rotations; pelvis gets the vertical bob (scale 0.0098). Output: module/ModuleData/cirith_charge.txt (60 fps). Checked: foot/head displacement vs KUF within 1-4 cm.
- Why not a real engine action: the engine does NOT merge module action_types.xml/action_sets.xml (ActionIndexCache index -1; only Native defines them), so the clip is played by the DLL overriding bone frames each tick (src/Charge.cs, same hook as the jiggle). Not an engine animation asset (no IK / no blend tree) - say so.
- Use: wear ALL FOUR Cirith pieces (cirith_head/body/legs/hands), press V (env CIRITH_CHARGEKEY=<InputKey> to change): charge plays 5 s (0.15 s blend in, 0.25 s out), 8 s cooldown, speed x1.5 via DrivenProperty MaxSpeedMultiplier + CombatMaxSpeedMultiplier then UpdateCustomDrivenProperties() once (SetMaximumSpeedLimit on top double-applies - do not). Only Agent.Main reacts to the key.
- Verified headless (CIRITH_CHARGETEST=1): engine running simulation top speed 4.03 -> 6.05 m/s (x1.50), restored after; with only the body piece (cirith_t_b) no charge and no boost. Frames: enginesim/out/charge_frames.png. Agents do not walk in the headless sim (AI/deployment), so real running speed was verified through the engine's own simulation, not by measuring displacement.
- Install: copy enginesim\CirithSim\module + bin\CirithSim.dll into <game>\Modules\CirithSim (run_engine_sim.ps1 does this).
