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
    // Weapon-specific animation controller for her own sword (item ids cirith_sword / cirith_sword_l).
    // It only runs while the agent is ACTIVELY WIELDING one of these two items; everything is released the moment the weapon is sheathed, dropped, swapped or the agent dies.
    // Her KUF clips (retargeted by transplant/retarget_anim.py) are played by overriding the bone frames every tick (the engine does not merge module action XML, see HANDOFF.md).
    //   idle / walk / run : forward locomotion only (backwards and strafing keep the engine's animations), never while the engine plays a combat action (attack, block, kick ...).
    //   sprint            : hold the sprint key (default V) while moving forward -> "charge" clip, x1.5 speed (full outfit), and the sword is moved to the OTHER hand
    //                       (item cirith_sword_l carries ForceAttachOffHandPrimaryItemBone, so the engine itself re-attaches the weapon to the left-hand bone).
    //   charge attack     : attack input during a sprint -> "chargeattack" clip with its root motion, engine attack started at the same moment (its hit window is the engine's).
    public class WeaponAnim
    {
        public const string ItemMain = "cirith_sword", ItemLeft = "cirith_sword_l";
        public static InputKey SprintKey = InputKey.V;
        const float SpeedMult = 1.5f;

        class Clip
        {
            public string Name; public int N, NB; public float RootSpeed, NatSpeed; public float[] Dz, Fw, Lat; public float[][] R;
            public static Clip Load(string name)
            {
                var f = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_anim_" + name + ".txt");
                var L = File.ReadAllLines(f); var h = L[0].Split(' ');
                var c = new Clip { Name = name, N = int.Parse(h[1]), NB = int.Parse(h[2]), RootSpeed = P(h[3]), NatSpeed = P(h[4]) };
                c.Dz = new float[c.N]; c.Fw = new float[c.N]; c.Lat = new float[c.N]; c.R = new float[c.N][];
                for (int i = 0; i < c.N; i++) { var p = L[i + 1].Split(' '); c.Dz[i] = P(p[0]); c.Fw[i] = P(p[1]); c.Lat[i] = P(p[2]); c.R[i] = new float[c.NB * 9]; for (int k = 0; k < c.NB * 9; k++) c.R[i][k] = P(p[3 + k]); }
                return c;
            }
        }
        static float P(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        public enum State { None, Idle, Walk, Run, Sprint, Attack }
        public State Current = State.None; public static string Log = "";
        Clip idle, walk, run, charge, attack; bool loaded, tried;
        float phase; Clip clip; float weight; float xfade = 1f; float[] prevPose; bool havePrev;
        Vec3 lastPos; bool havePos; Vec3 vel; float sprintTime, attackT; bool attackActive; Vec3 attackStart; float cooldown;
        int slotMain = -1, slotLeft = -1; bool leftHand; float baseMax = -1, baseCombat = -1; bool boosted;
        public bool SprintHeldOverride, AttackPressedOverride; public bool UseOverrides;   // used by the headless test

        public static string WieldedId(Agent a, out bool offHand)
        {
            offHand = false;
            try
            {
                var pi = a.GetPrimaryWieldedItemIndex(); if (pi != EquipmentIndex.None) { var id = a.Equipment[pi].Item?.StringId; if (id == ItemMain || id == ItemLeft) return id; }
            }
            catch { }
            return null;
        }
        static bool WearsOutfit(Agent a)
        {
            try { var eq = a.SpawnEquipment; string id(EquipmentIndex i) => eq[i].Item?.StringId ?? ""; return id(EquipmentIndex.Head) == "cirith_head" && id(EquipmentIndex.Body) == "cirith_body" && id(EquipmentIndex.Leg) == "cirith_legs" && id(EquipmentIndex.Gloves) == "cirith_hands"; } catch { return false; }
        }
        void Load()
        {
            tried = true;
            try
            {
                idle = Clip.Load("idle"); walk = Clip.Load("walk"); run = Clip.Load("run"); charge = Clip.Load("charge"); attack = Clip.Load("chargeattack"); loaded = true;
                SimSubModule.L($"weapon anim: clips loaded (idle {idle.N}, walk {walk.N}, run {run.N}, charge {charge.N}, chargeattack {attack.N} frames @60 fps); natural speeds walk {walk.NatSpeed:0.00} run {run.NatSpeed:0.00} charge {charge.NatSpeed:0.00} m/s; attack root travel {attack.Fw[attack.N - 1]:0.00} m");
                var k = Environment.GetEnvironmentVariable("CIRITH_SPRINTKEY"); if (!string.IsNullOrEmpty(k) && Enum.TryParse(k, true, out InputKey kk)) SprintKey = kk;
            }
            catch (Exception e) { SimSubModule.L("weapon anim load failed: " + e.Message); }
        }

        // ---- equipment slots of the two sword items (the left-hand twin lives in another weapon slot so the engine's own attach logic does the hand change)
        void FindSlots(Agent a)
        {
            slotMain = slotLeft = -1;
            for (int i = 0; i < 5; i++) { try { var id = a.Equipment[(EquipmentIndex)i].Item?.StringId; if (id == ItemMain) slotMain = i; else if (id == ItemLeft) slotLeft = i; } catch { } }
        }
        void SetHand(Agent a, bool left)
        {
            if (left == leftHand) return; FindSlots(a); int slot = left ? slotLeft : slotMain; if (slot < 0) return;
            try { a.TryToWieldWeaponInSlot((EquipmentIndex)slot, Agent.WeaponWieldActionType.Instant, false); leftHand = left; SimSubModule.L("weapon hand -> " + (left ? "LEFT (off-hand bone)" : "RIGHT (combat hand)") + " slot " + slot); } catch (Exception e) { SimSubModule.L("hand switch err " + e.Message); }
        }

        void Boost(Agent a, bool on)
        {
            try
            {
                if (on && !boosted) { baseMax = a.GetAgentDrivenPropertyValue(DrivenProperty.MaxSpeedMultiplier); baseCombat = a.GetAgentDrivenPropertyValue(DrivenProperty.CombatMaxSpeedMultiplier); boosted = true; a.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MaxSpeedMultiplier, baseMax * SpeedMult); a.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.CombatMaxSpeedMultiplier, baseCombat * SpeedMult); a.UpdateCustomDrivenProperties(); }
                else if (on) { a.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MaxSpeedMultiplier, baseMax * SpeedMult); a.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.CombatMaxSpeedMultiplier, baseCombat * SpeedMult); }
                else if (boosted) { boosted = false; a.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.MaxSpeedMultiplier, baseMax); a.SetAgentDrivenPropertyValueFromConsole(DrivenProperty.CombatMaxSpeedMultiplier, baseCombat); a.UpdateCustomDrivenProperties(); }
            }
            catch (Exception e) { SimSubModule.L("boost err " + e.Message); }
        }

        // release everything (weapon not wielded, agent dead, mission ending)
        public void Release(Agent a, string why)
        {
            if (Current == State.None && !boosted && !leftHand) return;
            Boost(a, false); if (leftHand) SetHand(a, false); Current = State.None; clip = null; weight = 0; attackActive = false; havePrev = false; lastPoseValid = false; sprintTime = 0;
            SimSubModule.L("weapon anim released: " + why);
        }

        static bool EngineBusy(Agent a)
        {
            try
            {
                var t = a.GetCurrentActionType(1);
                switch (t)
                {
                    case Agent.ActionCodeType.Other: case Agent.ActionCodeType.Idle: case Agent.ActionCodeType.Guard: return false;
                    default: return true;
                }
            }
            catch { return false; }
        }

        public void Update(Agent me, float dt)
        {
            if (!tried) Load(); if (!loaded) return;
            if (me == null || !me.IsActive() || me.AgentVisuals == null) { return; }
            string wid = WieldedId(me, out _);
            if (wid == null) { if (Current != State.None || boosted || leftHand) Release(me, "weapon no longer wielded"); havePos = false; return; }
            if (!me.IsActive()) { Release(me, "dead"); return; }
            // ---- velocity (horizontal, smoothed)
            var pos = me.Position; if (!havePos) { lastPos = pos; havePos = true; vel = Vec3.Zero; }
            var raw = (pos - lastPos) / Math.Max(dt, 0.001f); raw.z = 0; if (dt > 0.1f) raw = Vec3.Zero; lastPos = pos; float k = 1f - (float)Math.Exp(-dt / 0.08f); vel = Vec3.Lerp(vel, raw, k);
            var look = me.LookDirection; look.z = 0; if (look.LengthSquared > 1e-6f) look.Normalize();
            float speed = vel.Length, fwd = Vec3.DotProduct(vel, look);
            bool isMain = me == Agent.Main;
            bool sprintHeld = UseOverrides ? SprintHeldOverride : (isMain && SafeKey(SprintKey));
            bool attackPressed = UseOverrides ? AttackPressedOverride : (isMain && SafeMouseAttack());
            if (cooldown > 0) cooldown -= dt;
            bool forward = fwd > 0.6f * Math.Max(speed, 0.001f) && speed > 0.2f;
            bool busy = EngineBusy(me) && !attackActive;

            // ---- state machine
            State want;
            if (attackActive) want = State.Attack;
            else if (busy) want = State.None;
            else if (sprintHeld && forward && speed > 1.2f) want = State.Sprint;
            else if (speed < 0.25f) want = State.Idle;
            else if (forward) want = speed < 2.4f ? State.Walk : State.Run;
            else want = State.None;
            if (want == State.Sprint && attackPressed && cooldown <= 0 && !attackActive) { attackActive = true; attackT = 0; attackStart = pos; want = State.Attack; TriggerEngineAttack(me); }
            if (want != Current) { Enter(me, want); }
            // ---- advance the clip
            if (clip != null && want != State.None)
            {
                float rate = 1f;
                if (want == State.Walk || want == State.Run || want == State.Sprint) rate = Math.Max(0.5f, Math.Min(1.7f, speed / Math.Max(clip.NatSpeed, 0.1f)));
                if (want == State.Attack) { attackT += dt; rate = 1f; }
                phase += dt * 60f * rate;
                if (want == State.Attack)
                {
                    int fi = Math.Min((int)(attackT * 60f), clip.N - 1);
                    // root motion of the lunge: move the agent along its facing by the clip's forward travel (no double movement: the engine only moves her for locomotion)
                    var target = attackStart + look * clip.Fw[fi]; target.z = me.Position.z; try { me.TeleportToPosition(target); } catch { }
                    if (attackT >= clip.N / 60f) { attackActive = false; cooldown = 0.4f; Current = State.None; Enter(me, State.None); want = State.None; }
                }
            }
            float wTarget = want == State.None ? 0f : 1f; float tc = wTarget > weight ? 0.12f : 0.20f; weight += (wTarget - weight) * (1f - (float)Math.Exp(-dt / tc)); if (weight < 0.002f && wTarget == 0f) weight = 0f;
            if (want == State.Sprint) { sprintTime += dt; Boost(me, WearsOutfit(me)); } else { sprintTime = 0; if (boosted) Boost(me, false); }
            if (clip == null || weight <= 0f) { if (Current == State.None && !boosted && !attackActive) { if (leftHand && want != State.Sprint && want != State.Attack) SetHand(me, false); } return; }
            Apply(me, dt);
        }
        static bool SafeKey(InputKey k) { try { return Input.IsKeyDown(k); } catch { return false; } }
        static bool SafeMouseAttack() { try { return Input.IsKeyPressed(InputKey.LeftMouseButton); } catch { return false; } }

        void TriggerEngineAttack(Agent a)
        {
            // the engine's own attack (its damage window / collision / hit reaction) is started together with our clip; the player's mouse press already does this in a real game.
            try { if (a != Agent.Main) a.MovementFlags |= Agent.MovementControlFlag.AttackUp; } catch { }
        }

        void Enter(Agent me, State s)
        {
            // remember the pose we leave so the new clip fades in from it (no arm snapping)
            Current = s; phase = 0;
            switch (s) { case State.Idle: clip = idle; break; case State.Walk: clip = walk; break; case State.Run: clip = run; break; case State.Sprint: clip = charge; break; case State.Attack: clip = attack; break; default: clip = null; break; }
            xfade = 0f; if (lastPoseValid) { prevPose = (float[])lastPose.Clone(); havePrev = true; } else havePrev = false;
            // weapon hand: sprinting and the charge attack hold the sword in the other (left) hand, every other state uses the combat hand
            SetHand(me, s == State.Sprint || s == State.Attack);
            SimSubModule.L("weapon anim state -> " + s);
        }

        float[] lastPose = new float[28 * 9]; bool lastPoseValid;
        void Apply(Agent me, float dt)
        {
            var sk = me.AgentVisuals.GetSkeleton(); int n = sk.GetBoneCount(); if (n != clip.NB) return;
            int fr = ((int)phase) % clip.N; if (fr < 0) fr += clip.N; if (Current == State.Attack) fr = Math.Min((int)phase, clip.N - 1);
            xfade = Math.Min(1f, xfade + dt / 0.18f); float xf = xfade * xfade * (3 - 2 * xfade);
            var ent = new MatrixFrame[n]; for (int i = 0; i < n; i++) ent[i] = sk.GetBoneEntitialFrame((sbyte)i);
            var loc = new MatrixFrame[n]; var r = clip.R[fr];
            for (int i = 0; i < n; i++)
            {
                sbyte par = sk.GetParentBoneIndex((sbyte)i); var cur = par >= 0 ? ent[par].TransformToLocal(ent[i]) : ent[i]; int o = i * 9;
                // pose of the clip, cross-faded from the previous clip's last pose
                Vec3 s = new Vec3(r[o + 0], r[o + 3], r[o + 6]), f = new Vec3(r[o + 1], r[o + 4], r[o + 7]), u = new Vec3(r[o + 2], r[o + 5], r[o + 8]);
                if (havePrev && xf < 1f) { var q = prevPose; s = Vec3.Lerp(new Vec3(q[o + 0], q[o + 3], q[o + 6]), s, xf); f = Vec3.Lerp(new Vec3(q[o + 1], q[o + 4], q[o + 7]), f, xf); u = Vec3.Lerp(new Vec3(q[o + 2], q[o + 5], q[o + 8]), u, xf); }
                // blend with whatever the engine is playing (weight = state fade in/out)
                var m = new Mat3(cur.rotation.s * (1 - weight) + s * weight, cur.rotation.f * (1 - weight) + f * weight, cur.rotation.u * (1 - weight) + u * weight); m.Orthonormalize();
                lastPose[o + 0] = m.s.x; lastPose[o + 3] = m.s.y; lastPose[o + 6] = m.s.z; lastPose[o + 1] = m.f.x; lastPose[o + 4] = m.f.y; lastPose[o + 7] = m.f.z; lastPose[o + 2] = m.u.x; lastPose[o + 5] = m.u.y; lastPose[o + 8] = m.u.z;
                var org = cur.origin; if (i == 0) org.z += clip.Dz[fr] * weight;
                loc[i] = new MatrixFrame(m, org);
            }
            lastPoseValid = true;
            for (int i = 0; i < n; i++) sk.SetBoneLocalFrame((sbyte)i, loc[i]);
            sk.UpdateEntitialFramesFromLocalFrames();
        }
    }
}
