using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using Path = System.IO.Path;

namespace CirithSim
{
    // Live command channel for the persistent sim (CIRITH_SIMCMD=1): the game boots ONCE, then a client (enginesim/simctl.ps1) drops command lines into Modules\CirithSim\simcmd.txt and reads the
    // results from simout.txt  ("<id> ok|err <text>").  No reboot between experiments; data files (grips, voice manifest) can be reloaded with `reload`.
    public class SimCommands
    {
        public static bool Enabled => Environment.GetEnvironmentVariable("CIRITH_SIMCMD") == "1";
        string cmdFile, outFile, shotDir; float poll; Camera cam; bool camOn; Agent.MovementControlFlag holdFlags; float holdUntil; float clock; bool scripted;
        public static bool ForceSprint; float hb;
        void Out(string id, bool ok, string text) { try { File.AppendAllText(outFile, id + " " + (ok ? "ok" : "err") + " " + text.Replace("\r", " ").Replace("\n", " | ") + "\n"); } catch { } }
        static Vec3 Flat(Vec3 v) { v.z = 0; if (v.LengthSquared > 1e-8f) v.Normalize(); return v; }
        IEnumerable<Agent> Enemies(Agent me) { return Mission.Current.Agents.Where(a => a != null && a != me && a.IsActive() && a.IsHuman && a.Team != null && me.Team != null && a.Team.IsEnemyOf(me.Team)); }

        public void Tick(Agent me, float dt)
        {
            if (!Enabled) return; clock += dt;
            if (cmdFile == null)
            {
                var d = Path.GetDirectoryName(SimSubModule.LogPath); cmdFile = Path.Combine(d, "simcmd.txt"); outFile = Path.Combine(d, "simout.txt"); shotDir = Path.Combine(d, "shots"); Directory.CreateDirectory(shotDir);
                try { File.Delete(cmdFile); File.WriteAllText(outFile, ""); } catch { }
                SimSubModule.L("simcmd: command channel ready (" + cmdFile + ")"); File.WriteAllText(Path.Combine(d, "simready.txt"), "ready");
            }
            if (me != null && me.IsActive() && clock < holdUntil) { try { me.MovementFlags = holdFlags; } catch { } }
            else if (holdFlags != Agent.MovementControlFlag.None && clock >= holdUntil) { holdFlags = Agent.MovementControlFlag.None; try { if (scripted && me != null) me.MovementFlags = Agent.MovementControlFlag.None; } catch { } }
            if (camOn && me != null && me.IsActive()) ApplyCam(me);
            hb += dt; if (hb > 5f) { hb = 0; SimSubModule.L("simcmd: alive t=" + clock.ToString("0") + " dt=" + dt.ToString("0.000")); }
            poll += dt; if (poll < 0.1f) return; poll = 0;
            if (!File.Exists(cmdFile)) return;
            string[] lines; try { lines = File.ReadAllLines(cmdFile); File.Delete(cmdFile); } catch { return; }
            foreach (var l in lines)
            {
                var t = l.Trim(); if (t.Length == 0 || t.StartsWith("#")) continue; var p = t.Split(' ').Where(x => x.Length > 0).ToArray(); if (p.Length < 2) continue;
                try { Run(p[0], p[1], p.Skip(2).ToArray(), me); } catch (Exception e) { Out(p[0], false, e.GetType().Name + ": " + e.Message); }
            }
        }
        float F(string[] a, int i, float d) { return a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : d; }
        float camAz, camEl = 0.1f, camDist = 3f, camH = 1f; bool camErr;
        void ApplyCam(Agent me)
        {
            try
            {
                var ms = TaleWorlds.ScreenSystem.ScreenManager.TopScreen as TaleWorlds.MountAndBlade.View.Screens.MissionScreen; if (ms == null) return; if (cam == null) cam = Camera.CreateCamera();
                var f = Flat(me.LookDirection); var side = new Vec3(-f.y, f.x, 0); var target = me.Position + new Vec3(0, 0, camH);
                var dir = f * (float)Math.Cos(camAz) + side * (float)Math.Sin(camAz); var pos = target + dir * camDist * (float)Math.Cos(camEl) + new Vec3(0, 0, camDist * (float)Math.Sin(camEl));
                var look = target - pos; look.Normalize(); var m = Mat3.CreateMat3WithForward(ref look); m.RotateAboutSide(1.5707964f); cam.Frame = new MatrixFrame(m, pos); cam.SetFovVertical(0.9f, 16f / 9f, 0.05f, 2000f);
                typeof(TaleWorlds.MountAndBlade.View.Screens.MissionScreen).GetProperty("CustomCamera").SetValue(ms, cam);
            }
            catch (Exception e) { if (!camErr) { camErr = true; SimSubModule.L("simcmd cam err " + e.Message); } }
        }
        string State(Agent me)
        {
            var sb = new StringBuilder(); var w = WeaponAnim.MainInst;
            string act = ""; try { var ac = me.GetCurrentAction(1); act = ac.GetName() + "/" + me.GetCurrentActionType(1); } catch { }
            string act0 = ""; try { var ac0 = me.GetCurrentAction(0); act0 = ac0.GetName() + "/" + me.GetCurrentActionType(0); } catch { }
            string clip = ""; try { var ac = me.GetCurrentAction(1); clip = me.ActionSet.GetAnimationName(ref ac); } catch { }
            float mx = -1, cm = -1; try { mx = me.GetAgentDrivenPropertyValue(DrivenProperty.MaxSpeedMultiplier); cm = me.GetAgentDrivenPropertyValue(DrivenProperty.CombatMaxSpeedMultiplier); } catch { }
            string wield = ""; try { var pi = me.GetPrimaryWieldedItemIndex(); wield = pi == EquipmentIndex.None ? "none" : me.Equipment[pi].Item?.StringId; } catch { }
            sb.Append("pos=(" + me.Position.x.ToString("0.00") + "," + me.Position.y.ToString("0.00") + "," + me.Position.z.ToString("0.00") + ") hp=" + me.Health.ToString("0") + "/" + me.HealthLimit.ToString("0") + " wield=" + wield + " action=" + act + " act0=" + act0 + " clip=" + clip + " maxSpeedMult=" + mx.ToString("0.00") + " combatMult=" + cm.ToString("0.00") + " ctrl=" + me.Controller);
            sb.Append(" weaponAnim=" + (w == null ? "none" : w.Current.ToString()) + " boosted=" + (w != null && w.IsBoosted) + " vr=" + CirithVR.Active + " voice(played=" + CirithVoice.Played + ",blocked=" + CirithVoice.Blocked + ")");
            var en = Enemies(me).OrderBy(a => (a.Position - me.Position).LengthSquared).Take(3).ToList(); sb.Append(" enemies=" + string.Join(";", en.Select(a => a.Name + " d=" + (a.Position - me.Position).Length.ToString("0.0") + " hp=" + a.Health.ToString("0"))));
            return sb.ToString();
        }
        void Run(string id, string cmd, string[] a, Agent me)
        {
            if ((me == null || !me.IsActive()) && cmd != "log" && cmd != "quit" && cmd != "ping") { Out(id, false, "no active player agent"); return; }
            switch (cmd)
            {
                case "ping": Out(id, true, "pong t=" + clock.ToString("0.0")); break;
                case "log": SimSubModule.L("simcmd: " + string.Join(" ", a)); Out(id, true, "logged"); break;
                case "quit": Out(id, true, "bye"); TaleWorlds.Engine.Utilities.ExitProcess(0); break;
                case "state": Out(id, true, State(me)); break;
                case "script": scripted = a.Length == 0 || a[0] != "off"; try { me.Controller = scripted ? AgentControllerType.None : AgentControllerType.Player; } catch { } Out(id, true, "controller=" + me.Controller); break;
                case "cam": camAz = F(a, 0, 0f); camEl = F(a, 1, 0.1f); camDist = F(a, 2, 3f); camH = F(a, 3, 1f); camOn = true; Out(id, true, "cam az=" + camAz + " el=" + camEl + " dist=" + camDist + " h=" + camH); break;
                case "camoff": camOn = false; Out(id, true, "cam off"); break;
                case "shot":
                    {
                        var name = a.Length > 0 ? a[0] : "shot";
                        if (a.Length >= 5) { camAz = F(a, 1, 0); camEl = F(a, 2, 0.1f); camDist = F(a, 3, 3f); camH = F(a, 4, 1f); camOn = true; }
                        var path = Path.Combine(shotDir, name + ".bmp"); Utilities.TakeScreenshot(path); Out(id, true, path); break;
                    }
                case "move":
                    {
                        var dn = a.Length > 0 ? a[0] : "forward"; var fl = dn == "back" ? Agent.MovementControlFlag.Backward : dn == "left" ? Agent.MovementControlFlag.StrafeLeft : dn == "right" ? Agent.MovementControlFlag.StrafeRight : Agent.MovementControlFlag.Forward;
                        holdFlags = fl; holdUntil = clock + F(a, 1, 2f); Out(id, true, "moving " + dn + " for " + F(a, 1, 2f) + " s (use `script on` first so player input does not fight it)"); break;
                    }
                case "attack":
                    {
                        var d = a.Length > 0 ? a[0] : "right"; var fl = d == "left" ? Agent.MovementControlFlag.AttackLeft : d == "up" ? Agent.MovementControlFlag.AttackUp : d == "down" ? Agent.MovementControlFlag.AttackDown : Agent.MovementControlFlag.AttackRight;
                        holdFlags = fl; holdUntil = clock + F(a, 1, 1.2f); Out(id, true, "attack " + d); break;
                    }
                case "block": holdFlags = Agent.MovementControlFlag.DefendAuto; holdUntil = clock + F(a, 0, 1.5f); Out(id, true, "block"); break;
                case "stop": holdFlags = Agent.MovementControlFlag.None; holdUntil = 0; ForceSprint = false; Out(id, true, "stopped"); break;
                case "jump": me.EventControlFlags |= Agent.EventControlFlag.Jump; Out(id, true, "jump"); break;
                case "sprint": ForceSprint = a.Length == 0 || a[0] != "off"; Out(id, true, "forceSprint=" + ForceSprint); break;
                case "wield":
                    {
                        var on = a.Length == 0 || a[0] != "off";
                        if (on) { for (var i = EquipmentIndex.Weapon0; i <= EquipmentIndex.Weapon3; i++) { var s = me.Equipment[i].Item?.StringId; if (s == WeaponAnim.Item || s == WeaponAnim.ItemVr) { me.TryToWieldWeaponInSlot(i, Agent.WeaponWieldActionType.Instant, false); break; } } }
                        else me.TryToSheathWeaponInHand(Agent.HandIndex.MainHand, Agent.WeaponWieldActionType.Instant);
                        Out(id, true, "wield " + on); break;
                    }
                case "give": CirithRuntimeHooks.Give(me); Out(id, true, "give requested"); break;
                case "voice": { var t = a.Length > 0 ? a[0] : "Pain"; CirithVoice.Trigger(t, "simcmd"); Out(id, true, "voice " + t); break; }
                case "bring":
                    {
                        var dist = F(a, 0, 1.4f); var en = Enemies(me).OrderBy(x => (x.Position - me.Position).LengthSquared).FirstOrDefault(); if (en == null) { Out(id, false, "no enemy"); break; }
                        var f = Flat(me.LookDirection); var p = me.Position + f * dist; try { en.TeleportToPosition(p); } catch { } try { en.SetLookAgent(me); } catch { } Out(id, true, "brought " + en.Name + " to " + dist + " m in front"); break;
                    }
                case "freeze":
                    {
                        var n = 0; foreach (var en in Enemies(me).OrderBy(x => (x.Position - me.Position).LengthSquared).Take(a.Length > 0 ? (int)F(a, 0, 1) : 1)) { try { en.Controller = AgentControllerType.None; n++; } catch { } }
                        Out(id, true, "froze " + n + " enemy(ies) (target dummies)"); break;
                    }
                case "heal": me.Health = me.HealthLimit; Out(id, true, "hp " + me.Health); break;
                case "hp": { var en = Enemies(me).OrderBy(x => (x.Position - me.Position).LengthSquared).FirstOrDefault(); Out(id, true, en == null ? "no enemy" : en.Name + " hp=" + en.Health.ToString("0") + "/" + en.HealthLimit.ToString("0")); break; }
                case "reload": CirithRuntimeHooks.ReloadData(); Out(id, true, "data reloaded (grips, voice)"); break;
                default: Out(id, false, "unknown command " + cmd); break;
            }
        }
    }

    public static class CirithRuntimeHooks
    {
        public static void Give(Agent a) { var m = typeof(CirithRuntimeBehavior).GetMethod("GiveSword", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public); if (m != null) m.Invoke(null, new object[] { a }); }
        public static void ReloadData() { try { if (WeaponAnim.MainInst != null) WeaponAnim.MainInst.ReloadEdits(); } catch { } try { CirithVoice.Reload(); } catch { } SimSubModule.L("simcmd: data reloaded"); }
    }
}
