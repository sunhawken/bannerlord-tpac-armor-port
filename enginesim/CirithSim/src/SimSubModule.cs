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

        public override void OnInitialState() { L("initial state (main menu) reached"); menu = true; t = 0; }

        protected override void OnApplicationTick(float dt)
        {
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
            L("mission behavior init"); mission.AddMissionBehavior(new SimBehavior());
        }
    }

    public class SimBehavior : MissionBehavior
    {
        public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;
        float t, tFound = -1; bool camSet, shot, bearingSet; static float Dist => float.TryParse(Environment.GetEnvironmentVariable("CIRITH_DIST"), out var v) ? v : 3.0f; static float Az => float.TryParse(Environment.GetEnvironmentVariable("CIRITH_AZ"), out var v) ? v : 0f; Vec3 CamSide() { return new Vec3(0, 0, 0); } Agent me;


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
                if (me != null && ViewQueueTick(dt)) return;
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
