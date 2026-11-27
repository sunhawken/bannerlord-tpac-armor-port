using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
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
        int lastVoice = -1; bool ctlSet; TaleWorlds.Engine.Camera acam; string shotDir; int lastShot = -1; float life; bool autoDone, gWas, giveTested;
        static bool HasSword(Agent a) { try { for (var i = EquipmentIndex.Weapon0; i <= EquipmentIndex.Weapon3; i++) { var id = a.Equipment[i].Item?.StringId; if (id == WeaponAnim.Item || id == WeaponAnim.ItemVr) return true; } } catch { } return false; }
        // VR: the hidden-mesh item becomes its visible twin so the ENGINE draws it like any normal right-hand weapon (replaced in place - nothing is dropped)
        static void VrSwap(Agent a)
        {
            try
            {
                var it = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(WeaponAnim.ItemVr); if (it == null) { SimSubModule.L("VR swap: item cirith_sword_vr not found"); return; }
                var wielded = a.GetPrimaryWieldedItemIndex();
                for (var i = EquipmentIndex.Weapon0; i <= EquipmentIndex.Weapon3; i++)
                    if (a.Equipment[i].Item?.StringId == WeaponAnim.Item)
                    {
                        var mw = new MissionWeapon(it, null, null); a.EquipWeaponWithNewEntity(i, ref mw); SimSubModule.L("VR swap: " + i + " -> cirith_sword_vr");
                        if (wielded == i) a.TryToWieldWeaponInSlot(i, Agent.WeaponWieldActionType.Instant, false);
                    }
            }
            catch (Exception e) { SimSubModule.L("VR swap err " + e.Message); }
        }
        static void Wield(Agent a, bool on)
        {
            try
            {
                if (on)
                {
                    for (var i = EquipmentIndex.Weapon0; i <= EquipmentIndex.Weapon3; i++)
                    {
                        var id2 = a.Equipment[i].Item?.StringId;
                        if (id2 == WeaponAnim.Item || id2 == WeaponAnim.ItemVr) { a.TryToWieldWeaponInSlot(i, Agent.WeaponWieldActionType.WithAnimation, false); break; }
                    }
                }
                else a.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.WithAnimation);
            }
            catch (Exception e) { SimSubModule.L("wield err " + e.Message); }
        }
        static bool SafeCtrl() { try { return TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.LeftControl) || TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.RightControl); } catch { return false; } }
        bool cWas; bool SafeCombo() { try { bool d = SafeCtrl() && TaleWorlds.InputSystem.Input.IsKeyDown(TaleWorlds.InputSystem.InputKey.G); bool r = d && !cWas; cWas = d; return r; } catch { return false; } }
        static void GiveSword(Agent a)
        {
            try
            {
                if (!HasSword(a))
                {
                    var it = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(WeaponAnim.ItemId); if (it == null) { SimSubModule.L("give sword: item not found"); return; }
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
        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow) { try { if (affectedAgent == Agent.Main) CirithVoice.Trigger(agentState == AgentState.Killed || agentState == AgentState.Unconscious ? "Death" : "Pain", "removed " + agentState); } catch { } }
        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData) { try { if (affectedAgent == Agent.Main) CirithVoice.Trigger("Pain", "hit"); } catch { } }
        // silence the engine's own voice for the player: the agent visuals carry a voice-definition index (voice_definitions.xml); -1 = no definition -> nothing to play
        object silencedVisuals; float silenceCheck;
        void SilenceVanilla(Agent m)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("CIRITH_KEEPVANILLA") == "1") return;
                var v = m.AgentVisuals; if (v == null) return;
                if (!ReferenceEquals(silencedVisuals, v))
                {
                    string before = ""; try { before = m.GetAgentVoiceDefinition(); } catch { }
                    v.SetVoiceDefinitionIndex(-1, 1f); silencedVisuals = v;
                    string after = ""; try { after = m.GetAgentVoiceDefinition(); } catch { }
                    SimSubModule.L("voice: vanilla voice silenced for the player (definition '" + before + "' -> '" + after + "')");
                }
            }
            catch (Exception e) { SimSubModule.L("voice silence err " + e.Message); }
        }
        string lastAct = "";
        void VoiceFromActions(Agent m)
        {
            try
            {
                var act = m.GetCurrentActionType(1).ToString(); if (act == lastAct) return; var prev = lastAct; lastAct = act;
                if (act.StartsWith("Attack", StringComparison.OrdinalIgnoreCase) && !prev.StartsWith("Attack", StringComparison.OrdinalIgnoreCase)) CirithVoice.Trigger(new Random().Next(5) == 0 ? "Yell" : "Grunt", "action " + act);
                else if (act.IndexOf("Jump", StringComparison.OrdinalIgnoreCase) >= 0) CirithVoice.Trigger("Jump", "action " + act);
                else if (act.IndexOf("Stun", StringComparison.OrdinalIgnoreCase) >= 0 || act.IndexOf("Bash", StringComparison.OrdinalIgnoreCase) >= 0) CirithVoice.Trigger("Stun", "action " + act);
            }
            catch { }
        }
        // ---- measurement (CIRITH_GRIPMEASURE=1): where does the ENGINE put a wielded weapon relative to the r_hand bone? (weapon entity frame vs hand bone frame, in agent space)
        float measT; int measN;
        void GripMeasure(Agent m)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("CIRITH_GRIPMEASURE") != "1" || measN >= 40) return; measT += 0.016f; if (measT < 0.4f) return; measT = 0;
                var pi = m.GetPrimaryWieldedItemIndex(); if (pi == EquipmentIndex.None) { if (measN++ % 5 == 0) SimSubModule.L("gripmeasure: nothing wielded"); return; }
                var we = m.GetWeaponEntityFromEquipmentSlot(pi); if (!we.IsValid) { SimSubModule.L("gripmeasure: weapon entity invalid for " + m.Equipment[pi].Item?.StringId); measN++; return; }
                var gf = m.AgentVisuals.GetEntity().GetGlobalFrame(); var wf = we.GetGlobalFrame(); var sk = m.AgentVisuals.GetSkeleton(); var hf = sk.GetBoneEntitialFrame(26);
                float sc = gf.rotation.s.Length;
                Vec3 N(Vec3 v) { v.Normalize(); return v; }
                var gs = N(gf.rotation.s); var gfv = N(gf.rotation.f); var gu = N(gf.rotation.u);
                Vec3 L(Vec3 d) => new Vec3(Vec3.DotProduct(d, gs), Vec3.DotProduct(d, gfv), Vec3.DotProduct(d, gu));      // world vector -> agent entity space
                var wo = L(wf.origin - gf.origin) * (1f / sc); var ws = L(N(wf.rotation.s)); var wff = L(N(wf.rotation.f)); var wu = L(N(wf.rotation.u));
                var hs = N(hf.rotation.s); var hff = N(hf.rotation.f); var hu = N(hf.rotation.u);
                Vec3 H(Vec3 d) => new Vec3(Vec3.DotProduct(d, hs), Vec3.DotProduct(d, hff), Vec3.DotProduct(d, hu));          // agent space -> hand bone space
                var ro = H(wo - hf.origin); var rs = H(ws); var rf = H(wff); var ru = H(wu);
                SimSubModule.L($"gripmeasure {m.Equipment[pi].Item?.StringId} act={m.GetCurrentActionType(1)} scale={sc:0.000} | weapon in r_hand frame: origin=({ro.x:0.0000},{ro.y:0.0000},{ro.z:0.0000}) s=({rs.x:0.000},{rs.y:0.000},{rs.z:0.000}) f=({rf.x:0.000},{rf.y:0.000},{rf.z:0.000}) u=({ru.x:0.000},{ru.y:0.000},{ru.z:0.000})");
                measN++;
            }
            catch (Exception e) { SimSubModule.L("gripmeasure err " + e.Message); measN++; }
        }
        public override void OnMissionTick(float dt)
        {
            life += dt; if (SimSubModule.Harness && Environment.GetEnvironmentVariable("CIRITH_RUNTIME") == "1" && Environment.GetEnvironmentVariable("CIRITH_LIVE") != "1" && life > (Environment.GetEnvironmentVariable("CIRITH_ATTACKTEST") == "1" ? 40f : Environment.GetEnvironmentVariable("CIRITH_VOICETEST") == "1" ? 25f : 20f)) { SimSubModule.L("runtime test: done (jiggle agents tracked: " + jig.Count + ", main=" + (Agent.Main != null) + ")"); TaleWorlds.Engine.Utilities.ExitProcess(0); }
            try
            {
                var m = Agent.Main;
                if (m != null && m.IsActive() && m.AgentVisuals != null && (SafeCombo() || (!giveTested && life > 3f && Environment.GetEnvironmentVariable("CIRITH_GIVETEST") == "1" && (giveTested = true)))) GiveSword(m);   // Ctrl+G: always get the sword back (adds it to a free weapon slot if she does not carry it) and draw it
                // hand-held weapon: drawn automatically once at the start, key G draws / sheathes it (number keys are the order menu in a battle)
                if (m != null && m.IsActive() && m.AgentVisuals != null && HasSword(m))
                {
                    if (!autoDone && life > 2f) { autoDone = true; if (CirithVR.Active) VrSwap(m); else Wield(m, true); SimSubModule.L("runtime: sword drawn automatically"); }
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
                if (Environment.GetEnvironmentVariable("CIRITH_VOICETEST") == "1" && m != null && m.IsActive())
                {   // test: ask the engine for each voice type; the Harmony prefix must swap them for the WAVs
                    int vi = (int)((life - 4f) / 1.2f); if (life > 4f && vi != lastVoice && vi < 12) { lastVoice = vi; var vt = new[] { SkinVoiceManager.VoiceType.Grunt, SkinVoiceManager.VoiceType.Jump, SkinVoiceManager.VoiceType.Yell, SkinVoiceManager.VoiceType.Pain, SkinVoiceManager.VoiceType.Death, SkinVoiceManager.VoiceType.Stun, SkinVoiceManager.VoiceType.Fear, SkinVoiceManager.VoiceType.Climb, SkinVoiceManager.VoiceType.Focus, SkinVoiceManager.VoiceType.Debacle, SkinVoiceManager.VoiceType.Victory, SkinVoiceManager.VoiceType.Drown }; try { m.MakeVoice(vt[vi], SkinVoiceManager.CombatVoiceNetworkPredictionType.NoPrediction); } catch (Exception ve) { SimSubModule.L("voice test err " + ve.Message); } }
                }
                if (m != null && m.IsActive()) { VoiceFromActions(m); SilenceVanilla(m); GripMeasure(m); }
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
