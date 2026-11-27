using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using TpacTool.Lib;
using TpacTool.IO;

// Bannerlord TPAC -> GLB exporter (skinned meshes, PNG textures, skeletal animation).
// usage: tool <AssetPackages dir> diag
//        tool <AssetPackages dir> export <config.json> <outdir>
class P
{
    const float FPS = 30f;
    static AssetManager M;
    static Dictionary<string, Metamesh> MM = new Dictionary<string, Metamesh>();
    static Dictionary<string, SkeletalAnimation> AN = new Dictionary<string, SkeletalAnimation>();
    static Skeleton Skel;
    public static string TexCache = Path.GetFullPath(Environment.GetEnvironmentVariable("TEXCACHE") ?? "texcache");

    static System.Numerics.Quaternion QT(Vector3 n, Vector3 t, float w)
    {
        var b = Vector3.Cross(n, t); // columns N,T,B
        float m00 = n.X, m10 = n.Y, m20 = n.Z, m01 = t.X, m11 = t.Y, m21 = t.Z, m02 = b.X, m12 = b.Y, m22 = b.Z;
        float tr = m00 + m11 + m22; System.Numerics.Quaternion q;
        if (tr > 0) { float sq = MathF.Sqrt(tr + 1f) * 2; q = new System.Numerics.Quaternion((m21 - m12) / sq, (m02 - m20) / sq, (m10 - m01) / sq, 0.25f * sq); }
        else if (m00 > m11 && m00 > m22) { float sq = MathF.Sqrt(1f + m00 - m11 - m22) * 2; q = new System.Numerics.Quaternion(0.25f * sq, (m01 + m10) / sq, (m02 + m20) / sq, (m21 - m12) / sq); }
        else if (m11 > m22) { float sq = MathF.Sqrt(1f + m11 - m00 - m22) * 2; q = new System.Numerics.Quaternion((m01 + m10) / sq, 0.25f * sq, (m12 + m21) / sq, (m02 - m20) / sq); }
        else { float sq = MathF.Sqrt(1f + m22 - m00 - m11) * 2; q = new System.Numerics.Quaternion((m02 + m20) / sq, (m12 + m21) / sq, 0.25f * sq, (m10 - m01) / sq); }
        q = System.Numerics.Quaternion.Normalize(q); if (q.W < 0) q = new System.Numerics.Quaternion(-q.X, -q.Y, -q.Z, -q.W);
        float k = -w; return new System.Numerics.Quaternion(q.X * k, q.Y * k, q.Z * k, q.W * k);
    }

    static void Main(string[] a)
    {
        M = new AssetManager();
        M.Load(new DirectoryInfo(a[0]));
        M.SetAsDefaultGlobalResolver();
        foreach (var m in M.LoadedAssets.OfType<Metamesh>()) MM[m.Name] = m;
        foreach (var m in M.LoadedAssets.OfType<SkeletalAnimation>()) AN[m.Name] = m;
        Skel = M.LoadedAssets.OfType<Skeleton>().First(x => x.Name == "human_skeleton");
        if (a[1] == "diag") { Diag(); return; }
        if (a[1] == "tex") { foreach (var p in M.LoadedPackages) foreach (var t in p.Items.OfType<Texture>().Where(t => t.Name.Contains(a[2]))) Console.WriteLine($"{t.Name} pkg={p.File.Name} pixels={t.HasPixelData} fmt={t.Format} {t.Width}x{t.Height} guid={t.Guid}"); return; }
        if (a[1] == "seg") { foreach (var t in M.LoadedAssets.OfType<Texture>().Where(t => t.Name == a[2])) { Console.WriteLine(t.Name + " segs=" + t.TypelessDataSegments.Count + " mips=" + t.MipmapCount + " arr=" + t.ArrayCount); foreach (var ud in t.UnknownDependences) Console.WriteLine("  DEP " + ud.UnknownGuid1 + " " + ud.UnknownGuid2 + " " + ud.UnknownGuid3 + " -> " + M.GetAsset(ud.UnknownGuid1)?.Name + " | " + M.GetAsset(ud.UnknownGuid2)?.Name + " | " + M.GetAsset(ud.UnknownGuid3)?.Name); foreach (var g in t.TypelessDataSegments) { var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public; var ty = g.GetType(); Console.WriteLine("  " + ty.GetGenericArguments()[0].Name + " owner=" + g.OwnerGuid + " off=" + ty.BaseType.GetField("_offset", bf)?.GetValue(g) + " actual=" + ty.BaseType.GetField("_actualSize", bf)?.GetValue(g) + " stored=" + ty.BaseType.GetField("_storageSize", bf)?.GetValue(g) + " fmt=" + ty.BaseType.GetField("_storageFormat", bf)?.GetValue(g)); } } return; }
        if (a[1] == "cloth")
        {
            foreach (var n in a.Skip(2))
            {
                if (!MM.TryGetValue(n, out var mm)) { Console.WriteLine("MISSING " + n); continue; }
                Console.WriteLine($"== {n}: ClothMetamesh={mm.ClothMetamesh} ({(M.GetAsset(mm.ClothMetamesh)?.Name ?? "-")}) ClothString='{mm.ClothString}' ClothUint={mm.ClothUint} Original={M.GetAsset(mm.Original)?.Name} variations={mm.Variations.Count}");
                foreach (var me in mm.Meshes)
                {
                    var cm = me.ClothingMaterial; var vs = me.VertexStream?.Data;
                    string col = "-";
                    if (vs?.Colors1 != null && vs.Colors1.Length > 0)
                    {
                        var c = vs.Colors1; col = $"col1 n={c.Length} R[{c.Min(x => x.R)}..{c.Max(x => x.R)} avg {c.Average(x => (double)x.R):F0}] G[{c.Min(x => x.G)}..{c.Max(x => x.G)} avg {c.Average(x => (double)x.G):F0}] B[{c.Min(x => x.B)}..{c.Max(x => x.B)} avg {c.Average(x => (double)x.B):F0}] A[{c.Min(x => x.A)}..{c.Max(x => x.A)} avg {c.Average(x => (double)x.A):F0}]";
                    }
                    Console.WriteLine($"  mesh {me.Name} lod{me.Lod} v={me.VertexCount} f={me.FaceCount} flags=[{string.Join(",", me.Flags)}] matflags=[{string.Join(",", me.MaterialFlags)}] clothMat={(cm == null ? "null" : $"{cm.Name} bend={cm.BendingStiffness} shear={cm.ShearingStiffness} stretch={cm.StretchingStiffness} anchor={cm.AnchorStiffness} damp={cm.Damping} grav={cm.Gravity} inertia={cm.LinearInertia} maxv={cm.MaxLinearVelocity} drag={cm.AirDragMultiplier} wind={cm.Wind}")} {col}");
                }
            }
            return;
        }
        if (a[1] == "alphaz")
        {
            var mm = MM[a[2]]; var me = mm.Meshes.First(x => x.Lod == 0); var vs = me.VertexStream.Data;
            var z = vs.Positions.Select(p => p.Z).ToArray(); var al = vs.Colors1.Select(c => (double)c.A).ToArray();
            double zmin = z.Min(), zmax = z.Max();
            for (int b = 0; b < 8; b++)
            {
                var idx = Enumerable.Range(0, z.Length).Where(i => (z[i] - zmin) / (zmax - zmin) >= b / 8.0 && (z[i] - zmin) / (zmax - zmin) < (b + 1) / 8.0 + (b == 7 ? 1 : 0)).ToList();
                Console.WriteLine($"  height band {b} (0=bottom): n={idx.Count} alpha avg {(idx.Count > 0 ? idx.Average(i => al[i]) : 0):F0} min {(idx.Count > 0 ? idx.Min(i => al[i]) : 0):F0}");
            }
            return;
        }
        if (a[1] == "humananims")
        {
            foreach (var an in AN.Values.Where(x => x.Skeleton == Skel.Guid).OrderBy(x => x.Name))
            { int fr = 0; try { fr = an.Definition?.Data?.BoneAnims?.FirstOrDefault()?.RotationFrames.Count ?? 0; } catch { } Console.WriteLine(an.Name + "	" + fr); }
            return;
        }
        if (a[1] == "morph")
        {
            foreach (var n in a.Skip(2))
            {
                if (!MM.TryGetValue(n, out var mm)) { Console.WriteLine("MISSING " + n); continue; }
                foreach (var me in mm.Meshes.Where(x => x.Lod == 0))
                {
                    int mf = 0; string times = "";
                    try { var d = me.EditData?.Data; if (d != null) { mf = d.MorphFrames.Count; times = string.Join(",", d.MorphFrames.Select(f => f.Time)); } } catch (Exception e) { times = "err " + e.Message; }
                    Console.WriteLine($"{n} / {me.Name}: verts={me.VertexCount} morphFrames={mf} times=[{times}]");
                }
            }
            foreach (var ma in M.LoadedAssets.OfType<MorphAnimation>().Where(x => a.Skip(2).Any(k => x.Name.Contains(k)) ).Take(30)) Console.WriteLine("MorphAnimation asset: " + ma.Name);
            return;
        }
        if (a[1] == "morphdump")
        {
            // morphdump <metamesh> <outfile>: base positions + every morph frame (game space, metres) + triangle indices, raw float32/int32
            var mm = MM[a[2]]; var me = mm.Meshes.First(x => x.Lod == 0 && x.EditData.Data.MorphFrames.Count > 0);
            var vs = me.VertexStream.Data; var ed = me.EditData.Data; int n = vs.Positions.Length; int nf = ed.MorphFrames.Count;
            using var bw = new BinaryWriter(File.Create(a[3]));
            bw.Write(n); bw.Write(nf); bw.Write(vs.Indices.Length);
            foreach (var p in vs.Positions) { bw.Write(p.X); bw.Write(p.Y); bw.Write(p.Z); }
            foreach (var fr in ed.MorphFrames)
                for (int i = 0; i < n; i++) { var v = fr.Positions[ed.Vertices[i].PositionIndex]; bw.Write(v.X); bw.Write(v.Y); bw.Write(v.Z); }
            foreach (var ix in vs.Indices) bw.Write(ix);
            foreach (var j in vs.BoneIndices) { bw.Write(j.B1); bw.Write(j.B2); bw.Write(j.B3); bw.Write(j.B4); }
            foreach (var w in vs.BoneWeights) { bw.Write(w.W1); bw.Write(w.W2); bw.Write(w.W3); bw.Write(w.W4); }
            Console.WriteLine($"{a[2]}: {n} verts, {nf} frames, {vs.Indices.Length / 3} tris -> {a[3]}");
            return;
        }
        if (a[1] == "roundtrip")
        {
            // roundtrip <packName.tpac> <outfile>: load one package, save it again, then compare structure + bytes
            var src = Path.Combine(a[0], a[2]);
            var p = new AssetPackage(src, true, true);
            Console.WriteLine($"loaded {p.Items.Count} items, version unknown");
            p.Save(a[3], 2);
            var fa = new FileInfo(src); var fb = new FileInfo(a[3]);
            Console.WriteLine($"original {fa.Length} bytes, saved {fb.Length} bytes");
            var q = new AssetPackage(a[3], true, true);
            Console.WriteLine($"re-loaded saved package: {q.Items.Count} items");
            var da = File.ReadAllBytes(src); var db = File.ReadAllBytes(a[3]); int diff = 0; int first = -1;
            for (int i = 0; i < Math.Min(da.Length, db.Length); i++) if (da[i] != db[i]) { diff++; if (first < 0) first = i; }
            Console.WriteLine($"byte diffs {diff}, first at {first}, length delta {db.Length - da.Length}");
            return;
        }
        if (a[1] == "mkpack")
        {
            // mkpack <metamesh> <newName> <out.tpac> : clone an existing Metamesh (its own mesh data, base-game material refs) into a NEW package under a new name
            var mm = MM[a[2]];
            var pk = new AssetPackage(Guid.NewGuid());
            mm.Name = a[3]; mm.Guid = Guid.NewGuid();
            pk.Items.Add(mm);
            pk.Save(a[4], 2);
            var chk = new AssetPackage(a[4], true, true);
            Console.WriteLine($"wrote {a[4]} ({new FileInfo(a[4]).Length} bytes); re-loaded: {chk.Items.Count} items: {string.Join(",", chk.Items.Select(i => i.GetType().Name + ":" + i.Name))}");
            var rm = chk.Items.OfType<Metamesh>().First();
            Console.WriteLine($"  meshes={rm.Meshes.Count} first={rm.Meshes[0].Name} verts={rm.Meshes[0].VertexStream.Data.Positions.Length} materialRef={rm.Meshes[0].Material.Guid}");
            return;
        }
        if (a[1] == "vsinfo")
        {
            foreach (var n in a.Skip(2))
            {
                var mm = MM[n]; var me = mm.Meshes.First(x => x.Lod == 0); var vs = me.VertexStream.Data; var ed = me.EditData?.Data;
                Console.WriteLine($"== {n}/{me.Name} metamesh ver={mm.Version} verts={me.VertexCount} faces={me.FaceCount} posCount={me.PositionCount} vertKeys={me.VertexKeyCount} skin={me.SkinDataSize} unk2={me.UnknownInt2} unk3={me.UnknownInt3} flags=[{string.Join(",", me.Flags)}] bbox={me.BoundingBox.Min}..{me.BoundingBox.Max}");
                Console.WriteLine($"   stream: idx={vs.Indices.Length} col1={vs.Colors1?.Length} col2={vs.Colors2?.Length} uv1={vs.Uv1?.Length} uv2={vs.Uv2?.Length} pos={vs.Positions?.Length} anotherPos={vs.UnknownAnotherPositions?.Length} nrm={vs.Normals?.Length} tan={vs.Tangents?.Length} bw={vs.BoneWeights?.Length} bi={vs.BoneIndices?.Length} cNrm={vs.CompressedNormals?.Length} cPos={vs.CompressedPositions?.Length} cTan={vs.CompressedTangents?.Length} tt={vs.TangentTransform?.Length}");
                var us = me.VertexStream.UserData; Console.WriteLine("   userdata keys: " + string.Join(",", us.Keys));
                if (ed != null) Console.WriteLine($"   editdata: positions={ed.Positions?.Length} vertices={ed.Vertices?.Length} faces={ed.Faces?.Length} bones={ed.Bones?.Length} morph={ed.MorphFrames.Count}");
                Console.WriteLine($"   material={M.GetAsset(me.Material.Guid)?.Name} second={M.GetAsset(me.SecondMaterial.Guid)?.Name}");
            }
            return;
        }
        if (a[1] == "vsvalidate")
        {
            var mm = MM[a[2]]; var me = mm.Meshes.First(x => x.Lod == 0); var vs = me.VertexStream.Data; int n = vs.Positions.Length;
            double Max(IEnumerable<double> d) => d.Max(); double MaxF(IEnumerable<float> d) => d.Max();
            Console.WriteLine($"{a[2]}: n={n}");
            Console.WriteLine("anotherPos==pos maxdiff " + Max(Enumerable.Range(0, n).Select(i => (double)(vs.UnknownAnotherPositions[i] - vs.Positions[i]).Length())));
            Console.WriteLine("cPos(half) vs pos maxdiff " + Max(Enumerable.Range(0, n).Select(i => { var h = vs.CompressedPositions[i]; return Math.Abs((double)(float)h.X - vs.Positions[i].X) + Math.Abs((double)(float)h.Y - vs.Positions[i].Y) + Math.Abs((double)(float)h.Z - vs.Positions[i].Z); })) + "  W sample " + (float)vs.CompressedPositions[0].W + "," + (float)vs.CompressedPositions[5].W);
            Console.WriteLine("cNrm vs nrm maxdiff " + Max(Enumerable.Range(0, n).Select(i => { var c = vs.CompressedNormals[i]; return (double)(Math.Abs(c.X - vs.Normals[i].X) + Math.Abs(c.Y - vs.Normals[i].Y) + Math.Abs(c.Z - vs.Normals[i].Z)); })));
            Console.WriteLine("cNrm sample raw " + vs.CompressedNormals[3].RawData + " nrm " + vs.Normals[3] + " -> (" + vs.CompressedNormals[3].X + "," + vs.CompressedNormals[3].Y + "," + vs.CompressedNormals[3].Z + ")");
            Console.WriteLine("tan sample " + vs.Tangents[3] + "  cTan raw " + vs.CompressedTangents[3].ToString() + "  tt " + vs.TangentTransform[3]);
            Console.WriteLine("col1 sample " + vs.Colors1[3] + " col2 " + vs.Colors2[3] + " uv1 " + vs.Uv1[3] + " uv2 " + vs.Uv2[3]);
            Console.WriteLine("col1 distinct " + vs.Colors1.Select(c => c.ToString()).Distinct().Count() + " col2 distinct " + vs.Colors2.Select(c => c.ToString()).Distinct().Count() + " uv2==uv1? " + Max(Enumerable.Range(0, n).Select(i => (double)(vs.Uv2[i] - vs.Uv1[i]).Length())));
            Console.WriteLine("bw sums min/max " + vs.BoneWeights.Min(w => w.W1 + w.W2 + w.W3 + w.W4) + "/" + vs.BoneWeights.Max(w => w.W1 + w.W2 + w.W3 + w.W4) + " bi sample " + vs.BoneIndices[3] + " bw " + vs.BoneWeights[3]);
            return;
        }
        if (a[1] == "vsdump")
        {
            var mm = MM[a[2]]; var me = mm.Meshes.First(x => x.Lod == 0); var vs = me.VertexStream.Data; int n = vs.Positions.Length;
            using var bw = new BinaryWriter(File.Create(a[3]));
            bw.Write(n);
            foreach (var p in vs.Positions) { bw.Write(p.X); bw.Write(p.Y); bw.Write(p.Z); }
            foreach (var p in vs.Normals) { bw.Write(p.X); bw.Write(p.Y); bw.Write(p.Z); }
            foreach (var p in vs.Tangents) { bw.Write(p.X); bw.Write(p.Y); bw.Write(p.Z); bw.Write(p.W); }
            foreach (var p in vs.TangentTransform) { bw.Write(p.RawX); bw.Write(p.RawY); bw.Write(p.RawZ); bw.Write(p.RawW); }
            foreach (var p in vs.CompressedTangents) bw.Write(p.RawData);
            foreach (var p in vs.CompressedNormals) bw.Write(p.RawData);
            foreach (var p in vs.Uv1) { bw.Write(p.X); bw.Write(p.Y); }
            foreach (var p in vs.Uv2) { bw.Write(p.X); bw.Write(p.Y); }
            Console.WriteLine($"{a[2]}: {n} verts dumped"); return;
        }
        if (a[1] == "armorpack")
        {
            // armorpack <slots.bin> <out.tpac> : Cirith slot meshes (from transplant/dump_slots.py) -> new metameshes cloned from vanilla armor templates
            var tmpl = new Dictionary<string, string> { ["cirith_body_armor"] = "armor_roman_mail_shirt", ["cirith_legs_armor"] = "empire_plate_armor_boots", ["cirith_hands_armor"] = "mail_mitten_a", ["cirith_head_armor"] = "armor_merchant_helmet_a" };
            var pk = new AssetPackage(Guid.NewGuid());
            // ---- Cirith's own material + diffuse texture (flat normal / neutral spec / no team-colour from base-game defaults)
            Guid cirithMat = Guid.Empty;
            if (File.Exists(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a[2])), "..", "..", "enginesim", "tex", "cirith_main.dxt5")) || Environment.GetEnvironmentVariable("CIRITH_TEX") != null)
            {
                var texFile = Environment.GetEnvironmentVariable("CIRITH_TEX") ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(a[2])), "..", "..", "enginesim", "tex", "cirith_main.dxt5"));
                var tmplMat = M.LoadedAssets.OfType<Material>().First(m => m.Name == "imperial_lamellar_a_alpha");
                var tmplTex = (Texture)M.GetAsset(tmplMat.Textures[0].Guid);
                using (var tr = new BinaryReader(File.OpenRead(texFile)))
                {
                    int tw = tr.ReadInt32(), th = tr.ReadInt32(), tmips = tr.ReadInt32(); var mipData = new byte[1][][]; mipData[0] = new byte[tmips][]; for (int i = 0; i < tmips; i++) mipData[0][i] = tr.ReadBytes(tr.ReadInt32());
                    var oldTexGuid = tmplTex.Guid; tmplTex.Guid = Guid.NewGuid(); tmplTex.Name = "cirith_main_d"; tmplTex.Width = (uint)tw; tmplTex.Height = (uint)th; tmplTex.MipmapCount = (byte)tmips; tmplTex.ArrayCount = 1; tmplTex.Source = ""; tmplTex.Format = TextureFormat.DXT5;
                    var oldSeg = tmplTex.TypelessDataSegments.First(); var BFt = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    var pd = new TexturePixelData { RawImage = mipData, PrimaryRawImage = mipData[0][0] };
                    var pl = new ExternalLoader<TexturePixelData>(pd); pl.OwnerGuid = oldSeg.OwnerGuid == oldTexGuid ? tmplTex.Guid : oldSeg.OwnerGuid;
                    var udt = pl.UserData; udt[TexturePixelData.KEY_WIDTH] = tw; udt[TexturePixelData.KEY_HEIGHT] = th; udt[TexturePixelData.KEY_ARRAY] = 1; udt[TexturePixelData.KEY_MIPMAP] = tmips; udt[TexturePixelData.KEY_FORMAT] = TextureFormat.DXT5;
                    typeof(AbstractExternalLoader).GetField("_unknownUlong", BFt).SetValue(pl, typeof(AbstractExternalLoader).GetField("_unknownUlong", BFt).GetValue(oldSeg)); typeof(AbstractExternalLoader).GetField("_unknownUint", BFt).SetValue(pl, typeof(AbstractExternalLoader).GetField("_unknownUint", BFt).GetValue(oldSeg));
                    tmplTex.TypelessDataSegments[0] = pl; tmplTex.TexturePixels = pl;
                    Console.WriteLine($"texture cirith_main_d {tw}x{th} mips={tmips} oldSegType={oldSeg.GetType().GetGenericArguments()[0].Name} oldSegOwnerIsTex={oldSeg.OwnerGuid == oldTexGuid}");
                    Texture TexByName(string nme) => M.LoadedAssets.OfType<Texture>().First(t => t.Name == nme);
                    tmplMat.Guid = Guid.NewGuid(); tmplMat.Name = "cirith_main_mat";
                    tmplMat.Textures[0] = new AssetDependence<Texture>(tmplTex.Guid); tmplMat.Textures[1] = new AssetDependence<Texture>(TexByName("plain_black").Guid); tmplMat.Textures[2] = new AssetDependence<Texture>(TexByName("flat_n").Guid); tmplMat.Textures[4] = new AssetDependence<Texture>(TexByName("default_specular").Guid);
                    cirithMat = tmplMat.Guid; pk.Items.Add(tmplTex); pk.Items.Add(tmplMat);
                }
            }
            using var br = new BinaryReader(File.OpenRead(a[2]));
            int ns = br.ReadInt32();
            int njn = br.ReadInt32(); var boneMap = new int[njn]; var skb = Skel.Definition.Data.Bones;
            for (int i = 0; i < njn; i++) { var jn = System.Text.Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32())); boneMap[i] = skb.ToList().FindIndex(b => b.Name.Replace(' ', '_') == jn); if (boneMap[i] < 0) throw new Exception("bone not in skeleton: " + jn); }
            { var vs0 = MM["armor_roman_mail_shirt"].Meshes.First(x => x.Lod == 0).VertexStream.Data; double mx = 0;
              for (int i = 0; i < vs0.Positions.Length; i++) { var q = QT(vs0.Normals[i], new Vector3(vs0.Tangents[i].X, vs0.Tangents[i].Y, vs0.Tangents[i].Z), vs0.Tangents[i].W); var v = vs0.TangentTransform[i];
                  mx = Math.Max(mx, Math.Max(Math.Max(Math.Abs(q.X - v.X), Math.Abs(q.Y - v.Y)), Math.Max(Math.Abs(q.Z - v.Z), Math.Abs(q.W - v.W)))); }
              Console.WriteLine("qtangent self-check max err on vanilla: " + mx); }
            for (int si = 0; si < ns; si++)
            {
                string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(br.ReadInt32())); int n = br.ReadInt32(), nf = br.ReadInt32();
                var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n];
                for (int i = 0; i < n; i++) pos[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                for (int i = 0; i < n; i++) nrm[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                for (int i = 0; i < n; i++) uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                var jb = br.ReadBytes(n * 4); for (int i = 0; i < jb.Length; i++) jb[i] = (byte)boneMap[jb[i]]; var wb = br.ReadBytes(n * 4); var alphaB = br.ReadBytes(n); var idx = new int[nf * 3];
                for (int i = 0; i < idx.Length; i++) idx[i] = br.ReadInt32();
                var mm = MM[tmpl[name]]; var me = mm.Meshes.First(x => x.Lod == 0);
                if (a.Length > 4 && a[4] == "restream") { var mpR = new Dictionary<Guid, Guid>(); mpR[mm.Guid] = Guid.NewGuid(); foreach (var mx in mm.Meshes) mpR[mx.Guid] = Guid.NewGuid(); foreach (var sg in mm.TypelessDataSegments) if (mpR.ContainsKey(sg.OwnerGuid)) sg.OwnerGuid = mpR[sg.OwnerGuid]; mm.Guid = mpR[mm.Guid]; foreach (var mx in mm.Meshes) mx.Guid = mpR[mx.Guid]; me.Name = name; mm.Name = name;
                    var BF2 = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; var fU2 = typeof(AbstractExternalLoader).GetField("_unknownUlong", BF2); var fN2 = typeof(AbstractExternalLoader).GetField("_unknownUint", BF2);
                    var oldL = (ExternalLoader<VertexStreamData>)me.VertexStream; var nl = new ExternalLoader<VertexStreamData>(oldL.Data); nl.OwnerGuid = me.Guid; nl.UserData[VertexStreamData.KEY_HAS_QTANGENT] = true; fU2.SetValue(nl, fU2.GetValue(oldL)); fN2.SetValue(nl, fN2.GetValue(oldL));
                    var ix = mm.TypelessDataSegments.IndexOf(oldL); mm.TypelessDataSegments[ix] = nl; me.VertexStream = nl; pk.Items.Add(mm); Console.WriteLine("restream " + name); continue; }
                if (a.Length > 4 && a[4] == "clone") { var mp = new Dictionary<Guid, Guid>(); var oldMm = mm.Guid; mm.Guid = Guid.NewGuid(); mp[oldMm] = mm.Guid; foreach (var mx in mm.Meshes) { var og = mx.Guid; mx.Guid = Guid.NewGuid(); mp[og] = mx.Guid; } me.Name = name; mm.Name = name; foreach (var sg in mm.TypelessDataSegments) if (mp.ContainsKey(sg.OwnerGuid)) sg.OwnerGuid = mp[sg.OwnerGuid]; pk.Items.Add(mm); Console.WriteLine("clone " + name); continue; }
                if (a.Length > 4 && a[4] == "echo") { var tv = me.VertexStream.Data; n = tv.Positions.Length; idx = (int[])tv.Indices.Clone(); nf = idx.Length / 3; pos = (Vector3[])tv.Positions.Clone(); nrm = (Vector3[])tv.Normals.Clone(); uv = (Vector2[])tv.Uv1.Clone(); jb = new byte[n * 4]; wb = new byte[n * 4]; for (int i = 0; i < n; i++) { var bi = tv.BoneIndices[i]; var bw = tv.BoneWeights[i]; jb[i*4]=bi.B1; jb[i*4+1]=bi.B2; jb[i*4+2]=bi.B3; jb[i*4+3]=bi.B4; wb[i*4]=bw.W1; wb[i*4+1]=bw.W2; wb[i*4+2]=bw.W3; wb[i*4+3]=bw.W4; } }
                var allLods = mm.Meshes.ToList(); if (cirithMat != Guid.Empty) foreach (var mx0 in allLods) mx0.Material = new AssetDependence<Material>(cirithMat); var mp2 = new Dictionary<Guid, Guid>(); mp2[mm.Guid] = Guid.NewGuid(); foreach (var mx in allLods) mp2[mx.Guid] = Guid.NewGuid(); foreach (var sg in mm.TypelessDataSegments) if (mp2.ContainsKey(sg.OwnerGuid)) sg.OwnerGuid = mp2[sg.OwnerGuid]; mm.Guid = mp2[mm.Guid]; foreach (var mx in allLods) { mx.Guid = mp2[mx.Guid]; } me.Name = name; mm.Name = name;
                var oldStream = me.VertexStream; var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; var fU = typeof(AbstractExternalLoader).GetField("_unknownUlong", BF);
                var ulS = new Dictionary<Guid, ulong>(); var ulE = new Dictionary<Guid, ulong>();
                foreach (var sg in mm.TypelessDataSegments) { if (sg is ExternalLoader<VertexStreamData>) ulS[sg.OwnerGuid] = (ulong)fU.GetValue(sg); else if (sg is ExternalLoader<MeshEditData>) ulE[sg.OwnerGuid] = (ulong)fU.GetValue(sg); }
                if (Environment.GetEnvironmentVariable("KEEPLINKS") != "1") { mm.ClothMetamesh = Guid.Empty; mm.Original = Guid.Empty; mm.Variations.Clear(); mm.EditmodeMisc = null; }
                
                var tan = new Vector4[n]; var t3 = new Vector3[n]; var b3 = new Vector3[n];
                for (int f = 0; f < nf; f++)
                {
                    int i0 = idx[f * 3], i1 = idx[f * 3 + 1], i2 = idx[f * 3 + 2];
                    var e1 = pos[i1] - pos[i0]; var e2 = pos[i2] - pos[i0]; var d1 = uv[i1] - uv[i0]; var d2 = uv[i2] - uv[i0];
                    float r = d1.X * d2.Y - d2.X * d1.Y; if (Math.Abs(r) < 1e-12f) continue; r = 1f / r;
                    var T = (e1 * d2.Y - e2 * d1.Y) * r; var B = (e2 * d1.X - e1 * d2.X) * r;
                    foreach (var k in new[] { i0, i1, i2 }) { t3[k] += T; b3[k] += B; }
                }
                for (int i = 0; i < n; i++)
                {
                    var N = Vector3.Normalize(nrm[i]); var T = t3[i] - N * Vector3.Dot(N, t3[i]);
                    if (T.LengthSquared() < 1e-12f) { T = Math.Abs(N.X) < 0.9f ? Vector3.Cross(N, Vector3.UnitX) : Vector3.Cross(N, Vector3.UnitY); }
                    T = Vector3.Normalize(T); float w = Vector3.Dot(Vector3.Cross(N, T), b3[i]) < 0 ? -1f : 1f; nrm[i] = N; tan[i] = new Vector4(T, w);
                }
                if (a.Length > 4 && a[4] == "echo" && a.Length > 5 && a[5] == "vtan") { var tv = me.VertexStream.Data; for (int i = 0; i < n; i++) tan[i] = tv.Tangents[i]; }
                var vs = new VertexStreamData { Indices = idx, Positions = pos, UnknownAnotherPositions = (Vector3[])pos.Clone(), Normals = nrm, Tangents = tan, Uv1 = uv, Uv2 = (Vector2[])uv.Clone() };
                vs.Colors1 = Enumerable.Range(0, n).Select(i => new AbstractMeshData.Color { R = 255, G = 255, B = 255, A = alphaB[i] }).ToArray(); vs.Colors2 = new AbstractMeshData.Color[n]; vs.Uv2 = Enumerable.Repeat(new Vector2(0f, 1f), n).ToArray();
                vs.BoneIndices = new VertexStreamData.BoneIndex[n]; vs.BoneWeights = new VertexStreamData.BoneWeight[n];
                vs.CompressedNormals = new VertexStreamData.X11Y11Z10[n]; vs.CompressedPositions = new VertexStreamData.Half4[n]; vs.CompressedTangents = new VertexStreamData.X10Y11Z10W1[n]; vs.TangentTransform = new VertexStreamData.SnormShort4[n];
                var used = new HashSet<int>();
                for (int i = 0; i < n; i++)
                {
                    vs.BoneIndices[i] = new VertexStreamData.BoneIndex { B1 = jb[i * 4], B2 = jb[i * 4 + 1], B3 = jb[i * 4 + 2], B4 = jb[i * 4 + 3] };
                    vs.BoneWeights[i] = new VertexStreamData.BoneWeight { W1 = wb[i * 4], W2 = wb[i * 4 + 1], W3 = wb[i * 4 + 2], W4 = wb[i * 4 + 3] };
                    for (int k = 0; k < 4; k++) if (wb[i * 4 + k] > 0) used.Add(jb[i * 4 + k]);
                    vs.CompressedPositions[i] = new VertexStreamData.Half4 { X = (SystemHalf.Half)pos[i].X, Y = (SystemHalf.Half)pos[i].Y, Z = (SystemHalf.Half)pos[i].Z, W = (SystemHalf.Half)1f };
                    var cn = new VertexStreamData.X11Y11Z10(); cn.X = nrm[i].X; cn.Y = nrm[i].Y; cn.Z = nrm[i].Z; vs.CompressedNormals[i] = cn;
                    var ct = new VertexStreamData.X10Y11Z10W1(); ct.X = tan[i].X; ct.Y = tan[i].Y; ct.Z = tan[i].Z; ct.RawW = tan[i].W < 0 ? 1 : 0; vs.CompressedTangents[i] = ct;
                    vs.TangentTransform[i] = QT(nrm[i], new Vector3(tan[i].X, tan[i].Y, tan[i].Z), tan[i].W);
                }
                if (a.Length > 4 && a[4] == "echo") { var tv = MM[tmpl[name]].Meshes.First(x => x.Lod == 0).VertexStream.Data; Func<IEnumerable<double>, double> mx2 = d => d.Max();
                    Console.WriteLine($"  echo diff {name}: col1 {Enumerable.Range(0,n).Count(i => !tv.Colors1[i].Equals(vs.Colors1[i]))} col2 {Enumerable.Range(0,n).Count(i => !tv.Colors2[i].Equals(vs.Colors2[i]))} uv2 {mx2(Enumerable.Range(0,n).Select(i => (double)(tv.Uv2[i]-vs.Uv2[i]).Length()))} cpos {Enumerable.Range(0,n).Count(i => tv.CompressedPositions[i].ToString() != vs.CompressedPositions[i].ToString())} cnrm {Enumerable.Range(0,n).Count(i => tv.CompressedNormals[i].RawData != vs.CompressedNormals[i].RawData)} ctan {Enumerable.Range(0,n).Count(i => tv.CompressedTangents[i].RawData != vs.CompressedTangents[i].RawData)} tan {mx2(Enumerable.Range(0,n).Select(i => (double)(tv.Tangents[i]-vs.Tangents[i]).Length()))} tt {mx2(Enumerable.Range(0,n).Select(i => (double)Math.Abs(tv.TangentTransform[i].RawX-vs.TangentTransform[i].RawX)))} idx {tv.Indices.SequenceEqual(vs.Indices)} samePos {mx2(Enumerable.Range(0,n).Select(i => (double)(tv.UnknownAnotherPositions[i]-vs.UnknownAnotherPositions[i]).Length()))}"); }
                if (a.Length > 4 && a[4] == "echo")
                {
                    var BFx = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; var tld = (ExternalLoader<VertexStreamData>)me.VertexStream; var fileX = (FileInfo)typeof(AbstractExternalLoader).GetField("_file", BFx).GetValue(tld);
                    byte[] rawX; using (var brx = new BinaryReader(fileX.OpenRead())) rawX = (byte[])typeof(ExternalLoader<VertexStreamData>).GetMethod("GetRawData", BFx).Invoke(tld, new object[] { brx });
                    byte[] mineX; using (var msx = new MemoryStream()) { using (var bwx = new BinaryWriter(msx)) { var ud = new Dictionary<object, object>(); ud[VertexStreamData.KEY_HAS_QTANGENT] = true; vs.Colors1 = (AbstractMeshData.Color[])tld.Data.Colors1.Clone(); vs.Colors2 = (AbstractMeshData.Color[])tld.Data.Colors2.Clone(); vs.Uv2 = (Vector2[])tld.Data.Uv2.Clone(); vs.WriteData(bwx, ud); } mineX = msx.ToArray(); }
                    int idxB = tld.Data.Indices.Length * 2 + 4; string[] nm = { "col1", "col2", "uv1", "uv2", "pos", "pos2", "nrm", "tan", "bw", "bi", "cnrm", "cpos", "ctan", "tt" };
                    var sb = new System.Text.StringBuilder(); for (int k = 0; k < 14; k++) { ulong off = BitConverter.ToUInt64(rawX, idxB + k * 16), sz = BitConverter.ToUInt64(rawX, idxB + k * 16 + 8); int d = 0; for (ulong q = 0; q < sz; q++) if (idxB + (int)off + (int)q < mineX.Length && rawX[idxB + (int)off + (int)q] != mineX[idxB + (int)off + (int)q]) d++; sb.Append($"{nm[k]}:{d}/{sz} "); }
                    Console.WriteLine($"   echo bytes {name}: raw {rawX.Length} mine {mineX.Length} idxDiff {Enumerable.Range(0, idxB).Count(q => rawX[q] != mineX[q])} | {sb}");
                }
                { var cp = (Environment.GetEnvironmentVariable("ECHOCOPY") ?? "").Split(','); var tv9 = me.VertexStream.Data;
                  if (cp.Contains("cpos")) vs.CompressedPositions = (VertexStreamData.Half4[])tv9.CompressedPositions.Clone(); if (cp.Contains("nrm")) vs.Normals = (Vector3[])tv9.Normals.Clone(); if (cp.Contains("tt")) vs.TangentTransform = (VertexStreamData.SnormShort4[])tv9.TangentTransform.Clone(); }
                bool faithful = a.Length > 5 && a[5] == "vtan";
                if (faithful) { var tv = me.VertexStream.Data; vs.Colors1 = (AbstractMeshData.Color[])tv.Colors1.Clone(); vs.Colors2 = (AbstractMeshData.Color[])tv.Colors2.Clone(); vs.Uv2 = (Vector2[])tv.Uv2.Clone(); }
                var origPosCount = me.PositionCount; var origSkin = me.SkinDataSize; var origUnk2 = me.UnknownInt2; var origBox = me.BoundingBox;
                me.VertexStream = new ExternalLoader<VertexStreamData>(vs); me.VertexStream.OwnerGuid = me.Guid; me.VertexStream.UserData[VertexStreamData.KEY_HAS_QTANGENT] = true; typeof(AbstractExternalLoader).GetField("_unknownUint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(me.VertexStream, 1u); if (ulS.TryGetValue(me.Guid, out var us0)) fU.SetValue(me.VertexStream, us0); { int ix0 = mm.TypelessDataSegments.IndexOf(oldStream); mm.TypelessDataSegments[ix0] = me.VertexStream; }
                var uniq = pos.Select(p => (MathF.Round(p.X * 1e4f), MathF.Round(p.Y * 1e4f), MathF.Round(p.Z * 1e4f))).Distinct().Count();
                void Set(string prop, int v) => typeof(Mesh).GetProperty(prop).GetSetMethod(true).Invoke(me, new object[] { v });
                Set("VertexCount", n); Set("FaceCount", nf); Set("PositionCount", uniq); Set("SkinDataSize", uniq); Set("VertexKeyCount", 0);
                me.UnknownInt2 = used.Count;
                var mn = new Vector3(float.MaxValue); var mxv = new Vector3(float.MinValue); foreach (var p in pos) { mn = Vector3.Min(mn, p); mxv = Vector3.Max(mxv, p); }
                me.BoundingBox = new BoundingBox { Min = mn, Max = mxv };
                foreach (var m in Environment.GetEnvironmentVariable("LOD0ONLY") == "1" ? new List<Mesh> { me } : allLods)
                {
                    if (m != me)
                    {
                        mm.TypelessDataSegments.RemoveAll(sg => sg is ExternalLoader<VertexStreamData> && sg.OwnerGuid == m.Guid);
                        var l2 = new ExternalLoader<VertexStreamData>(vs); l2.OwnerGuid = m.Guid; l2.UserData[VertexStreamData.KEY_HAS_QTANGENT] = true;
                        typeof(AbstractExternalLoader).GetField("_unknownUint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(l2, 1u);
                        if (ulS.TryGetValue(m.Guid, out var us1)) fU.SetValue(l2, us1); m.VertexStream = l2; mm.TypelessDataSegments.Add(l2);
                        void SetM(string prop, int v) => typeof(Mesh).GetProperty(prop).GetSetMethod(true).Invoke(m, new object[] { v });
                        SetM("VertexCount", n); SetM("FaceCount", nf); SetM("PositionCount", uniq); SetM("SkinDataSize", uniq); SetM("VertexKeyCount", 0); m.UnknownInt2 = used.Count; m.BoundingBox = me.BoundingBox;
                    }
                    // consistent MeshEditData (unique positions, per-position bones, same vertex/face order as the stream)
                    var ed = new MeshEditData(); var pkey = new Dictionary<(float, float, float), int>(); var plist = new List<Vector4>(); var blist = new List<MeshEditData.Bone>();
                    var evs = new MeshEditData.Vertex[n];
                    for (int i = 0; i < n; i++)
                    {
                        var key = (MathF.Round(pos[i].X * 1e4f), MathF.Round(pos[i].Y * 1e4f), MathF.Round(pos[i].Z * 1e4f));
                        if (!pkey.TryGetValue(key, out var pi))
                        {
                            pi = plist.Count; pkey[key] = pi; plist.Add(new Vector4(pos[i], 1f));
                            blist.Add(new MeshEditData.Bone { W0 = wb[i * 4] / 255f, W1 = wb[i * 4 + 1] / 255f, W2 = wb[i * 4 + 2] / 255f, W3 = wb[i * 4 + 3] / 255f, B0 = jb[i * 4], B1 = jb[i * 4 + 1], B2 = jb[i * 4 + 2], B3 = jb[i * 4 + 3] });
                        }
                        var Nv = nrm[i]; var Tv = new Vector3(tan[i].X, tan[i].Y, tan[i].Z); var Bv = Vector3.Cross(Nv, Tv) * tan[i].W;
                        evs[i] = new MeshEditData.Vertex { PositionIndex = (uint)pi, Normal = new Vector4(Nv, 1f), Tangent = new Vector4(Tv, 1f), Binormal = new Vector4(Bv, 1f), Padding = new Vector4(Nv, 1f), Uv = uv[i], SecondUv = Vector2.Zero, Color = new AbstractMeshData.Color { R = 255, G = 255, B = 255, A = 255 }, SecondColor = new AbstractMeshData.Color { R = 255, G = 255, B = 255, A = 255 } };
                    }
                    ed.Positions = plist.ToArray(); ed.Vertices = evs; ed.Bones = blist.ToArray();
                    ed.Faces = new MeshEditData.Face[nf]; for (int f = 0; f < nf; f++) ed.Faces[f] = new MeshEditData.Face { V0 = idx[f * 3], V1 = idx[f * 3 + 1], V2 = idx[f * 3 + 2] };
                    mm.TypelessDataSegments.RemoveAll(sg => sg is ExternalLoader<MeshEditData> && sg.OwnerGuid == m.Guid);
                    var el = new ExternalLoader<MeshEditData>(ed); el.OwnerGuid = m.Guid; if (ulE.TryGetValue(m.Guid, out var ue1)) fU.SetValue(el, ue1); m.EditData = el; mm.TypelessDataSegments.Add(el);
                }
                if (alphaB.Min() < 255 && Environment.GetEnvironmentVariable("NOCLOTH") != "1")
                {
                    // in-game cloth: flag + per-mesh simulation parameters + vertex alpha = free weight (see transplant/rigfit.py CLOTH)
                    var cm = me.ClothingMaterial; if (!me.Flags.Contains("uses_cloth_simulation")) me.Flags.Add("uses_cloth_simulation");
                    bool pony = name.Contains("head");
                    cm.Name = ""; cm.StretchingStiffness = pony ? 1.0f : 0.6f; cm.AnchorStiffness = pony ? 0.0f : 0.30f; cm.BendingStiffness = pony ? 0.15f : 0.35f; cm.ShearingStiffness = pony ? 0.6f : 0.5f;
                    cm.Damping = pony ? 0.30f : 0.10f; cm.Gravity = pony ? 9.81f : 3.0f; cm.LinearInertia = 1.0f; cm.MaxLinearVelocity = -1f; cm.LinearVelocityMultiplier = 1.0f; cm.Wind = pony ? 0.3f : 0.0f; cm.AirDragMultiplier = 0.5f;
                    Console.WriteLine($"   cloth on {name}: alpha {alphaB.Min()}..{alphaB.Max()}");
                }
                if (faithful) { Set("PositionCount", origPosCount); Set("SkinDataSize", origSkin); me.UnknownInt2 = origUnk2; me.BoundingBox = origBox; }
                pk.Items.Add(mm);
                { var tv0 = MM[tmpl[name]].Meshes.First(x => x.Lod == 0).VertexStream.Data; var vset = new SortedSet<int>(); for (int i = 0; i < tv0.BoneIndices.Length; i++) { var bi = tv0.BoneIndices[i]; var bw = tv0.BoneWeights[i]; if (bw.W1 > 0) vset.Add(bi.B1); if (bw.W2 > 0) vset.Add(bi.B2); if (bw.W3 > 0) vset.Add(bi.B3); if (bw.W4 > 0) vset.Add(bi.B4); }
                  Console.WriteLine($"   vanilla bones: {string.Join(",", vset)}  | mine: {string.Join(",", used.OrderBy(x => x))}"); }
                Console.WriteLine($"{name}: {n} verts {nf} faces, uniq pos {uniq}, bones used {used.Count}, template {tmpl[name]} material {M.GetAsset(me.Material.Guid)?.Name}");
            }
            pk.Save(a[3], 2);
            var chk = new AssetPackage(a[3], true, true);
            Console.WriteLine($"wrote {a[3]} ({new FileInfo(a[3]).Length} bytes); re-loaded {chk.Items.Count} items");
            return;
        }
        if (a[1] == "seginfo")
        {
            var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            foreach (var n in a.Skip(2)) { var mm = MM[n]; Console.WriteLine($"{n}: guid={mm.Guid} deps={mm.UnknownDependences.Count} version={mm.Version} mesh.guid={mm.Meshes[0].Guid}");
                foreach (var sg in mm.TypelessDataSegments) { var ty = typeof(AbstractExternalLoader); Console.WriteLine($"   seg {sg.GetType().GetGenericArguments()[0].Name} owner={sg.OwnerGuid} type={sg.TypeGuid} ulong={ty.GetField("_unknownUlong", bf).GetValue(sg)} uint={ty.GetField("_unknownUint", bf).GetValue(sg)} fmt={ty.GetField("_storageFormat", bf).GetValue(sg)}"); }
                foreach (var d in mm.UnknownDependences) Console.WriteLine($"   dep {M.GetAsset(d.UnknownGuid1)?.Name} | {M.GetAsset(d.UnknownGuid2)?.Name} | {M.GetAsset(d.UnknownGuid3)?.Name}"); }
            return;
        }
        if (a[1] == "editdump")
        {
            var me0 = MM[a[2]].Meshes.First(x => x.Lod == 0); var ed = me0.EditData.Data;
            Console.WriteLine($"pos={ed.Positions.Length} unused={ed.UnusedVec3.Length} verts={ed.Vertices.Length} faces={ed.Faces.Length} bones={ed.Bones.Length} smg={ed.FaceSmoothingGroupMasks.Length} morph={ed.MorphFrames.Count}");
            for (int i = 0; i < 3; i++) Console.WriteLine($" pos[{i}]={ed.Positions[i]}");
            for (int i = 0; i < 3; i++) { var v = ed.Vertices[i]; Console.WriteLine($" vert[{i}] posIdx={v.PositionIndex} N={v.Normal} T={v.Tangent} B={v.Binormal} P={v.Padding} uv={v.Uv} uv2={v.SecondUv} c={v.Color} c2={v.SecondColor}"); }
            for (int i = 0; i < 3; i++) { var f = ed.Faces[i]; Console.WriteLine($" face[{i}]={f.V0},{f.V1},{f.V2}"); }
            for (int i = 0; i < 4; i++) { var b = ed.Bones[i]; Console.WriteLine($" bone[{i}] w={b.W0},{b.W1},{b.W2},{b.W3} b={b.B0},{b.B1},{b.B2},{b.B3}"); }
            Console.WriteLine(" smg[0..3]=" + string.Join(",", ed.FaceSmoothingGroupMasks.Take(4)));
            var vs0 = me0.VertexStream.Data; Console.WriteLine($" stream idx0..5={string.Join(",", vs0.Indices.Take(6))} pos0={vs0.Positions[0]} posIdx order check: vertex0 pos={ed.Positions[ed.Vertices[0].PositionIndex]}");
            return;
        }
        if (a[1] == "rawstream")
        {
            var me0 = MM[a[2]].Meshes.First(x => x.Lod == 0); var ld = (ExternalLoader<VertexStreamData>)me0.VertexStream;
            var BF = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; var t = typeof(ExternalLoader<VertexStreamData>); var tb = typeof(AbstractExternalLoader);
            var file = (FileInfo)tb.GetField("_file", BF).GetValue(ld);
            byte[] raw; using (var br = new BinaryReader(file.OpenRead())) raw = (byte[])t.GetMethod("GetRawData", BF).Invoke(ld, new object[] { br });
            byte[] mine; using (var ms = new MemoryStream()) { using (var bw = new BinaryWriter(ms)) { var ud = new Dictionary<object, object>(); ud[VertexStreamData.KEY_HAS_QTANGENT] = true; ld.Data.WriteData(bw, ud); } mine = ms.ToArray(); }
            Console.WriteLine($"raw {raw.Length} bytes, reserialized {mine.Length}");
            int diff = 0, first = -1; for (int i = 0; i < Math.Min(raw.Length, mine.Length); i++) if (raw[i] != mine[i]) { diff++; if (first < 0) first = i; }
            Console.WriteLine($"byte diffs {diff} first at {first}");
            var n = ld.Data.Positions.Length; int idxBytes = ld.Data.Indices.Length * 2 + 4;
            Console.WriteLine("raw table (ulong pairs after indices): " + string.Join(" ", Enumerable.Range(0, 28).Select(i => BitConverter.ToUInt64(raw, idxBytes + i * 8))));
            Console.WriteLine("mine table:                             " + string.Join(" ", Enumerable.Range(0, 28).Select(i => BitConverter.ToUInt64(mine, idxBytes + i * 8))));
            return;
        }
        if (a[1] == "pkgdiff")
        {
            var A = new AssetPackage(a[2], true, true); var B = new AssetPackage(a[3], true, true); var BFd = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var ma in A.Items.OfType<Metamesh>()) { var mb = B.Items.OfType<Metamesh>().First(x => x.Name == ma.Name); Console.WriteLine("== " + ma.Name);
                void CmpObj(object x, object y, string pre) { foreach (var pr in x.GetType().GetProperties()) { if (pr.GetIndexParameters().Length > 0) continue; object vx, vy; try { vx = pr.GetValue(x); vy = pr.GetValue(y); } catch { continue; } if (vx is System.Collections.IEnumerable && !(vx is string)) { var lx = ((System.Collections.IEnumerable)vx).Cast<object>().Select(o => o?.ToString()).ToList(); var ly = ((System.Collections.IEnumerable)vy).Cast<object>().Select(o => o?.ToString()).ToList(); if (!lx.SequenceEqual(ly) && pr.Name != "Meshes" && pr.Name != "TypelessDataSegments") Console.WriteLine($"  {pre}{pr.Name}: [{string.Join(",", lx.Take(6))}] vs [{string.Join(",", ly.Take(6))}]"); continue; } if (pr.PropertyType.IsPrimitive || pr.PropertyType.IsEnum || vx is string || vx is Guid || vx is Vector4 || vx is Vector3) { if (!Equals(vx, vy) && pr.Name != "Guid") Console.WriteLine($"  {pre}{pr.Name}: {vx} vs {vy}"); } } }
                CmpObj(ma, mb, "mm."); for (int i = 0; i < Math.Min(ma.Meshes.Count, mb.Meshes.Count); i++) CmpObj(ma.Meshes[i], mb.Meshes[i], $"mesh{i}.");
                Console.WriteLine($"  meshes {ma.Meshes.Count} vs {mb.Meshes.Count}; segs {ma.TypelessDataSegments.Count} vs {mb.TypelessDataSegments.Count}");
                for (int i = 0; i < Math.Min(ma.TypelessDataSegments.Count, mb.TypelessDataSegments.Count); i++) { var sa = ma.TypelessDataSegments[i]; var sb = mb.TypelessDataSegments[i]; Func<AbstractExternalLoader, string> d = s => s.GetType().GetGenericArguments()[0].Name + " owner#" + ma.Meshes.Concat(mb.Meshes).ToList().FindIndex(m => m.Guid == s.OwnerGuid) % Math.Max(1, ma.Meshes.Count) + " u64=" + typeof(AbstractExternalLoader).GetField("_unknownUlong", BFd).GetValue(s) + " u32=" + typeof(AbstractExternalLoader).GetField("_unknownUint", BFd).GetValue(s) + " fmt=" + typeof(AbstractExternalLoader).GetField("_storageFormat", BFd).GetValue(s); var x1 = d(sa); var x2 = d(sb); if (x1 != x2) Console.WriteLine($"  seg{i}: {x1} | {x2}"); }
            }
            return;
        }
        if (a[1] == "matinfo")
        {
            var me0 = MM[a[2]].Meshes.First(x => x.Lod == 0); var mat = M.GetAsset(me0.Material.Guid) as Material;
            Console.WriteLine($"material {mat.Name} guid={mat.Guid} shader={M.GetAsset(mat.Shader.Guid)?.Name} blend={mat.BlendMode} flags=[{string.Join(",", mat.Flags)}] vlayout=[{string.Join(",", mat.VertexLayoutFlags)}] smflags=[{string.Join(",", mat.ShaderMaterialFlags)}] alphaTest={mat.AlphaTest} deps={mat.UnknownDependences.Count} segs={mat.TypelessDataSegments.Count}");
            foreach (var kv in mat.Textures) { var t = M.GetAsset(kv.Value.Guid) as Texture; Console.WriteLine($"  slot {kv.Key}: {t?.Name} {t?.Width}x{t?.Height} fmt={t?.Format} mips={t?.MipmapCount} arr={t?.ArrayCount} hasPix={t?.HasPixelData} segs={t?.TypelessDataSegments.Count} deps={t?.UnknownDependences.Count} src='{t?.Source}' flags=[{string.Join(",", t?.Flags ?? new List<string>())}] sysflags=[{string.Join(",", t?.SystemFlags ?? new List<string>())}]"); }
            return;
        }
        if (a[1] == "texfind")
        {
            foreach (var t in M.LoadedAssets.OfType<Texture>().Where(t => a.Skip(2).Any(k => t.Name.Contains(k)) && t.Width <= 64)) Console.WriteLine($"{t.Name} {t.Width}x{t.Height} {t.Format} mips={t.MipmapCount} guid={t.Guid}");
            return;
        }
        if (a[1] == "matfind")
        {
            foreach (var mat in M.LoadedAssets.OfType<Material>().Where(m => m.AlphaTest > 0 && M.GetAsset(m.Shader.Guid)?.Name == "pbr_shading" && m.VertexLayoutFlags.Contains("skinning")).Take(25))
                Console.WriteLine($"{mat.Name} alphaTest={mat.AlphaTest} blend={mat.BlendMode} flags=[{string.Join(",", mat.Flags)}] vl=[{string.Join(",", mat.VertexLayoutFlags)}] sm=[{string.Join(",", mat.ShaderMaterialFlags)}] tex={string.Join(",", mat.Textures.Select(kv => kv.Key + ":" + (M.GetAsset(kv.Value.Guid)?.Name) ))}");
            return;
        }
        if (a[1] == "blobdump")
        {
            var BFb = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var mm = MM[a[2]]; var want = Guid.Parse(a[3]);
            foreach (var sg in mm.TypelessDataSegments.Where(s => s.TypeGuid == want))
            {
                var fileB = (FileInfo)typeof(AbstractExternalLoader).GetField("_file", BFb).GetValue(sg); byte[] raw;
                using (var brb = new BinaryReader(fileB.OpenRead())) raw = (byte[])sg.GetType().GetMethod("GetRawData", BFb).Invoke(sg, new object[] { brb });
                File.WriteAllBytes(a[4], raw); Console.WriteLine($"{a[2]} blob {want}: {raw.Length} bytes -> {a[4]}");
            }
            return;
        }
        if (a[1] == "packinfo")
        {
            var pk2 = new AssetPackage(a[2], true, true);
            foreach (var mm2 in pk2.Items.OfType<Metamesh>()) foreach (var me2 in mm2.Meshes) Console.WriteLine($"{mm2.Name}/{me2.Name} verts={me2.VertexCount} stream={me2.VertexStream?.Data.Positions.Length} first={me2.VertexStream?.Data.Positions[0]}");
            return;
        }
        if (a[1] == "list") { foreach (var n in MM.Keys.OrderBy(x => x)) Console.WriteLine(n); return; }
        var cfg = JsonDocument.Parse(File.ReadAllText(a[2])).RootElement;
        var outDir = a[3];
        Directory.CreateDirectory(outDir);
        foreach (var sc in cfg.GetProperty("scenes").EnumerateArray())
        {
            string id = sc.GetProperty("id").GetString();
            try
            {
                var g = new G(Skel);
                g.Build(sc);
                var path = Path.Combine(outDir, id + ".glb");
                g.Save(path);
                Console.WriteLine($"OK {id} {new FileInfo(path).Length / 1024} KB parts={g.PartCount} anims={g.AnimCount}");
            }
            catch (Exception e) { Console.WriteLine("FAIL " + id + " " + e); }
        }
    }


    // ---- fallback: low-res mip tail stored as an untyped segment when the full pixel data is not in the packs
    static byte[] RawSegment(AbstractExternalLoader g)
    {
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var t = typeof(AbstractExternalLoader);
        var file = (FileInfo)t.GetField("_file", bf).GetValue(g);
        ulong off = (ulong)t.GetField("_offset", bf).GetValue(g), actual = (ulong)t.GetField("_actualSize", bf).GetValue(g), stored = (ulong)t.GetField("_storageSize", bf).GetValue(g);
        string fmt = t.GetField("_storageFormat", bf).GetValue(g).ToString();
        using var fs = file.OpenRead(); fs.Seek((long)off, SeekOrigin.Begin);
        var buf = new byte[(int)stored]; int rd = 0; while (rd < buf.Length) { int n = fs.Read(buf, rd, buf.Length - rd); if (n <= 0) break; rd += n; }
        if (fmt == "LZ4HC") buf = LZ4.LZ4Codec.Decode(buf, 0, buf.Length, (int)actual);
        return buf;
    }
    static long MipBytes(TextureFormat f, int w, int h)
    {
        int block = 0;
        switch (f) { case TextureFormat.DXT1: case TextureFormat.BC4: block = 8; break; case TextureFormat.DXT2: case TextureFormat.DXT3: case TextureFormat.DXT4: case TextureFormat.DXT5: case TextureFormat.BC5: case TextureFormat.BC6H_UF16: case TextureFormat.BC7: block = 16; break; }
        if (block > 0) return (long)Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * block;
        return (long)w * h * (f.GetBitsPerPixel() / 8);
    }
    class RowWriter : TextureUtil.PipelineWriter
    {
        public List<byte[]> Rows = new List<byte[]>();
        public RowWriter(int w) { width = w; }
        public override void WriteLine(byte[] rgba8, bool normalized) { Rows.Add((byte[])rgba8.Clone()); }
        public override void WriteLine(ushort[] rgba16, bool normalized) { var r = new byte[rgba16.Length]; for (int i = 0; i < r.Length; i++) r[i] = (byte)(rgba16[i] >> 8); Rows.Add(r); }
        public override void WriteLine(uint[] rgba32, bool normalized) { var r = new byte[rgba32.Length]; for (int i = 0; i < r.Length; i++) r[i] = (byte)(rgba32[i] >> 24); Rows.Add(r); }
        public override void WriteLine(float[] depth, byte[] stencil) { }
        public override void WriteLine(float[] rgba32f) { var r = new byte[rgba32f.Length]; for (int i = 0; i < r.Length; i++) r[i] = (byte)Math.Clamp(rgba32f[i] * 255f, 0, 255); Rows.Add(r); }
    }
    static uint[] crcTab;
    static uint Crc(byte[] d, int off, int len, uint c = 0xFFFFFFFF)
    {
        if (crcTab == null) { crcTab = new uint[256]; for (uint n = 0; n < 256; n++) { uint x = n; for (int k = 0; k < 8; k++) x = (x & 1) != 0 ? 0xEDB88320 ^ (x >> 1) : x >> 1; crcTab[n] = x; } }
        for (int i = 0; i < len; i++) c = crcTab[(c ^ d[off + i]) & 0xFF] ^ (c >> 8);
        return c;
    }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = BitConverter.GetBytes((uint)data.Length); Array.Reverse(len); s.Write(len, 0, 4);
        var td = new byte[4 + data.Length]; Encoding.ASCII.GetBytes(type).CopyTo(td, 0); data.CopyTo(td, 4);
        s.Write(td, 0, td.Length);
        var crc = BitConverter.GetBytes(Crc(td, 0, td.Length) ^ 0xFFFFFFFF); Array.Reverse(crc); s.Write(crc, 0, 4);
    }
    static byte[] EncodePng(List<byte[]> rows, int w, int h)
    {
        var raw = new MemoryStream();
        foreach (var r in rows) { raw.WriteByte(0); var line = new byte[w * 4]; for (int x = 0; x < w; x++) { int src = r.Length >= w * 4 ? x * 4 : x * Math.Max(1, r.Length / w); for (int c = 0; c < 4; c++) line[x * 4 + c] = r.Length >= w * 4 ? r[src + c] : (c < 3 ? r[Math.Min(r.Length - 1, src)] : (byte)255); } raw.Write(line, 0, line.Length); }
        var comp = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(comp, System.IO.Compression.CompressionLevel.Fastest, true)) { var rb = raw.ToArray(); z.Write(rb, 0, rb.Length); }
        var o = new MemoryStream(); o.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var ih = new byte[13]; var wb = BitConverter.GetBytes(w); var hb = BitConverter.GetBytes(h); Array.Reverse(wb); Array.Reverse(hb); wb.CopyTo(ih, 0); hb.CopyTo(ih, 4); ih[8] = 8; ih[9] = 6;
        Chunk(o, "IHDR", ih); Chunk(o, "IDAT", comp.ToArray()); Chunk(o, "IEND", new byte[0]);
        return o.ToArray();
    }
    public static byte[] FallbackPng(Texture t)
    {
        var seg = t.TypelessDataSegments.FirstOrDefault(); if (seg == null) return null;
        var bytes = RawSegment(seg);
        for (int w = (int)Math.Min(t.Width, 4096); w >= 4; w /= 2)
        {
            int h = (int)Math.Max(1, (long)w * t.Height / t.Width);
            long total = 0; int mw = w, mh = h;
            while (true) { total += MipBytes(t.Format, mw, mh); if (mw == 1 && mh == 1) break; mw = Math.Max(1, mw / 2); mh = Math.Max(1, mh / 2); }
            if (total != bytes.Length) continue;
            var top = new byte[MipBytes(t.Format, w, h)]; Array.Copy(bytes, top, top.Length);
            if (!t.Format.IsSupported())
            {
                // dump as DDS (DX10 header) so Pillow can decode it afterwards (BC7)
                Directory.CreateDirectory(TexCache);
                using var ms = new MemoryStream(); using var bw = new BinaryWriter(ms);
                bw.Write(0x20534444); bw.Write(124); bw.Write(0x000A1007); bw.Write(h); bw.Write(w); bw.Write(top.Length); bw.Write(0); bw.Write(1);
                for (int i = 0; i < 11; i++) bw.Write(0);
                bw.Write(32); bw.Write(4); bw.Write(0x30315844); for (int i = 0; i < 5; i++) bw.Write(0);
                bw.Write(0x1000); for (int i = 0; i < 4; i++) bw.Write(0);
                bw.Write(98); bw.Write(3); bw.Write(0); bw.Write(1); bw.Write(0);
                bw.Write(top);
                File.WriteAllBytes(Path.Combine(TexCache, t.Name + ".dds"), ms.ToArray());
                return null;
            }
            var wr = new RowWriter(w);
            TextureUtil.DecodeTextureDataToWriter(top, w, h, t.Format, wr);
            return EncodePng(wr.Rows, w, h);
        }
        return null;
    }

    static void Diag()
    {
        var bones = Skel.Definition.Data.Bones;
        for (int i = 0; i < bones.Count; i++)
        {
            var b = bones[i];
            Matrix4x4 r = b.RestFrame; r.M44 = 1;
            Matrix4x4.Decompose(r, out var s, out var q, out var t);
            Console.WriteLine($"{i} {b.Name} parent={(b.Parent == null ? -1 : bones.IndexOf(b.Parent))} t=({t.X:F3},{t.Y:F3},{t.Z:F3}) q=({q.X:F2},{q.Y:F2},{q.Z:F2},{q.W:F2}) M44={b.RestFrame.M44}");
        }
        foreach (var an in new[] { "anim_female_idle", "anim_female_walk_forward_unarmed", "pledge_troop_walk_with_banner_loop" })
        {
            var d = AN[an].Definition.Data;
            Console.WriteLine($"ANIM {an} bones={d.BoneAnims.Count} rootpos={d.RootPositionFrames.Count}");
            double dotA = 0, dotB = 0; int n = 0;
            for (int i = 0; i < Math.Min(d.BoneAnims.Count, bones.Count); i++)
            {
                var ba = d.BoneAnims[i];
                if (ba.RotationFrames.Count == 0) continue;
                Matrix4x4 r = bones[i].RestFrame; r.M44 = 1;
                Matrix4x4.Decompose(r, out _, out var rq, out _);
                var q = ba.RotationFrames.Values[0].Value;
                dotA += Math.Abs(Quaternion.Dot(q, rq));
                dotB += Math.Abs(Quaternion.Dot(Quaternion.Conjugate(q), rq));
                n++;
                if (i < 6) Console.WriteLine($"  bone {i} rest=({rq.X:F2},{rq.Y:F2},{rq.Z:F2},{rq.W:F2}) anim=({q.X:F2},{q.Y:F2},{q.Z:F2},{q.W:F2}) keys={ba.RotationFrames.Count} posKeys={ba.PositionFrames.Count}");
            }
            Console.WriteLine($"  meanDot same={dotA / n:F3} conj={dotB / n:F3} n={n}  firstKeyTimes={string.Join(",", d.BoneAnims[1].RotationFrames.Keys.Take(5))}");
        }
        foreach (var n in MM.Keys.Where(k => k.Contains("hair") && k.Contains("female")).Take(40)) Console.Write(n + " ");
        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    class G
    {
        public int PartCount, AnimCount;
        Skeleton skel;
        List<Dictionary<string, object>> nodes = new(), meshes = new(), materials = new(), textures = new(), images = new(),
            skins = new(), anims = new(), accessors = new(), bufferViews = new();
        MemoryStream bin = new MemoryStream();
        Dictionary<string, int> matCache = new(), texCache = new();
        int[] boneNode;
        int rootNode;
        int skinIdx = -1;
        bool wantSkin = false;
        Dictionary<string, string> texMap = new();
        List<int> rootChildren = new();

        public G(Skeleton s) { skel = s; }

        int Pad() { while (bin.Position % 4 != 0) bin.WriteByte(0); return (int)bin.Position; }

        int BufferView(byte[] data, int? target = null)
        {
            int off = Pad();
            bin.Write(data, 0, data.Length);
            var bv = new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = off, ["byteLength"] = data.Length };
            if (target != null) bv["target"] = target.Value;
            bufferViews.Add(bv);
            return bufferViews.Count - 1;
        }

        int AccF(float[] d, int comp, string type, int? target = null, bool minmax = false)
        {
            var bytes = new byte[d.Length * 4];
            Buffer.BlockCopy(d, 0, bytes, 0, bytes.Length);
            int bv = BufferView(bytes, target);
            var acc = new Dictionary<string, object> { ["bufferView"] = bv, ["componentType"] = 5126, ["count"] = d.Length / comp, ["type"] = type };
            if (minmax)
            {
                var mn = new float[comp]; var mx = new float[comp];
                for (int c = 0; c < comp; c++) { mn[c] = float.MaxValue; mx[c] = float.MinValue; }
                for (int i = 0; i < d.Length; i++) { int c = i % comp; mn[c] = Math.Min(mn[c], d[i]); mx[c] = Math.Max(mx[c], d[i]); }
                acc["min"] = mn; acc["max"] = mx;
            }
            accessors.Add(acc); return accessors.Count - 1;
        }

        int AccU32(uint[] d)
        {
            var bytes = new byte[d.Length * 4];
            Buffer.BlockCopy(d, 0, bytes, 0, bytes.Length);
            int bv = BufferView(bytes, 34963);
            accessors.Add(new Dictionary<string, object> { ["bufferView"] = bv, ["componentType"] = 5125, ["count"] = d.Length, ["type"] = "SCALAR" });
            return accessors.Count - 1;
        }

        int AccU16x4(ushort[] d)
        {
            var bytes = new byte[d.Length * 2];
            Buffer.BlockCopy(d, 0, bytes, 0, bytes.Length);
            int bv = BufferView(bytes, 34962);
            accessors.Add(new Dictionary<string, object> { ["bufferView"] = bv, ["componentType"] = 5123, ["count"] = d.Length / 4, ["type"] = "VEC4" });
            return accessors.Count - 1;
        }

        static Matrix4x4 Rest(BoneNode b) { var r = b.RestFrame; r.M44 = 1f; return r; }

        void BuildSkeleton()
        {
            if (boneNode != null) return;
            var bones = skel.Definition.Data.Bones;
            rootNode = AddNode(new Dictionary<string, object> { ["name"] = "Root", ["rotation"] = new[] { -0.70710678f, 0f, 0f, 0.70710678f } });
            boneNode = new int[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                var r = Rest(bones[i]);
                Vector3 s = Vector3.One, t = Vector3.Zero; Quaternion q = Quaternion.Identity;
                if (!Matrix4x4.Decompose(r, out s, out q, out t)) { t = r.Translation; q = Quaternion.CreateFromRotationMatrix(r); }
                boneNode[i] = AddNode(new Dictionary<string, object>
                {
                    ["name"] = bones[i].Name.Replace(' ', '_'),
                    ["translation"] = new[] { t.X, t.Y, t.Z },
                    ["rotation"] = new[] { q.X, q.Y, q.Z, q.W },
                });
            }
            var kids = new Dictionary<int, List<int>>();
            for (int i = 0; i < bones.Count; i++)
            {
                int p = bones[i].Parent == null ? -1 : bones.IndexOf(bones[i].Parent);
                if (p < 0) { rootChildren.Add(boneNode[i]); }
                else { if (!kids.ContainsKey(p)) kids[p] = new List<int>(); kids[p].Add(boneNode[i]); }
            }
            foreach (var kv in kids) nodes[boneNode[kv.Key]]["children"] = kv.Value.ToArray();

            // inverse bind matrices (row-vector convention: write row-major == glTF column-major of the transposed matrix)
            var ibm = new float[bones.Count * 16];
            for (int i = 0; i < bones.Count; i++)
            {
                Matrix4x4 world = Rest(bones[i]);
                var b = bones[i];
                while (b.Parent != null) { b = b.Parent; world = Matrix4x4.Multiply(world, Rest(b)); }
                Matrix4x4.Invert(world, out var inv);
                ibm[i * 16 + 0] = inv.M11; ibm[i * 16 + 1] = inv.M12; ibm[i * 16 + 2] = inv.M13; ibm[i * 16 + 3] = inv.M14;
                ibm[i * 16 + 4] = inv.M21; ibm[i * 16 + 5] = inv.M22; ibm[i * 16 + 6] = inv.M23; ibm[i * 16 + 7] = inv.M24;
                ibm[i * 16 + 8] = inv.M31; ibm[i * 16 + 9] = inv.M32; ibm[i * 16 + 10] = inv.M33; ibm[i * 16 + 11] = inv.M34;
                ibm[i * 16 + 12] = inv.M41; ibm[i * 16 + 13] = inv.M42; ibm[i * 16 + 14] = inv.M43; ibm[i * 16 + 15] = inv.M44;
            }
            int ibmAcc = AccF(ibm, 16, "MAT4");
            skins.Add(new Dictionary<string, object>
            {
                ["joints"] = boneNode.ToArray(),
                ["inverseBindMatrices"] = ibmAcc,
                ["skeleton"] = rootChildren.Count > 0 ? rootChildren[0] : boneNode[0],
            });
            skinIdx = 0;
        }

        int AddNode(Dictionary<string, object> n) { nodes.Add(n); return nodes.Count - 1; }

        static bool IsDiffuseName(string n)
        {
            n = n.ToLower();
            return n.EndsWith("_d") || n.EndsWith("_diffuse") || n.EndsWith("_c") || n.EndsWith("_color") || n.EndsWith("_base_color") || n.EndsWith("_d_4k") || n.EndsWith("_c_4k");
        }

        int TextureIndex(Texture tex)
        {
            if (texCache.TryGetValue(tex.Name, out var ti)) return ti;
            string mapped = texMap.TryGetValue(tex.Name, out var mn) ? mn : tex.Name;
            string cached = Path.Combine(TexCache, mapped + ".png");
            if (File.Exists(cached))
            {
                int bvc = BufferView(File.ReadAllBytes(cached));
                images.Add(new Dictionary<string, object> { ["bufferView"] = bvc, ["mimeType"] = "image/png", ["name"] = tex.Name });
                textures.Add(new Dictionary<string, object> { ["source"] = images.Count - 1, ["sampler"] = 0 });
                texCache[tex.Name] = textures.Count - 1; return texCache[tex.Name];
            }
            if (!tex.HasPixelData)
            {
                var fp = P.FallbackPng(tex);
                if (fp == null) { Console.Error.WriteLine("  no pixels " + tex.Name); texCache[tex.Name] = -1; return -1; }
                int bv0 = BufferView(fp);
                images.Add(new Dictionary<string, object> { ["bufferView"] = bv0, ["mimeType"] = "image/png", ["name"] = tex.Name + " (low-res)" });
                textures.Add(new Dictionary<string, object> { ["source"] = images.Count - 1, ["sampler"] = 0 });
                texCache[tex.Name] = textures.Count - 1; return texCache[tex.Name];
            }
            string tmp = Path.Combine(Path.GetTempPath(), "bl_" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                TextureExporter.ExportToFile(tmp, tex);
                var bytes = File.ReadAllBytes(tmp);
                int bv = BufferView(bytes);
                images.Add(new Dictionary<string, object> { ["bufferView"] = bv, ["mimeType"] = "image/png", ["name"] = tex.Name });
                textures.Add(new Dictionary<string, object> { ["source"] = images.Count - 1, ["sampler"] = 0 });
                texCache[tex.Name] = textures.Count - 1;
            }
            catch (Exception e) { Console.Error.WriteLine("  texture fail " + tex.Name + ": " + e.Message); texCache[tex.Name] = -1; }
            finally { try { File.Delete(tmp); } catch { } }
            return texCache[tex.Name];
        }

        int MaterialIndex(Mesh me, string tint)
        {
            string key = (me.Material.TryGetItem(out var mt) ? mt.Name : "none") + "|" + tint;
            if (matCache.TryGetValue(key, out var mi)) return mi;
            var mat = new Dictionary<string, object> { ["name"] = mt != null ? mt.Name : "none", ["doubleSided"] = true };
            var pbr = new Dictionary<string, object> { ["metallicFactor"] = 0.0f, ["roughnessFactor"] = 0.8f };
            if (mt != null)
            {
                Texture best = null;
                foreach (var td in mt.Textures.Values)
                {
                    if (td.TryGetItem(out var tx))
                    {
                        string ln = tx.Name.ToLower();
                        if (IsDiffuseName(tx.Name)) { best = tx; break; }
                    }
                }
                if (best == null)
                    foreach (var td in mt.Textures.Values)
                        if (td.TryGetItem(out var tx))
                        {
                            string ln = tx.Name.ToLower();
                            if (ln.EndsWith("_n") || ln.EndsWith("_s") || ln.EndsWith("_h") || ln.StartsWith("default") || ln.EndsWith("_normal") || ln.EndsWith("_specular")) continue;
                            best = tx; break;
                        }
                if (best != null)
                {
                    int ti = TextureIndex(best);
                    if (ti >= 0) pbr["baseColorTexture"] = new Dictionary<string, object> { ["index"] = ti };
                    string ln = best.Name.ToLower() + " " + mt.Name.ToLower();
                    if (ln.Contains("lash") || ln.Contains("hair") || ln.Contains("alpha") || ln.Contains("eyebrow") || ln.Contains("brow") || ln.Contains("flag") || ln.Contains("cloth"))
                    { mat["alphaMode"] = "MASK"; mat["alphaCutoff"] = 0.4f; }
                }
            }
            if (tint != null)
            {
                var c = tint.Split(',').Select(float.Parse).ToArray();
                pbr["baseColorFactor"] = new[] { c[0], c[1], c[2], 1f };
            }
            mat["pbrMetallicRoughness"] = pbr;
            materials.Add(mat);
            matCache[key] = materials.Count - 1;
            return materials.Count - 1;
        }

        // returns glTF mesh index, or -1 if nothing exported
        int BuildMesh(string metaName, bool skinned, string[] excludeContains)
        {
            if (!MM.TryGetValue(metaName, out var mm)) { Console.WriteLine("  MISSING metamesh " + metaName); return -1; }
            var prims = new List<object>();
            foreach (var me in mm.Meshes.Where(x => x.Lod == 0))
            {
                if (excludeContains != null && excludeContains.Any(e => me.Name.Contains(e))) continue;
                var vs = me.VertexStream?.Data;
                if (vs == null || vs.Positions == null || vs.Indices == null || vs.Positions.Length == 0) { Console.WriteLine("  no vertex data " + metaName + "/" + me.Name); continue; }
                int n = vs.Positions.Length;
                var pos = new float[n * 3]; var nor = new float[n * 3]; var uv = new float[n * 2];
                for (int i = 0; i < n; i++)
                {
                    pos[i * 3] = vs.Positions[i].X; pos[i * 3 + 1] = vs.Positions[i].Y; pos[i * 3 + 2] = vs.Positions[i].Z;
                    if (vs.Normals != null && vs.Normals.Length == n) { nor[i * 3] = vs.Normals[i].X; nor[i * 3 + 1] = vs.Normals[i].Y; nor[i * 3 + 2] = vs.Normals[i].Z; }
                    else nor[i * 3 + 2] = 1;
                    if (vs.Uv1 != null && vs.Uv1.Length == n) { uv[i * 2] = vs.Uv1[i].X; uv[i * 2 + 1] = vs.Uv1[i].Y; }
                }
                var attrs = new Dictionary<string, object>
                {
                    ["POSITION"] = AccF(pos, 3, "VEC3", 34962, true),
                    ["NORMAL"] = AccF(nor, 3, "VEC3", 34962),
                    ["TEXCOORD_0"] = AccF(uv, 2, "VEC2", 34962),
                };
                if (skinned && vs.BoneIndices != null && vs.BoneWeights != null && vs.BoneIndices.Length == n && vs.BoneWeights.Length == n)
                {
                    var j = new ushort[n * 4]; var w = new float[n * 4];
                    for (int i = 0; i < n; i++)
                    {
                        var bi = vs.BoneIndices[i]; var bw = vs.BoneWeights[i];
                        j[i * 4] = bi.B1; j[i * 4 + 1] = bi.B2; j[i * 4 + 2] = bi.B3; j[i * 4 + 3] = bi.B4;
                        w[i * 4] = bw.W1 / 255f; w[i * 4 + 1] = bw.W2 / 255f; w[i * 4 + 2] = bw.W3 / 255f; w[i * 4 + 3] = bw.W4 / 255f;
                        float s = w[i * 4] + w[i * 4 + 1] + w[i * 4 + 2] + w[i * 4 + 3];
                        if (s <= 0) { w[i * 4] = 1; } else for (int k = 0; k < 4; k++) w[i * 4 + k] /= s;
                    }
                    attrs["JOINTS_0"] = AccU16x4(j);
                    attrs["WEIGHTS_0"] = AccF(w, 4, "VEC4", 34962);
                }
                var idx = vs.Indices.Select(x => (uint)x).ToArray();
                prims.Add(new Dictionary<string, object> { ["attributes"] = attrs, ["indices"] = AccU32(idx), ["material"] = MaterialIndex(me, null), ["extras"] = new Dictionary<string, object> { ["part"] = me.Name } });
            }
            if (prims.Count == 0) return -1;
            meshes.Add(new Dictionary<string, object> { ["name"] = metaName, ["primitives"] = prims });
            PartCount++;
            return meshes.Count - 1;
        }

        public void Build(JsonElement sc)
        {
            if (sc.TryGetProperty("texmap", out var tmj)) foreach (var kv in tmj.EnumerateObject()) texMap[kv.Name] = kv.Value.GetString();
            bool skeletal = !sc.TryGetProperty("skeletal", out var sk) || sk.GetBoolean();
            if (skeletal) BuildSkeleton();
            else rootNode = AddNode(new Dictionary<string, object> { ["name"] = "Root", ["rotation"] = new[] { -0.70710678f, 0f, 0f, 0.70710678f } });
            var rootKids = new List<int>(rootChildren);
            var sceneRoots = new List<int> { rootNode };
            if (sc.TryGetProperty("parts", out var parts))
                foreach (var p in parts.EnumerateArray())
                {
                    string[] excl = null;
                    string name = p.GetString();
                    int mi = BuildMesh(name, skeletal, excl);
                    if (mi < 0) continue;
                    var node = new Dictionary<string, object> { ["name"] = name, ["mesh"] = mi };
                    if (skeletal) node["skin"] = skinIdx;
                    int ni = AddNode(node);
                    if (skeletal) sceneRoots.Add(ni); else rootKids.Add(ni);
                }
            if (sc.TryGetProperty("static", out var sts))
                foreach (var st in sts.EnumerateArray())
                {
                    string mname = st.GetProperty("mesh").GetString();
                    int mi = BuildMesh(mname, false, null);
                    if (mi < 0) continue;
                    var node = new Dictionary<string, object> { ["name"] = mname, ["mesh"] = mi };
                    if (st.TryGetProperty("t", out var t)) node["translation"] = t.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    if (st.TryGetProperty("q", out var q)) node["rotation"] = q.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                    int ni = AddNode(node);
                    string attach = st.TryGetProperty("attach", out var at) ? at.GetString() : null;
                    if (attach != null && skeletal)
                    {
                        var bones = skel.Definition.Data.Bones;
                        int bi = bones.FindIndex(b => b.Name == attach);
                        if (bi < 0) throw new Exception("bone not found " + attach);
                        var ch = nodes[boneNode[bi]].TryGetValue("children", out var c0) ? ((int[])c0).ToList() : new List<int>();
                        ch.Add(ni); nodes[boneNode[bi]]["children"] = ch.ToArray();
                    }
                    else rootKids.Add(ni);
                }
            if (skeletal && sc.TryGetProperty("anims", out var ans))
                foreach (var an in ans.EnumerateArray()) AddAnim(an.GetString());
            nodes[rootNode]["children"] = rootKids.Concat(skeletal ? new int[0] : new int[0]).ToArray();
            if (skeletal) { nodes[rootNode]["children"] = rootKids.ToArray(); }
            Title = sc.TryGetProperty("title", out var ti) ? ti.GetString() : "";
        }
        string Title;

        void AddAnim(string name)
        {
            if (!AN.TryGetValue(name, out var an)) { Console.WriteLine("  MISSING anim " + name); return; }
            var d = an.Definition?.Data;
            if (d == null) { Console.WriteLine("  no anim data " + name); return; }
            var bones = skel.Definition.Data.Bones;
            var samplers = new List<object>(); var channels = new List<object>();
            // Bannerlord clips carry the bind (T) pose as their first and/or last keyframe; leaving it in makes the loop snap to T-pose.
            // Drop such end frames (all bones at rest) and re-time so the clip starts at 0.
            int firstOk = 0, lastOk = 0; int checkedBones = 0; bool trimFirst, trimLast;
            for (int i = 0; i < Math.Min(d.BoneAnims.Count, bones.Count); i++)
            {
                var rf = d.BoneAnims[i].RotationFrames;
                if (rf.Count < 3) continue;
                Matrix4x4.Decompose(Rest(bones[i]), out _, out var rq, out _);
                if (Math.Abs(Quaternion.Dot(Quaternion.Normalize(rf.Values[0].Value), rq)) >= 0.9995f) firstOk++;
                if (Math.Abs(Quaternion.Dot(Quaternion.Normalize(rf.Values[rf.Count - 1].Value), rq)) >= 0.9995f) lastOk++;
                checkedBones++;
            }
            // tolerant: unskinned helper bones (fingers) may differ; 90% of bones at rest = reference frame
            trimFirst = checkedBones > 0 && firstOk >= 0.9 * checkedBones;
            trimLast = checkedBones > 0 && lastOk >= 0.9 * checkedBones;
            float t0 = 0f;
            for (int i = 0; i < Math.Min(d.BoneAnims.Count, bones.Count); i++)
            {
                var ba = d.BoneAnims[i];
                if (ba.PositionFrames.Count > 0)
                {
                    var times = ba.PositionFrames.Keys.Select(k => k / FPS).ToArray();
                    var vals = new List<float>();
                    foreach (var f in ba.PositionFrames.Values) { vals.Add(f.Value.X); vals.Add(f.Value.Y); vals.Add(f.Value.Z); }
                    samplers.Add(new Dictionary<string, object> { ["input"] = AccF(times, 1, "SCALAR", null, true), ["output"] = AccF(vals.ToArray(), 3, "VEC3"), ["interpolation"] = "LINEAR" });
                    channels.Add(new Dictionary<string, object> { ["sampler"] = samplers.Count - 1, ["target"] = new Dictionary<string, object> { ["node"] = boneNode[i], ["path"] = "translation" } });
                }
                if (ba.RotationFrames.Count > 0)
                {
                    var keys = ba.RotationFrames.Keys.ToList(); var frs = ba.RotationFrames.Values.ToList();
                    int a0 = (trimFirst && keys.Count >= 3) ? 1 : 0, a1 = (trimLast && keys.Count >= 3) ? keys.Count - 2 : keys.Count - 1;
                    if (i == 0 || t0 == 0f) t0 = keys[a0];
                    var times = new List<float>(); var vals = new List<float>();
                    Quaternion prev = Quaternion.Identity; bool first = true;
                    for (int k = a0; k <= a1; k++)
                    {
                        times.Add((keys[k] - keys[a0]) / FPS);
                        var q = Quaternion.Normalize(frs[k].Value);
                        if (!first && Quaternion.Dot(q, prev) < 0) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
                        prev = q; first = false;
                        vals.Add(q.X); vals.Add(q.Y); vals.Add(q.Z); vals.Add(q.W);
                    }
                    samplers.Add(new Dictionary<string, object> { ["input"] = AccF(times.ToArray(), 1, "SCALAR", null, true), ["output"] = AccF(vals.ToArray(), 4, "VEC4"), ["interpolation"] = "LINEAR" });
                    channels.Add(new Dictionary<string, object> { ["sampler"] = samplers.Count - 1, ["target"] = new Dictionary<string, object> { ["node"] = boneNode[i], ["path"] = "rotation" } });
                }
            }
            if (trimFirst || trimLast) Console.WriteLine($"  anim {name}: trimmed rest-pose end frames (first={trimFirst}, last={trimLast})");
            if (channels.Count == 0) return;
            anims.Add(new Dictionary<string, object> { ["name"] = name, ["samplers"] = samplers, ["channels"] = channels });
            AnimCount++;
        }

        public void Save(string path)
        {
            var root = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "bannerlord-tpac-glb" },
                ["scene"] = 0,
                ["scenes"] = new[] { new Dictionary<string, object> { ["nodes"] = new[] { rootNode } } },
                ["nodes"] = nodes,
                ["buffers"] = new[] { new Dictionary<string, object> { ["byteLength"] = (int)Pad() } },
                ["bufferViews"] = bufferViews,
                ["accessors"] = accessors,
                ["samplers"] = new[] { new Dictionary<string, object> { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 } },
            };
            // skinned mesh nodes live at the scene root level too (they reference the skin; skeleton is under Root)
            var sceneNodes = new List<int> { rootNode };
            for (int i = 0; i < nodes.Count; i++) if (nodes[i].ContainsKey("skin")) sceneNodes.Add(i);
            root["scenes"] = new[] { new Dictionary<string, object> { ["nodes"] = sceneNodes } };
            if (meshes.Count > 0) root["meshes"] = meshes;
            if (materials.Count > 0) root["materials"] = materials;
            if (textures.Count > 0) { root["textures"] = textures; root["images"] = images; }
            if (skins.Count > 0) root["skins"] = skins;
            if (anims.Count > 0) root["animations"] = anims;
            var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root));
            int jpad = (4 - json.Length % 4) % 4;
            var binBytes = bin.ToArray();
            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);
            int total = 12 + 8 + json.Length + jpad + 8 + binBytes.Length;
            bw.Write(0x46546C67); bw.Write(2); bw.Write(total);
            bw.Write(json.Length + jpad); bw.Write(0x4E4F534A); bw.Write(json); for (int i = 0; i < jpad; i++) bw.Write((byte)0x20);
            bw.Write(binBytes.Length); bw.Write(0x004E4942); bw.Write(binBytes);
        }
    }
}
