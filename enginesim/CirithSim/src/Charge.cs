using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Path = System.IO.Path;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CirithSim
{
    // "Cirith Charge": her KUF animation 4 (CHARGE / STEP_FORWARD, baked onto the 28-bone skeleton by transplant/retarget_anim.py -> ModuleData/cirith_charge.txt) is played by overriding
    // the bone frames every tick (same hook as the jiggle rig), and the wearer runs 1.5x faster while it plays. Only for an agent that wears ALL FOUR Cirith pieces; only the player
    // (Agent.Main) reacts to the key - the headless test can trigger it with CIRITH_CHARGETEST.
    public class Charge
    {
        public const float SpeedMult = 1.5f, Duration = 5f, Cooldown = 8f, BlendIn = 0.15f, BlendOut = 0.25f;
        public static InputKey Key = InputKey.V;
        float[][] rots; float[] dz; int nfr, nb; bool loaded, tried;
        public bool Active; float t, cool; float baseMax = -1, baseCombat = -1; public static string Status = "";
        Skeleton sk; bool pushed;

        public static bool WearsOutfit(Agent a)
        {
            try
            {
                var eq = a.SpawnEquipment; string id(EquipmentIndex i) => eq[i].Item?.StringId ?? "";
                return id(EquipmentIndex.Head) == "cirith_head" && id(EquipmentIndex.Body) == "cirith_body" && id(EquipmentIndex.Leg) == "cirith_legs" && id(EquipmentIndex.Gloves) == "cirith_hands";
            }
            catch { return false; }
        }
        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }
        void Load()
        {
            tried = true;
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData");
                var lines = File.ReadAllLines(Path.Combine(dir, "cirith_charge.txt")); var h = lines[0].Split(' '); nfr = int.Parse(h[1]); nb = int.Parse(h[2]);
                rots = new float[nfr][]; dz = new float[nfr];
                for (int i = 0; i < nfr; i++) { var p = lines[i + 1].Split(' '); dz[i] = F(p[0]); rots[i] = new float[nb * 9]; for (int k = 0; k < nb * 9; k++) rots[i][k] = F(p[1 + k]); }
                loaded = true; SimSubModule.L($"charge: animation loaded, {nfr} frames x {nb} bones");
                var k2 = Environment.GetEnvironmentVariable("CIRITH_CHARGEKEY"); if (!string.IsNullOrEmpty(k2) && Enum.TryParse(k2, true, out InputKey kk)) Key = kk;
            }
            catch (Exception e) { SimSubModule.L("charge load failed: " + e.Message); }
        }
        static Mat3 Orth(Mat3 m) { m.Orthonormalize(); return m; }

        public void Start(Agent me)
        {
            if (Active || cool > 0 || !WearsOutfit(me)) return;
            Active = true; pushed = false; t = 0; baseMax = me.GetAgentDrivenPropertyValue(DrivenProperty.MaxSpeedMultiplier); baseCombat = me.GetAgentDrivenPropertyValue(DrivenProperty.CombatMaxSpeedMultiplier);
            SimSubModule.L($"charge START (base max speed mult {baseMax:0.00}, combat {baseCombat:0.00}) -> x{SpeedMult}");
        }
        void Stop(Agent me)
        {
            Active = false; cool = Cooldown;
            try { me.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MaxSpeedMultiplier, baseMax); me.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.CombatMaxSpeedMultiplier, baseCombat); } catch { }
            SimSubModule.L("charge END (speed restored)");
        }

        // returns true while the animation override is applied
        public bool Update(Agent me, float dt, bool testTrigger)
        {
            if (!tried) Load(); if (!loaded) return false;
            if (cool > 0) cool -= dt;
            if (!Active)
            {
                bool pressed = false; try { pressed = me == Agent.Main && Input.IsKeyPressed(Key); } catch { }
                if (pressed || testTrigger) Start(me);
                if (!Active) return false;
            }
            t += dt; if (t >= Duration) { Stop(me); return false; }
            try
            {   // speed boost (re-applied every tick: the engine resets driven properties whenever it recomputes agent stats)
                me.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MaxSpeedMultiplier, baseMax * SpeedMult); me.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.CombatMaxSpeedMultiplier, baseCombat * SpeedMult); if (!pushed) { pushed = true; me.UpdateCustomDrivenProperties(); }
            }
            catch (Exception e) { SimSubModule.L("charge speed err " + e.Message); }
            sk = me.AgentVisuals.GetSkeleton(); int n = sk.GetBoneCount(); if (n != nb) return false;
            float w = Math.Min(1f, Math.Min(t / BlendIn, (Duration - t) / BlendOut)); w = Math.Max(0f, w); w = w * w * (3 - 2 * w);
            int fr = (int)(t * 60f * 1.15f) % nfr;                                  // clip plays 15% faster to match the faster run
            var ent = new MatrixFrame[n]; for (int i = 0; i < n; i++) ent[i] = sk.GetBoneEntitialFrame((sbyte)i);
            var loc = new MatrixFrame[n];
            for (int i = 0; i < n; i++)
            {
                sbyte par = sk.GetParentBoneIndex((sbyte)i); var cur = par >= 0 ? ent[par].TransformToLocal(ent[i]) : ent[i];
                var r = rots[fr]; int o = i * 9;
                var s = new Vec3(r[o + 0], r[o + 3], r[o + 6]); var f = new Vec3(r[o + 1], r[o + 4], r[o + 7]); var u = new Vec3(r[o + 2], r[o + 5], r[o + 8]);   // columns of the 3x3
                var target = new Mat3(s, f, u);
                var blended = new Mat3(cur.rotation.s * (1 - w) + s * w, cur.rotation.f * (1 - w) + f * w, cur.rotation.u * (1 - w) + u * w); blended = Orth(blended);
                var org = cur.origin; if (i == 0) org.z += dz[fr] * w;
                loc[i] = new MatrixFrame(blended, org);
            }
            for (int i = 0; i < n; i++) sk.SetBoneLocalFrame((sbyte)i, loc[i]);
            sk.UpdateEntitialFramesFromLocalFrames();
            return true;
        }
    }
}
