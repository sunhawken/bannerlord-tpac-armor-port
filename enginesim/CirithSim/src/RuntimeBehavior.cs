using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CirithSim
{
    // Normal-game runtime (no test harness): runs the jiggle rig on every agent that wears a Cirith piece and the charge ability on the player.
    public class CirithRuntimeBehavior : MissionBehavior
    {
        protected override void OnEndMission() { try { foreach (var kv in wanim) { try { kv.Value.Dispose(); } catch { } } wanim.Clear(); } catch { } }
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        readonly Dictionary<Agent, Jiggle> jig = new Dictionary<Agent, Jiggle>(); readonly Dictionary<Agent, WeaponAnim> wanim = new Dictionary<Agent, WeaponAnim>(); readonly Charge charge = new Charge(); bool err; float clean;

        static bool WearsAny(Agent a)
        {
            try { var eq = a.SpawnEquipment; foreach (var i in new[] { EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves }) { var id = eq[i].Item?.StringId; if (id != null && id.StartsWith("cirith_")) return true; } } catch { }
            return false;
        }
        bool ctlSet; TaleWorlds.Engine.Camera acam; string shotDir; int lastShot = -1; float life; bool autoDone, gWas, giveTested;
        static bool HasSword(Agent a) { try { for (var i = EquipmentIndex.Weapon0; i <= EquipmentIndex.Weapon3; i++) if (a.Equipment[i].Item?.StringId == WeaponAnim.Item) return true; } catch { } return false; }
        static void Wield(Agent a, bool on) { try { if (on) { for (var i = EquipmentIndex.Weapon0; i <= EquipmentIndex.Weapon3; i++) if (a.Equipment[i].Item?.StringId == WeaponAnim.Item) { a.TryToWieldWeaponInSlot(i, Agent.WeaponWieldActionType.WithAnimation, false); break; } } else a.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.WithAnimation); } catch (Exception e) { SimSubModule.L("wield err " + e.Message); } }
        static bool SafeCtrl() { try { return TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftControl) || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightControl); } catch { return false; } }
        bool cWas; bool SafeCombo() { try { bool d = SafeCtrl() && TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.G); bool r = d && !cWas; cWas = d; return r; } catch { return false; } }
        static void GiveSword(Agent a)
        {
            try
            {
                if (!HasSword(a))
                {
                    var it = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(WeaponAnim.Item); if (it == null) { SimSubModule.L("give sword: item not found"); return; }
                    EquipmentIndex slot = EquipmentIndex.None;
                    for (var i = EquipmentIndex.Weapon0; i <= EquipmentIndex.Weapon3; i++) if (a.Equipment[i].IsEmpty) { slot = i; break; }
                    if (slot == EquipmentIndex.None) { slot = EquipmentIndex.Weapon3; a.RemoveEquippedWeapon(slot); }
                    var mw = new MissionWeapon(it, null, null); a.EquipWeaponWithNewEntity(slot, ref mw); SimSubModule.L("runtime: Ctrl+G -> sword added to slot " + slot);
                }
                Wield(a, true); SimSubModule.L("runtime: Ctrl+G -> sword drawn");
            }
            catch (Exception e) { SimSubModule.L("give sword err " + e.Message); }
        }
        bool SafeG() { try { bool d = TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.G); bool r = d && !gWas; gWas = d; return r; } catch { return false; } }
        public override void OnMissionTick(float dt)
        {
            life += dt; if (SimSubModule.Harness && Environment.GetEnvironmentVariable("CIRITH_RUNTIME") == "1" && Environment.GetEnvironmentVariable("CIRITH_LIVE") != "1" && life > (Environment.GetEnvironmentVariable("CIRITH_ATTACKTEST") == "1" ? 40f : 20f)) { SimSubModule.L("runtime test: done (jiggle agents tracked: " + jig.Count + ", main=" + (Agent.Main != null) + ")"); TaleWorlds.Engine.Utilities.ExitProcess(0); }
            try
            {
                var m = Agent.Main;
                if (m != null && m.IsActive() && m.AgentVisuals != null && (SafeCombo() || (!giveTested && life > 3f && Environment.GetEnvironmentVariable("CIRITH_GIVETEST") == "1" && (giveTested = true)))) GiveSword(m);   // Ctrl+G: always get the sword back (adds it to a free weapon slot if she does not carry it) and draw it
                // hand-held weapon: drawn automatically once at the start, key G draws / sheathes it (number keys are the order menu in a battle)
                if (m != null && m.IsActive() && m.AgentVisuals != null && HasSword(m))
                {
                    if (!autoDone && life > 2f) { autoDone = true; Wield(m, true); SimSubModule.L("runtime: sword drawn automatically"); }
                    if (!SafeCtrl() && SafeG()) { bool on = WeaponAnim.IsWielding(m); Wield(m, !on); SimSubModule.L("runtime: key G -> sword " + (on ? "sheathed" : "drawn")); }
                }
                if (Environment.GetEnvironmentVariable("CIRITH_ATTACKTEST") == "1" && m != null && m.IsActive())
                {   // test: swing with the engine's default attack, screenshots while it runs
                    float at = life - 5f; if (at > 0 && at < 8f)
                    {
                        if (shotDir == null) { shotDir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SimSubModule.LogPath), "shots"); System.IO.Directory.CreateDirectory(shotDir); }
                        try
                        {   // side camera 3.2 m away
                            var ms = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as TaleWorlds.MountAndBlade.View.Screens.MissionScreen;
                            if (ms != null) { if (acam == null) acam = TaleWorlds.Engine.Camera.CreateCamera(); var f = m.LookDirection; f.z = 0; f.Normalize(); var side = new TaleWorlds.Library.Vec3(-f.y, f.x, 0); var target = m.Position + new TaleWorlds.Library.Vec3(0, 0, 1.0f); var camPos = target + side * (Environment.GetEnvironmentVariable("CIRITH_CAMR") == "1" ? -3.0f : 3.0f) + f * 1.0f + new TaleWorlds.Library.Vec3(0, 0, 0.2f); var look = target - camPos; look.Normalize(); var mm = TaleWorlds.Library.Mat3.CreateMat3WithForward(ref look); mm.RotateAboutSide(1.5707964f); acam.Frame = new TaleWorlds.Library.MatrixFrame(mm, camPos); acam.SetFovVertical(0.9f, 16f / 9f, 0.05f, 2000f); typeof(TaleWorlds.MountAndBlade.View.Screens.MissionScreen).GetProperty("CustomCamera").SetValue(ms, acam); }
                        }
                        catch (Exception ce) { SimSubModule.L("atk cam err " + ce.Message); }
                        if (!ctlSet) { ctlSet = true; try { m.Controller = AgentControllerType.None; } catch { } } int ph = (int)(at / 1.0f); if (at % 2f < 1.2f) { try { m.MovementFlags = Agent.MovementControlFlag.AttackRight; } catch (Exception e) { SimSubModule.L("atk err " + e.Message); } }
                        int sh = (int)(at / 0.35f); if (sh != lastShot) { lastShot = sh; try { TaleWorlds.Engine.Utilities.TakeScreenshot(System.IO.Path.Combine(shotDir, "atk_" + sh.ToString("00") + ".bmp")); } catch { } SimSubModule.L("atk shot " + sh + " actionType=" + m.GetCurrentActionType(1) + " hold=" + WeaponAnim.IsWielding(m)); }
                    }
                    if (at > 9f) TaleWorlds.Engine.Utilities.ExitProcess(0);
                }
                foreach (var a in Mission.Current.Agents)
                {
                    if (a != null && a.IsActive() && a.AgentVisuals != null && WeaponAnim.IsWielding(a) || (a != null && wanim.ContainsKey(a))) { if (!wanim.TryGetValue(a, out var wa)) { wa = new WeaponAnim(); wanim[a] = wa; SimSubModule.L("runtime: weapon controller attached to " + (a.Name ?? "agent")); } wa.Update(a, dt); }
                    if (a == null || !a.IsActive() || a.AgentVisuals == null || !WearsAny(a)) continue;
                    if (!jig.TryGetValue(a, out var j)) { j = new Jiggle(); jig[a] = j; SimSubModule.L("runtime: jiggle rig attached to " + (a.Name ?? "agent")); }
                    j.Update(a, dt);
                }
                clean += dt; if (clean > 5f) { clean = 0; foreach (var k in jig.Keys.Where(k => k == null || !k.IsActive()).ToList()) jig.Remove(k); foreach (var k in wanim.Keys.Where(k => k == null || !k.IsActive()).ToList()) { try { wanim[k].Release(k, "agent inactive"); wanim[k].Dispose(); } catch { } wanim.Remove(k); } }
            }
            catch (Exception e) { if (!err) { err = true; SimSubModule.L("runtime error " + e); } }
        }
    }
}
