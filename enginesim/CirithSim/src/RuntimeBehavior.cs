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
        float life;
        public override void OnMissionTick(float dt)
        {
            life += dt; if (SimSubModule.Harness && Environment.GetEnvironmentVariable("CIRITH_RUNTIME") == "1" && life > 20f) { SimSubModule.L("runtime test: done (jiggle agents tracked: " + jig.Count + ", main=" + (Agent.Main != null) + ")"); TaleWorlds.Engine.Utilities.ExitProcess(0); }
            try
            {
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
