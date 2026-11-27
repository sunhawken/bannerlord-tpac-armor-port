# Test results (all in the real Bannerlord engine v1.5.7, headless hidden-desktop sim; dates 2026-11-26/27)

| test | result |
|---|---|
| Game boots from T: with the module (Steam running) | OK (~45 s to the battle) |
| Module passive on a normal launch (no test variable) | OK: main menu reached, "harness=False", no Custom Battle started, no exit within 150 s |
| Armor + areola + original skin | renders (docs/screenshots) |
| Jiggle (breast/glute/ponytail) in the normal runtime path | OK: "jiggle rig attached", motion visible (docs/screenshots/motion_chest.png) |
| Her weapon spawns (no crash) | OK once the item had body_name + item_holsters (without them the engine crashes at agent spawn) |
| Weapon in the game with her texture | OK (docs/screenshots/sword_in_hand.png) |
| Controller states idle -> walk -> run -> sprint -> run -> idle | OK, each state logged and photographed (weapon_proxy_final.png) |
| Restore when the weapon is swapped / sheathed | OK: "released: weapon no longer wielded", engine animations resume; re-wield re-attaches |
| Stress: 25 s of state changes every 0.25 s + 28 weapon swaps | 78 transitions, 0 exceptions, sword entity created 15 / removed 15 (no leak) |
| Agent killed while the controller is active | OK: "released: dead" + sword entity removed, no exception |
| Sprint x1.5 speed (full outfit) | engine running simulation 4.03 -> 6.05 m/s (x1.50), restored afterwards; not boosted with only one piece |
| Grip edit file -> game | OK: an edit changes the sword in the game as in the editor (docs/screenshots/edit_check.png) |
| Inventory keeper (campaign) | roster logic OK headless (empty -> adds 4, complete -> 0, after selling -> 1); NOT run inside a real campaign |

## Known limitations
- Module animation actions are not merged by the engine, so the clips are bone overrides (no engine animation asset, no IK).
- The engine's own weapon attachment ignores bone overrides; the visible sword is a DLL-placed entity and the item itself has an invisible mesh. Combat hit detection stays the engine's own and uses the item's hitbox at the engine's hand pose, not the visible sword.
- The charge attack and the hand swap were dropped by request.
- Sword placement per pose is waiting for the user's grip_edits.json.
- Sprint key (V) read with Input.IsKeyDown; verified through a test override only, not with a real keyboard.
- Not tested: saving/loading a campaign, mission unload in a real campaign, multiplayer.
