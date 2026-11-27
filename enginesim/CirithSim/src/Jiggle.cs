using System;
using System.IO;
using Path = System.IO.Path;
using System.Linq;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CirithSim
{
    // ---- spare-bone jiggle rig. l_finger0 = breasts (3D spring in chest-local axes), l_toe0/r_toe0 = glutes (springs in pelvis-local axes), r_finger0 = ponytail (world pendulum, rotation about the root).
    // Skin matrix of a vertex on a spare bone = boneAnim * inverse(boneRest). We want it to follow its parent bone Pf (spine2/pelvis/head) plus a local offset L:
    //   boneAnim = PfAnim * L * (PfRest^-1 * boneRest)
    public class Jiggle
    {
        Skeleton sk; bool ok; int iChest, iPelv, iHead, iBr, iGl, iGr, iPon; MatrixFrame rChest, rPelv, rHead, rBr, rGl, rGr, rPon; Vec3 pivot;
        Vec3 xBr, vBr, xGl, vGl, xGr, vGr; Vec3 cW, cV, pW, pV, hW, hV; bool have; Vec3 u, om; float gain = 1f; int n, tries, dbg;
        static Vec3 Dir(Vec3 v) { v.Normalize(); return v; }
        static Vec3 ToLoc(MatrixFrame f, Vec3 v) { var d = v - f.origin; return new Vec3(Vec3.DotProduct(d, f.rotation.s), Vec3.DotProduct(d, f.rotation.f), Vec3.DotProduct(d, f.rotation.u)); }
        static Vec3 ToPar(MatrixFrame f, Vec3 v) { return f.origin + f.rotation.s * v.x + f.rotation.f * v.y + f.rotation.u * v.z; }
        static Vec3 DirLoc(Mat3 r, Vec3 v) { return new Vec3(Vec3.DotProduct(v, r.s), Vec3.DotProduct(v, r.f), Vec3.DotProduct(v, r.u)); }
        static Vec3 DirPar(Mat3 r, Vec3 v) { return r.s * v.x + r.f * v.y + r.u * v.z; }
        static MatrixFrame Rod(Vec3 axis, float ang, Vec3 center)   // rotation by ang about axis through center
        {
            axis.Normalize(); float c = (float)Math.Cos(ang), s = (float)Math.Sin(ang), t = 1 - c; Vec3 a = axis;
            Vec3 cx = new Vec3(t * a.x * a.x + c, t * a.x * a.y + s * a.z, t * a.x * a.z - s * a.y), cy = new Vec3(t * a.x * a.y - s * a.z, t * a.y * a.y + c, t * a.y * a.z + s * a.x), cz = new Vec3(t * a.x * a.z + s * a.y, t * a.y * a.z - s * a.x, t * a.z * a.z + c);
            var m = new Mat3(cx, cy, cz); var org = center - (cx * center.x + cy * center.y + cz * center.z); return new MatrixFrame(m, org);
        }
        static void Spring(ref Vec3 x, ref Vec3 v, Vec3 aIn, float w, float z, float g, float lim, float dt)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.006f)); float h = dt / steps;
            for (int i = 0; i < steps; i++) { var acc = x * (-w * w) - v * (2 * z * w) - aIn * g; v += acc * h; x += v * h; }
            float l = x.Length; if (l > lim) { x *= lim / l; if (Vec3.DotProduct(v, x) > 0) v *= 0.5f; }
        }
        static float F(string s) { return float.Parse(s, System.Globalization.CultureInfo.InvariantCulture); }
        bool Init(Agent me)
        {
            sk = me.AgentVisuals.GetSkeleton(); int nb = sk.GetBoneCount();
            int Idx(string nm) { for (int i = 0; i < nb; i++) if (sk.GetBoneName((sbyte)i) == nm) return i; return -1; }
            iChest = Idx("spine2"); iPelv = Idx("pelvis"); iHead = Idx("head"); iBr = Idx("l_finger0"); iGl = Idx("l_toe0"); iGr = Idx("r_toe0"); iPon = Idx("r_finger0");
            if (new[] { iChest, iPelv, iHead, iBr, iGl, iGr, iPon }.Any(i => i < 0)) { SimSubModule.L("jiggle: bone missing"); return false; }
            rChest = sk.GetBoneEntitialRestFrame((sbyte)iChest); rPelv = sk.GetBoneEntitialRestFrame((sbyte)iPelv); rHead = sk.GetBoneEntitialRestFrame((sbyte)iHead);
            rBr = sk.GetBoneEntitialRestFrame((sbyte)iBr); rGl = sk.GetBoneEntitialRestFrame((sbyte)iGl); rGr = sk.GetBoneEntitialRestFrame((sbyte)iGr); rPon = sk.GetBoneEntitialRestFrame((sbyte)iPon);
            pivot = new Vec3(0, -0.0196f, 1.7847f);
            try { var f = Path.Combine(Path.GetDirectoryName(SimSubModule.LogPath), "ModuleData", "cirith_spare.txt"); foreach (var l in File.ReadAllLines(f)) { var p = l.Split(' '); if (p[0] == "pivot") pivot = new Vec3(F(p[1]), F(p[2]), F(p[3])); } } catch (Exception e) { SimSubModule.L("jiggle: pivot file " + e.Message); }
            var jg = Environment.GetEnvironmentVariable("CIRITH_JIG"); gain = string.IsNullOrEmpty(jg) ? 1f : F(jg);
            SimSubModule.L($"jiggle init: bones chest={iChest} pelvis={iPelv} head={iHead} breast={iBr} glutes={iGl},{iGr} pony={iPon}; pivot={pivot}; gain={gain}; headRest={rHead.origin}; ponRest={rPon.origin}; brRest={rBr.origin}");
            return true;
        }
        public void Update(Agent me, float dt)
        {
            if (!ok) { if (tries++ > 3) return; ok = Init(me); if (!ok) return; }
            if (gain <= 0) return;
            float rawDt = Math.Max(dt, 0.001f); dt = Math.Min(rawDt, 0.05f);
            var gf = me.AgentVisuals.GetEntity().GetGlobalFrame(); var gr = gf.rotation; gr.Orthonormalize();
            var aChest = sk.GetBoneEntitialFrame((sbyte)iChest); var aPelv = sk.GetBoneEntitialFrame((sbyte)iPelv); var aHead = sk.GetBoneEntitialFrame((sbyte)iHead);
            var pLoc = ToLoc(rHead, pivot);
            Vec3 cw = gf.origin + DirPar(gr, aChest.origin), pw = gf.origin + DirPar(gr, aPelv.origin), hw = gf.origin + DirPar(gr, ToPar(aHead, pLoc));
            Vec3 cA = Vec3.Zero, pA = Vec3.Zero, hA = Vec3.Zero;
            if (have && (rawDt > 0.1f || (cw - cW).Length > 0.6f || (pw - pW).Length > 0.6f || (hw - hW).Length > 0.6f)) { cV = (cw - cW) / rawDt; pV = (pw - pW) / rawDt; hV = (hw - hW) / rawDt; }   // frame hitch (screenshot): re-sync velocity, no acceleration
            else if (have) { float k = 1f - (float)Math.Exp(-dt / 0.07f); var cv = Vec3.Lerp(cV, (cw - cW) / rawDt, k); var pv = Vec3.Lerp(pV, (pw - pW) / rawDt, k); var hv = Vec3.Lerp(hV, (hw - hW) / rawDt, k); cA = (cv - cV) / dt; pA = (pv - pV) / dt; hA = (hv - hV) / dt; cV = cv; pV = pv; hV = hv; }
            else { cV = pV = hV = Vec3.Zero; u = DirPar(gr, Dir(new Vec3(0, -0.12f, -1f))); om = Vec3.Zero; have = true; }
            cW = cw; pW = pw; hW = hw;
            bool vr = CirithVR.Active; float lim = vr ? 10f : 25f;            // VR: head/body tracking and snap moves must not shake the springs
            if (vr) hA *= 0.35f;
            Func<Vec3, Vec3> clampA = a => { float l = a.Length; return l > lim ? a * (lim / l) : a; };
            cA = clampA(cA); pA = clampA(pA); hA = clampA(hA);
            // world accel -> model space (inverse entity rotation) -> bone-local axes
            var cAl = DirLoc(aChest.rotation, DirLoc(gr, cA)); var pAl = DirLoc(aPelv.rotation, DirLoc(gr, pA));
            float lm = 0.035f * Math.Max(gain, 1f);
            Spring(ref xBr, ref vBr, cAl, 6.5f * 6.2832f, 0.12f, gain, lm, dt);
            Spring(ref xGl, ref vGl, pAl, 4.5f * 6.2832f, 0.14f, gain * 0.9f, lm, dt);
            Spring(ref xGr, ref vGr, pAl, 4.8f * 6.2832f, 0.14f, gain * 0.9f, lm, dt);
            // ponytail pendulum in world space (hangs down, slightly back like the baked pose)
            var fh = DirPar(gr, new Vec3(0, 1, 0)); fh.z = 0; fh.Normalize();
            var geff = new Vec3(0, 0, -9.81f) - hA - fh * (9.81f * 0.12f); float Leff = 0.5f, zeta = 0.3f; float wn = (float)Math.Sqrt(9.81f / Leff);
            int ns = Math.Max(1, (int)Math.Ceiling(dt / 0.004f)); float h = dt / ns;
            for (int i = 0; i < ns; i++) { var al = Vec3.CrossProduct(u, geff) * (1f / Leff); om += al * h - om * (2 * zeta * wn * h); u += Vec3.CrossProduct(om, u) * h; u.Normalize(); om -= u * Vec3.DotProduct(om, u); }
            // write the spare bones
            void Put(int bone, MatrixFrame anim) { sbyte par = sk.GetParentBoneIndex((sbyte)bone); var pe = sk.GetBoneEntitialFrame(par); sk.SetBoneLocalFrame((sbyte)bone, pe.TransformToLocal(anim)); }
            var Lbr = new MatrixFrame(Mat3.Identity, xBr); Put(iBr, aChest.TransformToParent(Lbr.TransformToParent(rChest.TransformToLocal(rBr))));
            var Lgl = new MatrixFrame(Mat3.Identity, xGl); Put(iGl, aPelv.TransformToParent(Lgl.TransformToParent(rPelv.TransformToLocal(rGl))));
            var Lgr = new MatrixFrame(Mat3.Identity, xGr); Put(iGr, aPelv.TransformToParent(Lgr.TransformToParent(rPelv.TransformToLocal(rGr))));
            var r0L = DirLoc(rHead.rotation, Dir(new Vec3(0, -0.12f, -1f))); var uL = DirLoc(aHead.rotation, DirLoc(gr, u));
            var ax = Vec3.CrossProduct(r0L, uL); float sn = ax.Length, cs = Vec3.DotProduct(r0L, uL); float ang = (float)Math.Atan2(sn, cs); ang = Math.Min(ang, vr ? 0.5f : 0.8f);
            MatrixFrame Lp = sn > 1e-5f ? Rod(ax, ang, pLoc) : MatrixFrame.Identity;
            Put(iPon, aHead.TransformToParent(Lp.TransformToParent(rHead.TransformToLocal(rPon))));
            if (float.IsNaN(xBr.x + xGl.x + xGr.x + vBr.x + vGl.x + vGr.x + om.x + u.x) || float.IsInfinity(xBr.x + xGl.x + xGr.x)) { xBr = vBr = xGl = vGl = xGr = vGr = Vec3.Zero; om = Vec3.Zero; u = DirPar(gr, Dir(new Vec3(0, -0.12f, -1f))); SimSubModule.L("jiggle: reset after a numeric blow-up"); }
            sk.UpdateEntitialFramesFromLocalFrames();
            if (Environment.GetEnvironmentVariable("CIRITH_JIGLOG") == "2" && cV.Length > 0.3f && dbg++ < 80) SimSubModule.L($"tick dt={dt:0.0000} rawstep={(cw - cW).Length:0.0000} speedF={cV.Length:0.00} cA={cA.Length:0.0}");
            if (n++ % 30 == 1 && Environment.GetEnvironmentVariable("CIRITH_JIGLOG") == "1") SimSubModule.L($"jig xBr={xBr} xGl={xGl} ponAng={ang * 57.3f:0.0} cA={cA} hA={hA}");
        }
    }
}
