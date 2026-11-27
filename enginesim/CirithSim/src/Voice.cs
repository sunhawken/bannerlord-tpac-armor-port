using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Path = System.IO.Path;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CirithSim
{
    // Cirith's voice: every voice the engine would make for the PLAYER agent (Agent.Main) is replaced by the ElevenLabs-generated WAV files
    // in ModuleData/voice (made by enginesim/voice/gen_voice.py). Agent.MakeVoice is intercepted with Harmony (Bannerlord.Harmony must be loaded; without it nothing is replaced and the
    // vanilla voice stays) and the WAV is played with SoundEvent.CreateEventFromExternalFile at the agent's head.
    public static class CirithVoice
    {
        static readonly Dictionary<string, List<string>> files = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        static readonly Random rng = new Random(); static readonly List<SoundEvent> live = new List<SoundEvent>(); static readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
        static bool installed; public static bool Enabled = true; public static int Played, Blocked;
        static float Now => (float)Environment.TickCount / 1000f;

        public static void Install()
        {
            if (installed) return; installed = true;
            try
            {
                var dir = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "voice");
                if (!Directory.Exists(dir)) { SimSubModule.L("voice: no ModuleData/voice folder - nothing replaced"); return; }
                foreach (var f in Directory.GetFiles(dir, "*.wav")) { var n = Path.GetFileNameWithoutExtension(f); int u = n.LastIndexOf('_'); if (u <= 0) continue; var t = n.Substring(0, u); if (!files.TryGetValue(t, out var l)) files[t] = l = new List<string>(); l.Add(f); }
                SimSubModule.L("voice: " + files.Sum(k => k.Value.Count) + " clips for " + string.Join(",", files.Keys));
                if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0Harmony")) { SimSubModule.L("voice: Harmony not loaded - vanilla voice stays (load Bannerlord.Harmony before CirithSim)"); return; }
                Patch();
            }
            catch (Exception e) { SimSubModule.L("voice install err " + e.Message); }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Patch()
        {
            var h = new HarmonyLib.Harmony("cirith.voice");
            var orig = typeof(Agent).GetMethod("MakeVoice"); var pre = typeof(CirithVoice).GetMethod(nameof(Prefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            h.Patch(orig, new HarmonyLib.HarmonyMethod(pre)); SimSubModule.L("voice: Agent.MakeVoice patched");
        }
        static bool IsCirith(Agent a)
        {
            try
            {
                var id = a.Character?.StringId ?? ""; if (id.StartsWith("cirith")) return true;
                var eq = a.SpawnEquipment; foreach (var i in new[] { EquipmentIndex.Head, EquipmentIndex.Body, EquipmentIndex.Leg, EquipmentIndex.Gloves }) { var s = eq[i].Item?.StringId; if (s != null && s.StartsWith("cirith_")) return true; }
            }
            catch { }
            return false;
        }
        // returns false = skip the engine's own voice
        static bool Prefix(Agent __instance, SkinVoiceManager.SkinVoiceType voiceType)
        {
            try
            {
                if (!Enabled || __instance == null) return true;
                if (__instance != Agent.Main) return true;      // player only: every other agent keeps its own voice
                var t = voiceType.TypeID;
                if (!files.TryGetValue(t, out var list) || list.Count == 0) { Blocked++; return false; }      // a voice type we have no line for: silence rather than the vanilla voice
                float now = Now; if (lastPlay.TryGetValue(t, out var lp) && now - lp < (t == "Grunt" ? 0.35f : 0.8f)) return false; lastPlay[t] = now;
                Play(__instance, t, list[rng.Next(list.Count)]); return false;
            }
            catch (Exception e) { SimSubModule.L("voice prefix err " + e.Message); return true; }
        }
        public static void Play(Agent a, string type, string file)
        {
            try
            {
                for (int i = live.Count - 1; i >= 0; i--) { try { if (live[i].IsStopped()) { live[i].Release(); live.RemoveAt(i); } } catch { live.RemoveAt(i); } }
                var ev = SoundEvent.CreateEventFromExternalFile("cirith_" + type, file, Mission.Current.Scene, true, false);
                if (ev == null) { SimSubModule.L("voice: event null for " + Path.GetFileName(file)); return; }
                var pos = a.GetEyeGlobalPosition(); ev.SetPosition(pos); bool ok = ev.Play(); live.Add(ev); Played++;
                SimSubModule.L("voice: " + type + " -> " + Path.GetFileName(file) + " played=" + ok);
            }
            catch (Exception e) { SimSubModule.L("voice play err " + e.Message); }
        }
    }
}
