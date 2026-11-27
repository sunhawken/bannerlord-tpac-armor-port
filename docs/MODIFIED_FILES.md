# Modified / created files (Cirith Bannerlord port)

The game's own files were never edited. Everything lives in `T:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\CirithSim` (installed by `enginesim\run_engine_sim.ps1` from `enginesim\CirithSim\module` + `bin\CirithSim.dll`).
The only other change to the game folder: the interrupted S: -> T: move was left to the user's FastCopy (S: is now empty). Backups of the files edited for the path move: `_backup_S_paths\`.

## Module content (`enginesim\CirithSim\module`)
| file | what |
|---|---|
| SubModule.xml | module definition; items also load for Campaign / CampaignStoryMode |
| AssetPackages\cirith_armor.tpac | rigged armor (head/body/legs/hands) with the ORIGINAL texture atlas |
| AssetPackages\cirith_sword.tpac / cirith_sword_hidden.tpac | her weapon mesh+texture / invisible 1 mm twin used by the item |
| AssetPackages\cirith_probe.tpac | developer axis probe (colored bars), not used in play |
| ModuleData\cirith_items.xml | armor items, `cirith_sword` (needs body_name + item_holsters), `cirith_probe` |
| ModuleData\cirith_chars.xml | test characters (cirith_test, cirith_sword_test, cirith_w_main, cirith_probe_test, ...) |
| ModuleData\cirith_anim_{idle,walk,run,charge}.txt | her KUF clips retargeted to the 28-bone skeleton (60 fps) |
| ModuleData\cirith_grip.txt | sword grip frame in the l_hand bone frame (from her original files) |
| ModuleData\cirith_grip_edits.txt | per-pose sword edits written from the grip editor (empty = none) |
| ModuleData\cirith_spare.txt | ponytail pivot for the jiggle rig |

## DLL source (`enginesim\CirithSim\src`)
SimSubModule.cs (entry point; test harness only when env CIRITH_CHAR is set), RuntimeBehavior.cs (normal play), Jiggle.cs (breast/glute/ponytail), WeaponAnim.cs (weapon-specific controller + DLL-placed sword), Charge.cs (old outfit charge test, unused in play), InventoryKeeper.cs (campaign inventory), CirithSim.csproj (references now T:).

## Pipeline scripts
transplant: dump_slots.py (spare-bone re-skin), gem_mask.py (areola; all black-transparency cuts OFF unless CUTOUTS=1; skin lift OFF unless LIFT=1), retarget_anim.py, dump_weapon.py, dump_probe.py, export_grip_editor.py, apply_grip_edits.py.
enginesim: mk_atlas.py (original atlas; edited atlas only with EDITED_TEXTURE=1), mk_weapon_tex.py, seam_scan.py / seam_fix.py / skin_islands.py (seam tools), run_engine_sim.ps1 (T: paths, auto-closes the crash dialog), cloth_batch.py, run_clothtest.sh, views_*.txt.
src: tool\Program.cs (new verbs wdiag + weapon support in armorpack), gen_config.py. grip_editor\ (editor page, README, Start Grip Editor.bat). README.md / HANDOFF.md.
