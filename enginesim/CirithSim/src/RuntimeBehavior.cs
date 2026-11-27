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
        float life; bool autoDone, gWas, giveTested;
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
            life += dt; if (SimSubModule.Harness && Environment.GetEnvironmentVariable("CIRITH_RUNTIME") == "1" && Environment.GetEnvironmentVariable("CIRITH_LIVE") != "1" && life > 20f) { SimSubModule.L("runtime test: done (jiggle agents tracked: " + jig.Count + ", main=" + (Agent.Main != null) + ")"); TaleWorlds.Engine.Utilities.ExitProcess(0); }
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
