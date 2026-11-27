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
