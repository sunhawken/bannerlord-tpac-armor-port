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
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        readonly Dictionary<Agent, Jiggle> jig = new Dictionary<Agent, Jiggle>(); readonly Charge charge = new Charge(); bool err; float clean;

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
                var main = Agent.Main;
                if (main != null && main.IsActive() && Charge.WearsOutfit(main)) charge.Update(main, dt, false);
                foreach (var a in Mission.Current.Agents)
                {
                    if (a == null || !a.IsActive() || a.AgentVisuals == null || !WearsAny(a)) continue;
                    if (!jig.TryGetValue(a, out var j)) { j = new Jiggle(); jig[a] = j; SimSubModule.L("runtime: jiggle rig attached to " + (a.Name ?? "agent")); }
                    j.Update(a, dt);
                }
                clean += dt; if (clean > 5f) { clean = 0; foreach (var k in jig.Keys.Where(k => k == null || !k.IsActive()).ToList()) jig.Remove(k); }
            }
            catch (Exception e) { if (!err) { err = true; SimSubModule.L("runtime error " + e); } }
        }
    }
}
