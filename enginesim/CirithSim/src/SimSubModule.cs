using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.CustomBattle;
using TaleWorlds.MountAndBlade.CustomBattle.CustomBattle;
using TaleWorlds.MountAndBlade.View.Screens;
using TaleWorlds.ObjectSystem;
using TaleWorlds.ScreenSystem;

namespace CirithSim
{
    // Bare-minimum real-engine sim: boots the game, auto-starts a Custom Battle with ONE test unit (id from env CIRITH_CHAR, default cirith_test),
    // parks the camera on her, writes everything to <module>/cirithsim.log and quits. Driven by run_engine_sim.ps1.
    public class SimSubModule : MBSubModuleBase
    {
        public static string LogPath;
        // The auto-battle / auto-quit sim harness is ONLY active when the sim script sets CIRITH_CHAR. In a normal game the module is passive (items + jiggle + charge + inventory keeper).
        public static bool Harness => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CIRITH_CHAR"));
        public static void L(string m) { try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff ") + m + "\n"); } catch { } }
        float t; int stage; bool menu; public static string Target = "cirith_test";

        protected override void OnSubModuleLoad()
        {
            var dir = Path.GetDirectoryName(typeof(SimSubModule).Assembly.Location);
            LogPath = Path.GetFullPath(Path.Combine(dir, "..", "..", "cirithsim.log"));
            File.WriteAllText(LogPath, "");
            Target = Environment.GetEnvironmentVariable("CIRITH_CHAR") ?? "cirith_test";
            L("SubModule loaded, target character = " + Target);
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            try { if (gameStarterObject is TaleWorlds.CampaignSystem.CampaignGameStarter cgs) { cgs.AddBehavior(new CirithInventoryBehavior()); L("campaign: inventory keeper registered"); } } catch (Exception e) { L("OnGameStart error " + e.Message); }
        }

        public override void OnInitialState() { L("initial state (main menu) reached; harness=" + Harness); menu = true; t = 0; }

        protected override void OnApplicationTick(float dt)
        {
            if (!Harness) return;
            t += dt;
            try
            {
                if (stage == 0 && menu && t > 3f && MBGameManager.Current == null)
                {
                    L("starting Custom Battle game manager"); MBGameManager.StartNewGame(new CustomGameManager()); stage = 1; t = 0;
                }
                else if (stage == 1 && t > 2f && GameStateManager.Current != null && GameStateManager.Current.ActiveState is CustomBattleState)
                {
                    L("CustomBattleState active -> launching battle"); Launch(); stage = 2; t = 0;
                }
                else if (stage == 1 && t > 90f) { L("TIMEOUT waiting for CustomBattleState"); Utilities.ExitProcess(3); }
            }
            catch (Exception e) { L("ERROR " + e); Utilities.ExitProcess(2); }
        }

        void Launch()
        {
            var om = MBObjectManager.Instance;
            var cultures = om.GetObjectTypeList<BasicCultureObject>().Select(c => c.StringId).ToList();
            L("cultures: " + string.Join(",", cultures));
            var culture = om.GetObject<BasicCultureObject>("vlandia") ?? om.GetObjectTypeList<BasicCultureObject>().First();
            var player = om.GetObject<BasicCharacterObject>(Target);
            if (player == null) { L("CHARACTER NOT FOUND: " + Target); Utilities.ExitProcess(4); return; }
            var enemy = om.GetObject<BasicCharacterObject>("commander_2") ?? om.GetObject<BasicCharacterObject>("commander_1");
            L($"player = {player.StringId} female={player.IsFemale}; equipment: " + string.Join(",", Enumerable.Range(0, 12).Select(i => player.Equipment[i].Item?.StringId ?? "-")));
            var nums = new[] { 1, 0, 0, 0 };
            var ps = new List<BasicCharacterObject>[] { new List<BasicCharacterObject>(), new List<BasicCharacterObject>(), new List<BasicCharacterObject>(), new List<BasicCharacterObject>() };
            var es = new List<BasicCharacterObject>[] { new List<BasicCharacterObject>(), new List<BasicCharacterObject>(), new List<BasicCharacterObject>(), new List<BasicCharacterObject>() };
            var parties = CustomBattleHelper.GetCustomBattleParties(player, player, enemy, culture, nums, ps, culture, nums, es, true);
            string season = CustomBattleData.Seasons.Cast<object>().Select(s => s.ToString()).FirstOrDefault() ?? "Summer";
            L("season = " + season);
            var data = CustomBattleHelper.PrepareBattleData(player, player, parties[0], parties[1], CustomBattlePlayerSide.Attacker, CustomBattlePlayerType.Commander,
                CustomBattleHelper.DefaultBattleGameTypeStringId, Environment.GetEnvironmentVariable("CIRITH_SCENE") ?? "battle_terrain_a", season, 12f,
                new List<MissionSiegeWeapon>(), new List<MissionSiegeWeapon>(), new float[] { 1f, 1f }, 0, false, null);
            L("starting mission");
            CustomBattleHelper.StartGame(data);
        }

        public override void OnMissionBehaviorInitialize(Mission mission)
        {
            L("mission behavior init; harness=" + Harness); mission.AddMissionBehavior(Harness && Environment.GetEnvironmentVariable("CIRITH_RUNTIME") != "1" ? (MissionBehavior)new SimBehavior() : new CirithRuntimeBehavior());
        }
    }

    public class SimBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        float t, tFound = -1; bool camSet, shot, bearingSet; static float Dist => float.TryParse(Environment.GetEnvironmentVariable("CIRITH_DIST"), out var v) ? v : 3.0f; static float Az => float.TryParse(Environment.GetEnvironmentVariable("CIRITH_AZ"), out var v) ? v : 0f; Vec3 CamSide() { return new Vec3(0, 0, 0); } Agent me;




        // ---- bone-control probe: CIRITH_BONETEST="name:dx,dy,dz" lifts that bone (model space, relative to its CURRENT animated frame) every tick, to learn whether SetBoneLocalFrame sticks to the render
        Jiggle jig = new Jiggle(); bool jigErr, chargeErr, rosterTested;
        int boneTestIdx = -2;
        void BoneTest()
        {
            var bt = Environment.GetEnvironmentVariable("CIRITH_BONETEST"); if (string.IsNullOrEmpty(bt)) return;
            try
            {
                var sk = me.AgentVisuals.GetSkeleton(); var parts = bt.Split(':'); var d = parts[1].Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (boneTestIdx == -2) { boneTestIdx = -1; for (int i = 0; i < sk.GetBoneCount(); i++) if (sk.GetBoneName((sbyte)i) == parts[0]) boneTestIdx = i; SimSubModule.L("bonetest bone " + parts[0] + " = " + boneTestIdx); }
                if (boneTestIdx < 0) return;
                var ent = sk.GetBoneEntitialFrame((sbyte)boneTestIdx); ent.origin += new Vec3(d[0], d[1], d[2]);
                sbyte par = sk.GetParentBoneIndex((sbyte)boneTestIdx); var pe = sk.GetBoneEntitialFrame(par); var local = pe.TransformToLocal(ent);
                sk.SetBoneLocalFrame((sbyte)boneTestIdx, local); sk.UpdateEntitialFramesFromLocalFrames();
            }
            catch (Exception e) { SimSubModule.L("bonetest err " + e.Message); }
        }



        // ---- weapon animation test (CIRITH_WTEST=1, character cirith_sword_test): scripted idle -> walk -> run -> sprint -> charge attack -> run -> idle -> other weapon -> back; side camera frames in shots/
        WeaponAnim wa = new WeaponAnim(); float wt = -1; int wshot; Camera wcam; float wspeed; Vec3 wpos; bool wInit; List<string> wlog = new List<string>(); string wlast = "";
        bool WTest(float dt)
        {
            if (Environment.GetEnvironmentVariable("CIRITH_WTEST") != "1") return false;
            if (wt < 0) { wt = 0; shotDir = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "shots"); Directory.CreateDirectory(shotDir); }
            wt += dt; var ms = ScreenManager.TopScreen as MissionScreen; if (ms == null) return true;
            try
            {
                if (wcam == null) wcam = Camera.CreateCamera();
                var f = me.LookDirection; f.z = 0; f.Normalize(); var side = new Vec3(-f.y, f.x, 0); var target = me.Position + new Vec3(0, 0, 1.0f); var camPos = target + side * 3.2f + new Vec3(0, 0, 0.2f); var look = target - camPos; look.Normalize();
                var m = Mat3.CreateMat3WithForward(ref look); m.RotateAboutSide(1.5707964f); wcam.Frame = new MatrixFrame(m, camPos); wcam.SetFovVertical(0.9f, 16f / 9f, 0.05f, 2000f);
                typeof(MissionScreen).GetProperty("CustomCamera").SetValue(ms, wcam);
            }
            catch (Exception e) { SimSubModule.L("wtest cam err " + e.Message); }
            if (!wInit) { wInit = true; wpos = me.Position; wa.UseOverrides = true; try { me.Controller = AgentControllerType.None; } catch { } }
            // timeline: (until, speed m/s, sprint, attack-pulse, label)
            string label; float spd = 0; bool sprint = false, att = false;
            if (wt < 5f) { label = "settle"; }
            else if (wt < 8f) { label = "idle"; }
            else if (wt < 12f) { label = "walk"; spd = 1.5f; }
            else if (wt < 16f) { label = "run"; spd = 3.8f; }
            else if (wt < 20f) { label = "sprint"; spd = 5.6f; sprint = true; }
            else if (wt < 20.1f) { label = "sprint+attack"; spd = 5.6f; sprint = true; att = true; }
            else if (wt < 24f) { label = "charge attack"; sprint = true; spd = 0; }
            else if (wt < 27f) { label = "run again"; spd = 3.8f; }
            else if (wt < 30f) { label = "idle again"; }
            else if (wt < 33f) { label = "other weapon"; }
            else { label = "cirith again"; }
            if (wt >= 30f && wt < 30.1f) { try { me.TryToWieldWeaponInSlot(EquipmentIndex.Weapon2, Agent.WeaponWieldActionType.Instant, false); SimSubModule.L("wtest: wielded the vanilla sword (slot 2)"); } catch (Exception e) { SimSubModule.L("wield err " + e.Message); } }
            if (wt >= 33f && wt < 33.1f) { try { me.TryToWieldWeaponInSlot(EquipmentIndex.Weapon0, Agent.WeaponWieldActionType.Instant, false); SimSubModule.L("wtest: wielded cirith_sword again (slot 0)"); } catch (Exception e) { SimSubModule.L("wield err " + e.Message); } }
            if (wt >= 4.5f && wt < 4.6f) { try { me.TryToWieldWeaponInSlot(EquipmentIndex.Weapon0, Agent.WeaponWieldActionType.Instant, false); } catch { } }
            wa.SprintHeldOverride = sprint; wa.AttackPressedOverride = att;
            if (spd > 0) { try { var f0 = me.LookDirection; f0.z = 0; f0.Normalize(); var np = me.Position + f0 * (spd * dt); np.z = me.Position.z; me.TeleportToPosition(np); } catch { } }
            if (label != wlast) { wlast = label; SimSubModule.L($"wtest phase '{label}' at {wt:0.0}s; wielded={WeaponAnim.WieldedId(me, out _) ?? "other"} state={wa.Current}"); }
            wa.Update(me, dt);
            // frames: 4 per phase after settle
            float[] marks = { 6.5f, 7.8f, 9f, 10.2f, 11.5f, 13f, 14.2f, 15.5f, 17f, 18.2f, 19.5f, 20.6f, 21.3f, 22f, 22.7f, 23.4f, 25f, 26.5f, 28.5f, 29.5f, 31.5f, 32.5f, 34f };
            if (wshot < marks.Length && wt >= marks[wshot]) { Shot($"w_{wshot:00}_{label.Replace(' ', '_').Replace('+', '_')}"); SimSubModule.L($"wtest shot {wshot} state={wa.Current} hand={(WeaponAnim.WieldedId(me, out _))}"); wshot++; }
            if (wt > 35f) { SimSubModule.L("wtest done"); Utilities.ExitProcess(0); }
            return true;
        }

        // ---- charge test (CIRITH_CHARGETEST=1): she is ordered to walk-run 60 m ahead; speed is measured 2 s before and during the charge; side camera frames are written to shots/.
        Charge charge = new Charge(); float ct = -1; Vec3 cStart; float sA, sB; int nA, nB; int cshot; bool cOrdered, cTrig, cDone; Camera ccam; Vec3 lastP;
        string RunSim() { try { float acc = 0, mx = 0; var arr = new float[32]; me.GetRunningSimulationDataUntilMaximumSpeedReached(ref acc, ref mx, arr); return $"engine running simulation: top speed {mx:0.00} m/s, time to reach {acc:0.00} s"; } catch (Exception e) { return "runsim err " + e.Message; } }
        bool ChargeTick(float dt)
        {
            if (Environment.GetEnvironmentVariable("CIRITH_CHARGETEST") != "1") return false;
            if (ct < 0) { ct = 0; shotDir = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "shots"); Directory.CreateDirectory(shotDir); }
            ct += dt; var ms = ScreenManager.TopScreen as MissionScreen; if (ms == null) return true;
            try
            {
                if (ccam == null) ccam = Camera.CreateCamera();
                var f = me.LookDirection; f.z = 0; f.Normalize(); var side = new Vec3(-f.y, f.x, 0); var target = me.Position + new Vec3(0, 0, 1.0f) + f * 0.4f; var camPos = target + side * 3.0f + new Vec3(0, 0, 0.2f); var look = target - camPos; look.Normalize();
                var m = Mat3.CreateMat3WithForward(ref look); m.RotateAboutSide(1.5707964f); ccam.Frame = new MatrixFrame(m, camPos); ccam.SetFovVertical(0.9f, 16f / 9f, 0.05f, 2000f);
                typeof(MissionScreen).GetProperty("CustomCamera").SetValue(ms, ccam);
            }
            catch (Exception e) { SimSubModule.L("charge cam err " + e.Message); }
            float spd = ct > 0.05f ? (me.Position - lastP).Length / Math.Max(dt, 0.0005f) : 0f; lastP = me.Position;
            if (ct > 5f && !cOrdered)
            {
                cOrdered = true; try { SimSubModule.L("mission mode " + Mission.Current.Mode + " ; deployment=" + (Mission.Current.GetMissionBehavior<TaleWorlds.MountAndBlade.DeploymentHandler>() != null)); Mission.Current.SetMissionMode(MissionMode.Battle, false); SimSubModule.L("mission mode now " + Mission.Current.Mode); } catch (Exception me2) { SimSubModule.L("mode err " + me2.Message); }
                try { Mission.Current.AllowAiTicking = true; Mission.Current.PauseAITick = false; SimSubModule.L("AI ticking on; deploymentFinished=" + Mission.Current.IsDeploymentFinished); } catch (Exception e3) { SimSubModule.L("ai tick err " + e3.Message); }
                try { SimSubModule.L("controller was " + me.Controller); me.Controller = AgentControllerType.AI; SimSubModule.L("controller now " + me.Controller); } catch (Exception ee) { SimSubModule.L("controller err " + ee.Message); }
                var fw = me.LookDirection; fw.z = 0; fw.Normalize(); var tgt = me.Position + fw * 120f;
                try { var wp = new WorldPosition(Mission.Current.Scene, UIntPtr.Zero, tgt, false); me.SetScriptedPosition(ref wp, false, Agent.AIScriptedFrameFlags.None); SimSubModule.L("charge test: ordered to move 120 m ahead from " + me.Position); } catch (Exception e) { SimSubModule.L("order err " + e.Message); }
            }
            if (ct > 8f && ct <= 10f) { sA += spd; nA++; }
            if (ct > 9f && ct < 9.05f) SimSubModule.L(RunSim()); SimSubModule.L($"maxFwdSpeed {me.GetMaximumForwardUnlimitedSpeed():0.00} speedLimit {me.GetMaximumSpeedLimit():0.00} maxMult {me.GetAgentDrivenPropertyValue(DrivenProperty.MaxSpeedMultiplier):0.00} pos {me.Position}");
            if (ct > 12f && ct < 12.05f) SimSubModule.L("CHARGING " + RunSim()); SimSubModule.L($"CHARGING: maxFwdSpeed {me.GetMaximumForwardUnlimitedSpeed():0.00} speedLimit {me.GetMaximumSpeedLimit():0.00} maxMult {me.GetAgentDrivenPropertyValue(DrivenProperty.MaxSpeedMultiplier):0.00} pos {me.Position}");
            if (ct > 10f && !cTrig) { cTrig = true; SimSubModule.L($"charge test: baseline speed {sA / Math.Max(nA, 1):0.00} m/s over {nA} ticks; triggering charge"); }
            bool trig = cTrig && !cDone;
            if (trig) cDone = charge.Active || cDone;
            if (ct > 11.5f && ct <= 13.5f) { sB += spd; nB++; }
            if (ct > 10.8f && cshot < 24 && ct >= 10.8f + cshot * 0.1f) { Shot($"ch_{cshot:00}"); cshot++; }
            if (ct > 14f) { SimSubModule.L($"charge test RESULT: baseline {sA / Math.Max(nA, 1):0.00} m/s, charging {sB / Math.Max(nB, 1):0.00} m/s, ratio {(sB / Math.Max(nB, 1)) / Math.Max(sA / Math.Max(nA, 1), 0.001f):0.00}"); Utilities.ExitProcess(0); }
            return true;
        }

        // ---- motion test (cloth/jiggle). CIRITH_MOTION=1 -> slide + stop, side camera, frames in shots/.
        // CIRITH_VARIANTS="itemA,itemB,..." (with CIRITH_MOTION=1) runs MANY variants in ONE game session: for each item the Body slot is swapped at runtime, she slides 3 s and stops dead,
        // frames v<idx>_<k>.bmp are written at 4 moments. One boot (~45 s) then ~9 s per variant instead of ~100 s per run.
        float mt = -1; int mshot; Camera mcam; Vec3 startPos; string[] variants; int vIdx = -1; float vStart; int vShot; bool equipped;
        bool MotionTick(float dt)
        {
            if (Environment.GetEnvironmentVariable("CIRITH_MOTION") != "1") return false;
            if (mt < 0)
            {
                mt = 0; startPos = me.Position; shotDir = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "shots"); Directory.CreateDirectory(shotDir);
                var vv = Environment.GetEnvironmentVariable("CIRITH_VARIANTS"); variants = string.IsNullOrEmpty(vv) ? null : vv.Split(','); SimSubModule.L("motion test start, variants=" + (variants == null ? "none" : variants.Length.ToString()));
            }
            mt += dt; var ms = ScreenManager.TopScreen as MissionScreen; if (ms == null) return true;
            // free camera on her left side
            try
            {
                if (mcam == null) mcam = Camera.CreateCamera();
                var f = me.LookDirection; f.z = 0; f.Normalize(); var side = new Vec3(-f.y, f.x, 0); var target = me.Position + new Vec3(0, 0, 1.0f); var camPos = target + side * 3.2f + new Vec3(0, 0, 0.2f); var look = target - camPos; look.Normalize();
                var m = Mat3.CreateMat3WithForward(ref look); m.RotateAboutSide(1.5707964f); mcam.Frame = new MatrixFrame(m, camPos); mcam.SetFovVertical(0.9f, 16f / 9f, 0.05f, 2000f);
                typeof(MissionScreen).GetProperty("CustomCamera").SetValue(ms, mcam);
            }
            catch (Exception e) { SimSubModule.L("motion cam err " + e.Message); }
            if (variants == null)   // single-run mode
            {
                if (mt > 5f && mt <= 9.2f) Slide(dt);
                if (mt >= 7.0f + mshot * 0.2f && mshot < 45) { Shot($"mot_{mshot:00}"); mshot++; }
                if (mshot >= 45) { SimSubModule.L("motion done"); Utilities.ExitProcess(0); }
                return true;
            }
            if (mt < 5f) return true;                                                  // battle settles
            if (vIdx < 0 || (mt - vStart) > 9.0f) { vIdx++; if (vIdx >= variants.Length) { SimSubModule.L("variants done"); Utilities.ExitProcess(0); return true; } vStart = mt; vShot = 0; equipped = false; try { me.TeleportToPosition(startPos); } catch { } }
            float tv = mt - vStart;
            if (!equipped && tv > 0.2f)
            {
                equipped = true;
                try
                {
                    var item = TaleWorlds.ObjectSystem.MBObjectManager.Instance.GetObject<ItemObject>(variants[vIdx]); var eq = me.SpawnEquipment.Clone(false);
                    eq[EquipmentIndex.Body] = new EquipmentElement(item); me.UpdateSpawnEquipmentAndRefreshVisuals(eq); SimSubModule.L($"variant {vIdx}: {variants[vIdx]} equipped");
                }
                catch (Exception e) { SimSubModule.L("equip err " + e.Message); }
            }
            if (tv > 2.0f && tv <= 5.0f) Slide(dt);                                    // 3 s at 3.5 m/s, then stop dead at tv=5.0
            float[] at = { 3.5f, 5.1f, 5.6f, 6.6f }; if (vShot < at.Length && tv >= at[vShot]) { Shot($"v{vIdx:00}_{vShot}"); vShot++; }
            return true;
        }
        void Slide(float dt) { try { var f0 = me.LookDirection; f0.z = 0; f0.Normalize(); var np = me.Position + f0 * (3.5f * dt); np.z = me.Position.z; me.TeleportToPosition(np); } catch (Exception e) { SimSubModule.L("teleport err " + e.Message); } }
        void Shot(string name) { try { Utilities.TakeScreenshot(Path.Combine(shotDir, name + ".bmp")); } catch { } SimSubModule.L("shot " + name); }

        // ---- scripted free-cam: CIRITH_VIEWS=<file>  lines "name az elev dist height fov" (az rad offset from behind-her, elev rad, dist m added, height m added, fov deg or 0)
        Camera cam; static float height(float h) { return h; }
        List<string[]> views; int vi = -1; float vt; string shotDir;
        static System.Reflection.FieldInfo F(string n) { return typeof(MissionScreen).GetField(n, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public); }
        bool ViewQueueTick(float dt)
        {
            var vf = Environment.GetEnvironmentVariable("CIRITH_VIEWS"); if (string.IsNullOrEmpty(vf) || !File.Exists(vf)) return false;
            if (views == null)
            {
                views = File.ReadAllLines(vf).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).Select(l => l.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
                foreach (var ty in new[] { typeof(Skeleton), typeof(MBAgentVisuals) })
                    foreach (var mi in ty.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                        if (mi.Name.Contains("Bone") || mi.Name.Contains("Frame") || mi.Name.Contains("Morph") || mi.Name.Contains("Cloth") || mi.Name.Contains("Entitial") || mi.Name.Contains("Local") || mi.Name.Contains("Mesh"))
                            SimSubModule.L("API " + ty.Name + "." + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ") -> " + mi.ReturnType.Name);
                foreach (var mi in typeof(Agent).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                    if (mi.Name.StartsWith("Set") && (mi.Name.Contains("Movement") || mi.Name.Contains("Look") || mi.Name.Contains("Target") || mi.Name.Contains("Scripted") || mi.Name.Contains("Position") || mi.Name.Contains("Control")) || mi.Name.Contains("Teleport") || mi.Name.Contains("Force"))
                        SimSubModule.L("AGENT API " + mi.Name + "(" + string.Join(", ", mi.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")");
                foreach (var fi in typeof(MissionScreen).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)) if (fi.Name.IndexOf("ist", StringComparison.OrdinalIgnoreCase) >= 0 || fi.Name.IndexOf("oom", StringComparison.OrdinalIgnoreCase) >= 0 || fi.Name.IndexOf("eight", StringComparison.OrdinalIgnoreCase) >= 0 || fi.Name.IndexOf("levation", StringComparison.OrdinalIgnoreCase) >= 0) SimSubModule.L("MS field " + fi.FieldType.Name + " " + fi.Name);
                shotDir = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "shots"); Directory.CreateDirectory(shotDir); t = 0; vt = -1; vi = -1;
                SimSubModule.L("view queue: " + views.Count + " views");
            }
            if (t < 4f) return true;                                  // let the battle settle
            var ms = ScreenManager.TopScreen as MissionScreen; if (ms == null) return true;
            if (vi == -1 || vt > 2.2f) { if (vi >= 0) { var nm = views[vi][0]; try { Utilities.TakeScreenshot(Path.Combine(shotDir, nm + ".bmp")); } catch (Exception e) { SimSubModule.L("shot fail " + e.Message); } SimSubModule.L("shot " + nm); } vi++; vt = 0; if (vi >= views.Count) { SimSubModule.L("views done"); Utilities.ExitProcess(0); return true; } }
            vt += dt; var v = views[vi]; float P(int i, float d) { float r; return v.Length > i && float.TryParse(v[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r) ? r : d; }
            float az = P(1, 0), el = P(2, 0), dist = P(3, 0), hgt = P(4, 0), fov = P(5, 0);
            try
            {
                // true free camera: our own Camera object handed to MissionScreen.CustomCamera. az: 0 = in front of her, pi = behind; dist = metres from the target point; height = target height above her feet
                if (cam == null) { cam = Camera.CreateCamera(); var cp = typeof(MissionScreen).GetProperty("CustomCamera"); SimSubModule.L("CustomCamera setter: " + (cp != null && cp.GetSetMethod(true) != null)); }
                var f = me.LookDirection; f.z = 0; f.Normalize();
                float ca = (float)Math.Cos(az), sa = (float)Math.Sin(az);
                var dirH = new Vec3(f.x * ca - f.y * sa, f.x * sa + f.y * ca, 0);               // horizontal direction from her to the camera
                var target = me.Position + new Vec3(0, 0, height(hgt));
                var camPos = target + new Vec3(dirH.x * (float)Math.Cos(el), dirH.y * (float)Math.Cos(el), (float)Math.Sin(el)) * dist;
                var look = target - camPos; look.Normalize();
                var m = Mat3.CreateMat3WithForward(ref look); m.RotateAboutSide(1.5707964f);
                cam.Frame = new MatrixFrame(m, camPos);
                cam.SetFovVertical(fov > 0 ? fov * 0.0174533f : 0.9f, 16f / 9f, 0.05f, 2000f);
                typeof(MissionScreen).GetProperty("CustomCamera").SetValue(ms, cam);
            }
            catch (Exception e) { SimSubModule.L("view cam err " + e.Message); }
            return true;
        }

        public override void OnMissionTick(float dt)
        {
            t += dt;
            try
            {
                if (me == null) me = Mission.Current.Agents.FirstOrDefault(a => a.Character != null && a.Character.StringId == SimSubModule.Target);
                if (me != null) BoneTest();
                if (me != null) { try { charge.Update(me, dt, ct > 10f && !cDone && Environment.GetEnvironmentVariable("CIRITH_CHARGETEST") == "1"); } catch (Exception ce) { if (!chargeErr) { chargeErr = true; SimSubModule.L("charge err " + ce); } } }
                if (me != null && ChargeTick(dt)) { }
                if (me != null) { try { jig.Update(me, dt); } catch (Exception je) { if (!jigErr) { jigErr = true; SimSubModule.L("jiggle err " + je); } } }
                if (me != null && WTest(dt)) { try { jig.Update(me, dt); } catch { } return; }
                if (me != null && Environment.GetEnvironmentVariable("CIRITH_CHARGETEST") == "1") return;
                if (me != null && MotionTick(dt)) return;
                if (me != null && ViewQueueTick(dt)) return;
                if (me != null && !rosterTested && Environment.GetEnvironmentVariable("CIRITH_INVTEST") == "1") { rosterTested = true; try { var r = new TaleWorlds.CampaignSystem.Roster.ItemRoster(); int a1 = CirithInventoryBehavior.EnsureRoster(r, "test: empty"); int a2 = CirithInventoryBehavior.EnsureRoster(r, "test: full"); var it = MBObjectManager.Instance.GetObject<ItemObject>("cirith_body"); r.AddToCounts(it, -1); int a3 = CirithInventoryBehavior.EnsureRoster(r, "test: body sold"); r.AddToCounts(it, 2); int a4 = CirithInventoryBehavior.EnsureRoster(r, "test: has 3 bodies"); SimSubModule.L($"INVENTORY TEST: empty roster -> added {a1} (expect 4); full -> {a2} (0); after selling body -> {a3} (1); with 3 bodies -> {a4} (0); counts now: " + string.Join(",", CirithInventoryBehavior.Pieces.Select(x => x + "=" + r.GetItemNumber(MBObjectManager.Instance.GetObject<ItemObject>(x))))); } catch (Exception ie) { SimSubModule.L("INVENTORY TEST ERROR " + ie); } }
                if (me != null && !camSet && t > 3f)
                {
                    SimSubModule.L($"agent found at {me.Position}, equipment: " + string.Join(",", Enumerable.Range(0, 12).Select(i => me.SpawnEquipment[(EquipmentIndex)i].Item?.StringId ?? "-")));
                    camSet = true; tFound = t;
                }
                if (me != null && camSet && t - tFound < 12f && Environment.GetEnvironmentVariable("CIRITH_NOCAM") != "1")
                {
                    var ms = ScreenManager.TopScreen as MissionScreen;
                    if (ms != null)
                    {
                        if (Environment.GetEnvironmentVariable("CIRITH_AZ") != null && Environment.GetEnvironmentVariable("CIRITH_AZ") != "") ms.CameraBearing = me.LookDirection.AsVec2.RotationInRadians + Az;
                    }
                }
                if (me != null && camSet && !shot && t - tFound > 9f && Environment.GetEnvironmentVariable("CIRITH_NOCAM") != "1")
                {
                    shot = true;
                    { var msr = ScreenManager.TopScreen; foreach (var mi in msr.GetType().GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy)) if (mi.Name.IndexOf("amera", StringComparison.OrdinalIgnoreCase) >= 0 || mi.Name.IndexOf("Photo", StringComparison.OrdinalIgnoreCase) >= 0) SimSubModule.L("MS member: " + mi.MemberType + " " + mi.Name); }
                    foreach (var ag in Mission.Current.Agents) SimSubModule.L($"AGENT {ag.Character?.StringId} main={ag == Agent.Main} team={ag.Team?.TeamIndex} pos={ag.Position} visible={ag.AgentVisuals != null} equip=" + string.Join(",", Enumerable.Range(0, 12).Select(i => ag.SpawnEquipment[(EquipmentIndex)i].Item?.StringId ?? "-")));
                    try
                    {
                        foreach (var nmx in new[] { "cirith_body_armor", "cirith_head_armor", "cirith_hands_armor", "cirith_legs_armor", "vlandian_woman_dress", "clo_vlandian_woman_dress", "empire_helmet_g_d", "armor_roman_mail_shirt" })
                        {
                            var mmx = MetaMesh.GetCopy(nmx, false, true);
                            if (mmx == null) { SimSubModule.L("METAMESH " + nmx + ": not found"); continue; }
                            SimSubModule.L($"METAMESH {nmx}: meshes={mmx.MeshCount} HasClothData={mmx.HasClothData()}");
                        }
                    } catch (Exception cx) { SimSubModule.L("cloth probe failed: " + cx.Message); }
                    try
                    {
                        var ent = me.AgentVisuals.GetEntity(); SimSubModule.L("entity " + ent.Name + " frame " + ent.GetGlobalFrame().origin + " visible=" + ent.IsVisibleIncludeParents());
                        Action<GameEntity, string> dump = null;
                        dump = (ge, ind) => { int cnt = ge.GetComponentCount(GameEntity.ComponentType.MetaMesh); SimSubModule.L(ind + "entity '" + ge.Name + "' metameshes=" + cnt + " children=" + ge.ChildCount + " visible=" + ge.IsVisibleIncludeParents());
                            for (int ci = 0; ci < cnt; ci++) { var mmc = ge.GetComponentAtIndex(ci, GameEntity.ComponentType.MetaMesh) as MetaMesh; if (mmc == null) continue; var bb = mmc.GetBoundingBox(); SimSubModule.L($"{ind}  metamesh[{ci}] name={mmc.GetName()} meshes={mmc.MeshCount} bbox={bb.min}..{bb.max}"); }
                            foreach (var ch in ge.GetChildren()) dump(ch, ind + "  "); };
                        dump(ent, "");
                        var sk = me.AgentVisuals.GetSkeleton(); SimSubModule.L("skeleton bones=" + sk.GetBoneCount());
                    } catch (Exception de) { SimSubModule.L("entity dump failed: " + de.Message); }
                    try { var mi2 = typeof(MissionScreen).GetMethod("SetExtraCameraParameters"); SimSubModule.L("SetExtraCameraParameters(" + string.Join(", ", mi2.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name)) + ")"); } catch (Exception e3) { SimSubModule.L("sig fail " + e3.Message); }
                    var msx = ScreenManager.TopScreen as MissionScreen; SimSubModule.L("bearing=" + msx.CameraBearing + " agentRot=" + me.LookDirection.AsVec2.RotationInRadians + " camera frame = " + (msx != null ? msx.CombatCamera.Frame.origin.ToString() : "n/a"));
                    var png = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "shot.png"));
                    foreach (var ty in typeof(Utilities).Assembly.GetTypes())
                        foreach (var mi in ty.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                            if (mi.Name.StartsWith("TakeScreenshot")) SimSubModule.L("screenshot api: " + ty.FullName + "." + mi.Name + "(" + string.Join(",", mi.GetParameters().Select(x => x.ParameterType.Name)) + ")");
                    try { Utilities.TakeScreenshot(png); SimSubModule.L("TakeScreenshot -> " + png); } catch (Exception e) { SimSubModule.L("screenshot failed " + e.Message); }
                }
                float ex; if (float.TryParse(Environment.GetEnvironmentVariable("CIRITH_EXIT"), out ex) && ex > 0 && t > ex) { SimSubModule.L("done, exiting"); Utilities.ExitProcess(0); }
            }
            catch (Exception e) { SimSubModule.L("TICK ERROR " + e.Message); }
        }
    }
}
