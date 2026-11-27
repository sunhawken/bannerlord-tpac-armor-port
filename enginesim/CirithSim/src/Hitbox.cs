using System;
using System.Linq;
using System.Runtime.CompilerServices;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CirithSim
{
    // Real-geometry hitbox for Cirith's blade. The engine's own melee detection works with the item's compiled physics shape (vanilla sword capsules) at the engine's hold frame; the item is
    // given a generous weapon_length so the engine REPORTS every contact the real blade could make, and this Harmony prefix on Mission.MeleeHitCallback then keeps only the contacts that lie on the
    // REAL sword mesh (sampled hull, swept since the last tick, plus a little extra length and thickness). Everything else (damage, blocks, armour) stays the engine's own.
    public static class CirithHitbox
    {
        public static bool Enabled = true; public static float Tol = 0.06f;     // thickness margin (m) around the real geometry
        static bool installed; static int accepted, rejected, logged;
        public static void Install()
        {
            if (installed) return; installed = true;
            try
            {
                var t = Environment.GetEnvironmentVariable("CIRITH_HITTOL"); if (!string.IsNullOrEmpty(t) && float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) Tol = v;
                if (Environment.GetEnvironmentVariable("CIRITH_HITBOX") != "1") { SimSubModule.L("hitbox: real-geometry filter is OFF (set CIRITH_HITBOX=1 to test it) - the engine hitbox is used"); return; }
                if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "0Harmony")) { SimSubModule.L("hitbox: Harmony not loaded - engine hitbox stays"); return; }
                Patch();
            }
            catch (Exception e) { SimSubModule.L("hitbox install err " + e.Message); }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Patch()
        {
            var h = new HarmonyLib.Harmony("cirith.hitbox");
            var orig = typeof(Mission).GetMethod("MeleeHitCallback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var pre = typeof(CirithHitbox).GetMethod(nameof(Prefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            h.Patch(orig, new HarmonyLib.HarmonyMethod(pre)); SimSubModule.L("hitbox: Mission.MeleeHitCallback patched (tolerance " + Tol.ToString("0.000") + " m)");
        }
        // false = skip the engine's own handling of this contact
        static bool Prefix(ref AttackCollisionData collisionData, Agent attacker, Agent victim, ref MeleeCollisionReaction colReaction)
        {
            try
            {
                if (!Enabled || attacker == null || attacker != Agent.Main || victim == null || !WeaponAnim.IsWielding(attacker)) return true;
                var w = WeaponAnim.MainInst; if (w == null) return true;
                var p = collisionData.CollisionGlobalPosition; float d = w.MinDistance(p);
                if (d == float.MaxValue) return true;                      // no geometry data yet: leave the engine alone
                bool ok = d <= Tol;
                if (logged++ < 60) SimSubModule.L("hitbox: contact with " + (victim.Name ?? "agent") + " at " + d.ToString("0.000") + " m from the real blade -> " + (ok ? "HIT" : "ignored (off the blade)"));
                if (ok) { accepted++; return true; }
                rejected++; colReaction = MeleeCollisionReaction.ContinueChecking; return false;
            }
            catch (Exception e) { SimSubModule.L("hitbox prefix err " + e.Message); return true; }
        }
    }
}
