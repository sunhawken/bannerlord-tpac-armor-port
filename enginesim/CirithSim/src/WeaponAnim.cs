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
    // Weapon-specific animation controller for her own sword (item id cirith_sword; the engine holds it in the LEFT hand, exactly like her original files where it sits on her R hand = Bannerlord's left).
    // It only runs while the agent is ACTIVELY WIELDING that item and releases everything the moment it is sheathed, dropped, swapped or the agent dies.
    // Her KUF clips (retargeted by transplant/retarget_anim.py, sword arm = Bannerlord left arm) are played by overriding the bone frames every tick (the engine does not merge module action XML).
    //   idle / walk / run : forward locomotion only (backwards and strafing keep the engine's animations), never while the engine plays a combat action (attack, block, kick ...).
    //   sprint            : hold the sprint key (default V) while moving forward -> her "charge / step forward" clip; x1.5 speed when the full outfit is worn.
    public class WeaponAnim
    {
        public const string Item = "cirith_sword", ItemVr = "cirith_sword_vr";
        public static string ItemId => CirithVR.Active ? ItemVr : Item;
        public static InputKey SprintKey = InputKey.C;
        const float SpeedMult = 1.5f;

        class Clip
        {
            public string Name; public int N, NB; public float NatSpeed; public float[] Dz; public float[][] R;
            public static Clip Load(string name)
            {
                var f = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_anim_" + name + ".txt");
                var L = File.ReadAllLines(f); var h = L[0].Split(' ');
                var c = new Clip { Name = name, N = int.Parse(h[1]), NB = int.Parse(h[2]), NatSpeed = P(h[4]) };
                c.Dz = new float[c.N]; c.R = new float[c.N][];
                for (int i = 0; i < c.N; i++) { var p = L[i + 1].Split(' '); c.Dz[i] = P(p[0]); c.R[i] = new float[c.NB * 9]; for (int k = 0; k < c.NB * 9; k++) c.R[i][k] = P(p[3 + k]); }
                return c;
            }
        }
        static float P(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }

        public enum State { None, Idle, Walk, Run, Sprint }
        public State Current = State.None;
        Clip idle, walk, run, charge, clip; bool loaded, tried;
        float phase, weight, xfade = 1f; float[] prevPose; bool havePrev; float[] lastPose = new float[28 * 9]; bool lastPoseValid;
        Vec3 lastPos, vel; bool havePos; bool boosted; float baseMax = -1, baseCombat = -1;
        public bool SprintHeldOverride, UseOverrides;   // used by the headless test only
        int calibLog = 0, calibTick, dbgN;
        // ---- our own sword mesh, attached to the left-hand BONE of the skeleton with the exact grip frame computed from her original files (the engine's own weapon attachment ignores bone overrides, so it is hidden while we drive the arm)
        MatrixFrame grip = MatrixFrame.Identity, gripR = MatrixFrame.Identity; bool gripLoaded; bool engineWeaponHidden;

        public static bool IsWielding(Agent a)
        {
            try { var pi = a.GetPrimaryWieldedItemIndex(); return pi != EquipmentIndex.None && (a.Equipment[pi].Item?.StringId == Item || a.Equipment[pi].Item?.StringId == ItemVr); } catch { return false; }
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
                bool mir = Environment.GetEnvironmentVariable("CIRITH_LEFT") != "1"; Mirrored = mir; string sfx = mir ? "_m" : ""; idle = Clip.Load("idle" + sfx); walk = Clip.Load("walk" + sfx); run = Clip.Load("run" + sfx); charge = Clip.Load("charge" + sfx); loaded = true;
                SimSubModule.L($"weapon anim: clips loaded (idle {idle.N}, walk {walk.N}, run {run.N}, sprint {charge.N} frames @60 fps); natural speeds walk {walk.NatSpeed:0.00} run {run.NatSpeed:0.00} sprint {charge.NatSpeed:0.00} m/s");
                try { var g = File.ReadAllText(Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_grip.txt")).Split(new[] { ' ', (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries).Select(P).ToArray();
                      grip = new MatrixFrame(new Mat3(new Vec3(g[0], g[3], g[6]), new Vec3(g[1], g[4], g[7]), new Vec3(g[2], g[5], g[8])), Vec3.Zero); gripLoaded = true; LoadHull(); try { var fr = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_grip_r.txt"); if (File.Exists(fr)) { var q = File.ReadAllText(fr).Split(new[] { ' ', (char)10, (char)13 }, StringSplitOptions.RemoveEmptyEntries).Select(P).ToArray(); gripR = new MatrixFrame(new Mat3(new Vec3(q[0], q[3], q[6]), new Vec3(q[1], q[4], q[7]), new Vec3(q[2], q[5], q[8])), Vec3.Zero); } } catch { } LoadEdits(); SimSubModule.L($"weapon grip frame loaded: blade in l_hand frame = {g[2]:0.000},{g[5]:0.000},{g[8]:0.000}"); } catch (Exception ge) { SimSubModule.L("grip load failed: " + ge.Message); }
                var k = Environment.GetEnvironmentVariable("CIRITH_SPRINTKEY"); if (!string.IsNullOrEmpty(k) && Enum.TryParse(k, true, out InputKey kk)) SprintKey = kk;
            }
            catch (Exception e) { SimSubModule.L("weapon anim load failed: " + e.Message); }
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

        // release everything (weapon not wielded, agent dead, mission ending): the engine's own animations take over again immediately
        public void Release(Agent a, string why)
        {
            if (Current == State.None && !boosted) return;
            Boost(a, false); Current = State.None; clip = null; weight = 0; havePrev = false; lastPoseValid = false;
            SimSubModule.L("weapon anim released: " + why);
        }

        public static bool Mirrored = true; readonly Dictionary<string, MatrixFrame> editsR = new Dictionary<string, MatrixFrame>();
        // ---- real-geometry hitbox: sample points of her actual sword mesh (ModuleData/cirith_sword_hull.txt), swept between the last two ticks
        public static WeaponAnim MainInst; Vec3[] hull; MatrixFrame curW, prevW; bool haveW;
        void LoadHull()
        {
            try
            {
                var f = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_sword_hull.txt"); if (!File.Exists(f)) return;
                var l = File.ReadAllLines(f).Select(x => x.Split(' ')).Where(q => q.Length == 3).Select(q => new Vec3(P(q[0]), P(q[1]), P(q[2]))).ToList();
                float tip = l.Max(v => v.z); for (float z = tip + 0.04f; z <= tip + 0.13f; z += 0.04f) l.Add(new Vec3(0, 0, z));      // a little LONGER than the real blade
                hull = l.ToArray(); SimSubModule.L("hitbox hull loaded: " + hull.Length + " points (blade tip z=" + tip.ToString("0.00") + " + extension)");
            }
            catch (Exception e) { SimSubModule.L("hull load failed: " + e.Message); }
        }
        // distance (m) from a world point to the real sword geometry, including the sweep since the previous tick
        public float MinDistance(Vec3 p)
        {
            if (hull == null || !haveW) return float.MaxValue; float best = float.MaxValue;
            foreach (var h in hull)
            {
                var a = curW.origin + curW.rotation.s * h.x + curW.rotation.f * h.y + curW.rotation.u * h.z; var b = prevW.origin + prevW.rotation.s * h.x + prevW.rotation.f * h.y + prevW.rotation.u * h.z;
                var ab = a - b; float L2 = ab.LengthSquared; float t = L2 > 1e-8f ? Math.Max(0f, Math.Min(1f, Vec3.DotProduct(p - b, ab) / L2)) : 0f; var q = b + ab * t; float d = (p - q).Length; if (d < best) best = d;
            }
            return best;
        }
        // VR: no clips, no proxy sword, no bone overrides - only the sprint state (key C) for the x1.5 boost
        void VrTick(Agent me, float dt)
        {
            ShowProxy(me, false);
            var pos = me.Position; if (!havePos) { lastPos = pos; havePos = true; vel = Vec3.Zero; }
            var raw = (pos - lastPos) / Math.Max(dt, 0.001f); raw.z = 0; if (dt > 0.1f || raw.Length > 12f) raw = Vec3.Zero; lastPos = pos; float k = 1f - (float)Math.Exp(-dt / 0.08f); vel = Vec3.Lerp(vel, raw, k);
            var look = me.LookDirection; look.z = 0; if (look.LengthSquared > 1e-6f) look.Normalize();
            float speed = vel.Length, fwd = Vec3.DotProduct(vel, look);
            bool sprintHeld = me == Agent.Main && SafeKey(SprintKey); bool forward = fwd > 0.6f * Math.Max(speed, 0.001f) && speed > 0.2f;
            bool want = sprintHeld && forward && speed > 1.2f;
            if (want) Boost(me, WearsOutfit(me)); else if (boosted) Boost(me, false);
            Current = want ? State.Sprint : State.None;
        }
        bool engineHold;
        // jump / fall: the engine's own animation must win while she is in the air (action type on either channel, or a vertical speed the ground never produces)
        static bool JumpLike(Agent a, float vz)
        {
            try { foreach (int ch in new[] { 0, 1 }) { var n = a.GetCurrentActionType(ch).ToString(); if (n.IndexOf("Jump", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Fall", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Land", StringComparison.OrdinalIgnoreCase) >= 0) return true; } } catch { }
            return Math.Abs(vz) > 1.2f;
        }
        float lastZ, airUntil; bool haveZ;
        bool EngineBusy(Agent a, float dt)
        {
            float z = a.Position.z; float vz = haveZ && dt > 0.0001f && dt < 0.1f ? (z - lastZ) / dt : 0f; lastZ = z; haveZ = true;
            if (JumpLike(a, vz)) airUntil = life + 0.35f;      // keep yielding a moment through the landing
            life += dt; if (life < airUntil) return true;
            return EngineBusy(a);
        }
        float life;
        static bool EngineBusy(Agent a)
        {
            try { switch (a.GetCurrentActionType(1)) { case Agent.ActionCodeType.Other: case Agent.ActionCodeType.Idle: case Agent.ActionCodeType.Guard: return false; default: return true; } }
            catch { return false; }
        }

        public void Update(Agent me, float dt)
        {
            if (!tried) Load(); if (!loaded) return;
            if (me == null) return;
            if (!me.IsActive()) { Release(me, "dead"); Dispose(); return; }
            if (me.AgentVisuals == null) return;
            if (!IsWielding(me)) { if (Current != State.None || boosted) Release(me, "weapon no longer wielded"); ShowProxy(me, false); havePos = false; return; }
            if (CirithVR.Active) { VrTick(me, dt); return; }
            ShowProxy(me, true);
            // ---- velocity (horizontal, smoothed)
            var pos = me.Position; if (!havePos) { lastPos = pos; havePos = true; vel = Vec3.Zero; }
            var raw = (pos - lastPos) / Math.Max(dt, 0.001f); raw.z = 0; if (dt > 0.1f) raw = Vec3.Zero; lastPos = pos; float k = 1f - (float)Math.Exp(-dt / 0.08f); vel = Vec3.Lerp(vel, raw, k);
            var look = me.LookDirection; look.z = 0; if (look.LengthSquared > 1e-6f) look.Normalize();
            float speed = vel.Length, fwd = Vec3.DotProduct(vel, look);
            bool sprintHeld = UseOverrides ? SprintHeldOverride : (me == Agent.Main && SafeKey(SprintKey));
            bool forward = fwd > 0.6f * Math.Max(speed, 0.001f) && speed > 0.2f;
            // ---- state machine (the engine's combat actions always win: no override while attacking / blocking)
            State want;
            engineHold = EngineBusy(me, dt) || Environment.GetEnvironmentVariable("CIRITH_FORCEHOLD") == "1";
            if (engineHold) want = State.None;
            else if (sprintHeld && forward && speed > 1.2f) want = State.Sprint;
            else if (speed < 0.25f) want = State.Idle;
            else if (forward) want = speed < 2.4f ? State.Walk : State.Run;
            else want = State.None;
            if (want != Current) Enter(want);
            // ---- advance the clip (playback speed follows the real speed so the feet do not slide)
            if (clip != null && want != State.None)
            {
                float rate = 1f; if (want != State.Idle) rate = Math.Max(0.5f, Math.Min(1.7f, speed / Math.Max(clip.NatSpeed, 0.1f)));
                phase += dt * 60f * rate;
            }
            float wTarget = want == State.None ? 0f : 1f; float tc = wTarget > weight ? 0.12f : 0.20f; weight += (wTarget - weight) * (1f - (float)Math.Exp(-dt / tc)); if (weight < 0.002f && wTarget == 0f) weight = 0f;
            if (want == State.Sprint) Boost(me, WearsOutfit(me)); else if (boosted) Boost(me, false);
            if (calibLog > 0) Calibrate(me);
            if (clip == null || weight <= 0f) { UpdateProxy(me); return; }
            Apply(me, dt);
            UpdateProxy(me);
        }
        static bool SafeKey(InputKey k) { try { return Input.IsKeyDown(k); } catch { return false; } }

        void Enter(State s)
        {
            Current = s; phase = 0;
            switch (s) { case State.Idle: clip = idle; break; case State.Walk: clip = walk; break; case State.Run: clip = run; break; case State.Sprint: clip = charge; break; default: clip = null; break; }
            xfade = 0f; if (lastPoseValid) { prevPose = (float[])lastPose.Clone(); havePrev = true; } else havePrev = false;   // fade in from the pose we leave: no arm snapping
            SimSubModule.L("weapon anim state -> " + s);
        }

        // grip calibration: the engine attaches the weapon's MetaMesh straight to a bone (Skeleton bone component); its Frame is the grip frame relative to that bone
        void Calibrate(Agent me)
        {
            if (calibTick++ % 40 != 0) return;
            try
            {
                var sk = me.AgentVisuals.GetSkeleton(); calibLog--;
                foreach (int bone in new[] { 19, 26, 20, 27 })
                {
                    int cnt = sk.GetBoneComponentCount((sbyte)bone);
                    for (int i = 0; i < cnt; i++)
                    {
                        var c = sk.GetBoneComponentAtIndex((sbyte)bone, i); var mm = c as MetaMesh; if (mm == null) { SimSubModule.L($"CALIB bone {bone} ({sk.GetBoneName((sbyte)bone)}) component {i}: {c?.GetType().Name}"); continue; }
                        var fr = mm.Frame; var bb = mm.GetBoundingBox();
                        SimSubModule.L($"CALIB bone {bone} ({sk.GetBoneName((sbyte)bone)}) metamesh '{mm.GetName()}' meshes={mm.MeshCount}: frame origin {fr.origin.x:0.000},{fr.origin.y:0.000},{fr.origin.z:0.000} ; X(guard) {fr.rotation.s.x:0.000},{fr.rotation.s.y:0.000},{fr.rotation.s.z:0.000} ; Y(thin) {fr.rotation.f.x:0.000},{fr.rotation.f.y:0.000},{fr.rotation.f.z:0.000} ; Z(blade) {fr.rotation.u.x:0.000},{fr.rotation.u.y:0.000},{fr.rotation.u.z:0.000} ; bbox {bb.min}..{bb.max}");
                    }
                }
                try { int sc = sk.GetComponentCount(GameEntity.ComponentType.MetaMesh); SimSubModule.L($"CALIB skeleton metamesh components: {sc}"); for (int i = 0; i < sc; i++) { var mm = sk.GetComponentAtIndex(GameEntity.ComponentType.MetaMesh, i) as MetaMesh; if (mm != null) { var fr = mm.Frame; SimSubModule.L($"CALIB skel metamesh {i} '{mm.GetName()}' frame origin {fr.origin.x:0.000},{fr.origin.y:0.000},{fr.origin.z:0.000} X {fr.rotation.s.x:0.000},{fr.rotation.s.y:0.000},{fr.rotation.s.z:0.000} Y {fr.rotation.f.x:0.000},{fr.rotation.f.y:0.000},{fr.rotation.f.z:0.000} Z {fr.rotation.u.x:0.000},{fr.rotation.u.y:0.000},{fr.rotation.u.z:0.000}"); } } } catch (Exception e1) { SimSubModule.L("calib skel err " + e1.Message); }
                try { var all = sk.GetAllMeshes().ToList(); SimSubModule.L($"CALIB skeleton meshes: {all.Count}: " + string.Join(", ", all.Select(m => m.Name).Take(12))); } catch (Exception e2) { SimSubModule.L("calib meshes err " + e2.Message); }
                try { var ent = me.AgentVisuals.GetEntity(); foreach (var ch in ent.GetChildren()) { var fr = ch.GetFrame(); var g = ch.GetGlobalFrame(); SimSubModule.L($"CALIB child '{ch.Name}': local frame origin {fr.origin.x:0.000},{fr.origin.y:0.000},{fr.origin.z:0.000} | global origin {g.origin.x:0.000},{g.origin.y:0.000},{g.origin.z:0.000}; bone attach={(ch.Parent != null)}"); } } catch (Exception e3) { SimSubModule.L("calib child err " + e3.Message); }
                try
                {
                    var we = me.GetWeaponEntityFromEquipmentSlot(me.GetPrimaryWieldedItemIndex()); var gf = me.AgentVisuals.GetEntity().GetGlobalFrame(); var gr = gf.rotation; gr.Orthonormalize(); var hf = sk.GetBoneEntitialFrame(19);
                    var wf = we.GetGlobalFrame(); Func<Vec3, Vec3> toModel = v => new Vec3(Vec3.DotProduct(v, gr.s), Vec3.DotProduct(v, gr.f), Vec3.DotProduct(v, gr.u));
                    Func<Vec3, Vec3> toHand = v => { v = toModel(v); v.Normalize(); return new Vec3(Vec3.DotProduct(v, hf.rotation.s), Vec3.DotProduct(v, hf.rotation.f), Vec3.DotProduct(v, hf.rotation.u)); };
                    var hw = gf.origin + gr.s * hf.origin.x + gr.f * hf.origin.y + gr.u * hf.origin.z; var off = toModel(wf.origin - hw); var offH = new Vec3(Vec3.DotProduct(off, hf.rotation.s), Vec3.DotProduct(off, hf.rotation.f), Vec3.DotProduct(off, hf.rotation.u));
                    var x = toHand(wf.rotation.s); var y = toHand(wf.rotation.f); var z = toHand(wf.rotation.u);
                    SimSubModule.L($"CALIB weapon entity (slot wielded) '{we.Name}': in l_hand frame  X(guard)={x.x:0.000},{x.y:0.000},{x.z:0.000}  Y(thin)={y.x:0.000},{y.y:0.000},{y.z:0.000}  Z(blade)={z.x:0.000},{z.y:0.000},{z.z:0.000}  origin offset from hand={offH.x:0.000},{offH.y:0.000},{offH.z:0.000} (|{off.Length:0.000}| m) ; model-space blade {toModel(wf.rotation.u).x:0.00},{toModel(wf.rotation.u).y:0.00},{toModel(wf.rotation.u).z:0.00}");
                }
                catch (Exception e4) { SimSubModule.L("calib weapon entity err " + e4.Message); }
                if (calibLog < 38) calibLog = 0;
            }
            catch (Exception e) { SimSubModule.L("calib err " + e.Message); }
        }

        // our own sword entity: placed every tick at  agentFrame * handBoneFrame * grip * userEdit  (the engine's own weapon attachment ignores bone overrides, so it is hidden while we drive the arm)
        GameEntity swordEnt; Dictionary<string, float[]> edits = new Dictionary<string, float[]>();
        readonly Dictionary<string, float[]> editsAtk = new Dictionary<string, float[]>();
        // the animation clip the engine is playing now ("ready_thrust_1h", "release_slashright_1h", ...) -> the key of the editor's attack tabs
        string AtkKey(Agent me)
        {
            try
            {
                var ac = me.GetCurrentAction(1); var nm = me.ActionSet.GetAnimationName(ref ac); if (string.IsNullOrEmpty(nm)) return null;
                nm = nm.Replace("quick_", "").Replace("_balanced", "").Replace("_unbalanced", "").Replace("_left_stance", "").Replace("_lstance", "");
                nm = System.Text.RegularExpressions.Regex.Replace(nm, "^(blocked|stuck)_", "release_"); nm = System.Text.RegularExpressions.Regex.Replace(nm, "^ready_from_[a-z]+_", "ready_"); nm = nm.Replace("ready_continue_", "ready_");
                lastAtkName = nm; return nm;
            }
            catch { return null; }
        }
        string lastAtkName = "", lastLoggedAtk = null;
        MatrixFrame AtkGrip(Agent me)
        {
            var k = AtkKey(me); if (k != lastLoggedAtk) { lastLoggedAtk = k; SimSubModule.L("attack clip: " + k + (k != null && editsAtk.ContainsKey(k) ? " (edited grip)" : " (base grip)")); }
            if (k != null && editsAtk.TryGetValue(k, out var e))
            {
                var re = Rxyz(e[3], e[4], e[5]); var rot = new Mat3(); var m = gripR.rotation; Vec3 Mul(Mat3 A, Vec3 v) => A.s * v.x + A.f * v.y + A.u * v.z;
                rot.s = Mul(m, re.s); rot.f = Mul(m, re.f); rot.u = Mul(m, re.u); return new MatrixFrame(rot, new Vec3(e[0], e[1], e[2]));
            }
            return gripR;
        }
        void LoadEdits()
        {
            try
            {
                var fa = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_grip_edits_atk.txt");
                if (File.Exists(fa)) { foreach (var l in File.ReadAllLines(fa)) { var q = l.Split(' ').Where(x => x.Length > 0).ToArray(); if (q.Length == 7) editsAtk[q[0]] = q.Skip(1).Select(P).ToArray(); } SimSubModule.L("attack grips loaded for: " + string.Join(", ", editsAtk.Keys)); }
            }
            catch (Exception e) { SimSubModule.L("attack grips load failed: " + e.Message); }
            try
            {
                var fr = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_grip_edits_r.txt");
                if (File.Exists(fr)) foreach (var l in File.ReadAllLines(fr)) { var n0 = l.Split(' ')[0]; var v = l.Split(' ').Skip(1).Where(x => x.Length > 0).Select(P).ToArray(); if (v.Length == 12) editsR[n0] = new MatrixFrame(new Mat3(new Vec3(v[3], v[6], v[9]), new Vec3(v[4], v[7], v[10]), new Vec3(v[5], v[8], v[11])), new Vec3(v[0], v[1], v[2])); }
                if (editsR.Count > 0) SimSubModule.L("right-hand grips loaded for: " + string.Join(", ", editsR.Keys));
                var f = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_grip_edits.txt"); if (!File.Exists(f)) return;
                foreach (var l in File.ReadAllLines(f)) { var q = l.Split(' ').Where(x => x.Length > 0).ToArray(); if (q.Length == 7) edits[q[0]] = q.Skip(1).Select(P).ToArray(); }
                SimSubModule.L("weapon grip edits loaded for: " + string.Join(", ", edits.Keys));
            }
            catch (Exception e) { SimSubModule.L("grip edits load failed: " + e.Message); }
        }
        static Mat3 Rxyz(float rx, float ry, float rz)
        {
            float a = rx * 0.0174533f, b = ry * 0.0174533f, c = rz * 0.0174533f; float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a), cb = (float)Math.Cos(b), sb = (float)Math.Sin(b), cc = (float)Math.Cos(c), sc = (float)Math.Sin(c);
            // R = Rx * Ry * Rz (three.js Euler 'XYZ'); columns are the images of the axes
            var r00 = cb * cc; var r01 = -cb * sc; var r02 = sb; var r10 = ca * sc + sa * sb * cc; var r11 = ca * cc - sa * sb * sc; var r12 = -sa * cb; var r20 = sa * sc - ca * sb * cc; var r21 = sa * cc + ca * sb * sc; var r22 = ca * cb;
            return new Mat3(new Vec3(r00, r10, r20), new Vec3(r01, r11, r21), new Vec3(r02, r12, r22));
        }
        MatrixFrame EditedGrip()
        {
            string key = Current == State.Sprint ? "sprint" : Current.ToString().ToLowerInvariant();
            if (Mirrored) return editsR.TryGetValue(key, out var fr_) ? fr_ : gripR;
            if (!edits.TryGetValue(key, out var e)) return grip;
            var re = Rxyz(e[3], e[4], e[5]);                                       // rotation about the sword's own axes
            var rot = new Mat3(); var m = grip.rotation;                              // R = B * Redit
            Vec3 Mul(Mat3 A, Vec3 v) => A.s * v.x + A.f * v.y + A.u * v.z;
            rot.s = Mul(m, re.s); rot.f = Mul(m, re.f); rot.u = Mul(m, re.u);
            return new MatrixFrame(rot, new Vec3(e[0], e[1], e[2]));               // position offset (metres) in the hand-bone frame
        }
        void ShowProxy(Agent me, bool on)
        {
            try
            {
                if (on && swordEnt == null && gripLoaded)
                {
                    var mm = MetaMesh.GetCopy(Environment.GetEnvironmentVariable("CIRITH_PROXYMESH") ?? Item, false, true); if (mm == null) { SimSubModule.L("weapon proxy: metamesh not found"); gripLoaded = false; return; }
                    swordEnt = GameEntity.CreateEmpty(Mission.Current.Scene, false, false, false); swordEnt.AddMultiMesh(mm, true); SimSubModule.L("weapon proxy entity created");
                }
                if (!on)
                {
                    if (swordEnt != null) { try { swordEnt.Remove(0); } catch { } swordEnt = null; SimSubModule.L("weapon proxy removed"); }
                }
            }
            catch (Exception e) { SimSubModule.L("weapon proxy err " + e.Message); }
        }
        // remove the sword entity (agent died / mission ends) - never leave a floating sword behind
        public void Dispose() { if (swordEnt != null) { try { swordEnt.Remove(0); } catch { } swordEnt = null; SimSubModule.L("weapon proxy removed (dispose)"); } }
        static string V(Vec3 v) { return v.x.ToString("0.0000") + "," + v.y.ToString("0.0000") + "," + v.z.ToString("0.0000"); }
        void UpdateProxy(Agent me)
        {
            if (swordEnt == null) return;
            try
            {
                var gf = me.AgentVisuals.GetEntity().GetGlobalFrame(); var hf = me.AgentVisuals.GetSkeleton().GetBoneEntitialFrame((sbyte)(Mirrored ? 26 : 19));
                var world = gf.TransformToParent(hf.TransformToParent(EditedGrip()));
                if (engineHold)      // the engine's own attack/block/guard animation moves the combat (right) hand: the visible sword sits in that hand, same grip as the left one but mirrored
                {
                    var hr = me.AgentVisuals.GetSkeleton().GetBoneEntitialFrame(26); world = gf.TransformToParent(hr.TransformToParent(AtkGrip(me)));
                }
                swordEnt.SetGlobalFrame(ref world, false);
                prevW = haveW ? curW : world; curW = world; haveW = true; if (me == Agent.Main) MainInst = this;
                if (Environment.GetEnvironmentVariable("CIRITH_GRIPLOG") == "1" && dbgN++ % 40 == 0 && clip != null)
                {
                    var back = swordEnt.GetGlobalFrame(); var inv = gf.TransformToLocal(back); var gl = EditedGrip();
                    SimSubModule.L("GRIPLOG state=" + Current + " clipFrame=" + (((int)phase) % clip.N) + " weight=" + weight.ToString("0.00") + " | gfScale=" + gf.rotation.s.Length.ToString("0.000") + " | hand(model) origin=" + V(hf.origin) + " s=" + V(hf.rotation.s) + " f=" + V(hf.rotation.f) + " u=" + V(hf.rotation.u) + " | gripT=" + V(gl.origin) + " | swordReadBack(model) origin=" + V(inv.origin) + " Z=" + V(inv.rotation.u) + " X=" + V(inv.rotation.s) + " | pelvis(model)=" + V(me.AgentVisuals.GetSkeleton().GetBoneEntitialFrame(0).origin));
                }
            }
            catch (Exception e) { SimSubModule.L("proxy update err " + e.Message); }
        }

        void Apply(Agent me, float dt)
        {
            var sk = me.AgentVisuals.GetSkeleton(); int n = sk.GetBoneCount(); if (n != clip.NB) return;
            int fr = ((int)phase) % clip.N; if (fr < 0) fr += clip.N;
            xfade = Math.Min(1f, xfade + dt / 0.18f); float xf = xfade * xfade * (3 - 2 * xfade);
            var ent = new MatrixFrame[n]; for (int i = 0; i < n; i++) ent[i] = sk.GetBoneEntitialFrame((sbyte)i);
            var loc = new MatrixFrame[n]; var r = clip.R[fr];
            for (int i = 0; i < n; i++)
            {
                sbyte par = sk.GetParentBoneIndex((sbyte)i); var cur = par >= 0 ? ent[par].TransformToLocal(ent[i]) : ent[i]; int o = i * 9;
                Vec3 s = new Vec3(r[o + 0], r[o + 3], r[o + 6]), f = new Vec3(r[o + 1], r[o + 4], r[o + 7]), u = new Vec3(r[o + 2], r[o + 5], r[o + 8]);
                if (havePrev && xf < 1f) { var q = prevPose; s = Vec3.Lerp(new Vec3(q[o + 0], q[o + 3], q[o + 6]), s, xf); f = Vec3.Lerp(new Vec3(q[o + 1], q[o + 4], q[o + 7]), f, xf); u = Vec3.Lerp(new Vec3(q[o + 2], q[o + 5], q[o + 8]), u, xf); }
                var m = new Mat3(cur.rotation.s * (1 - weight) + s * weight, cur.rotation.f * (1 - weight) + f * weight, cur.rotation.u * (1 - weight) + u * weight); m.Orthonormalize();   // weight = fade with the engine's own pose
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
