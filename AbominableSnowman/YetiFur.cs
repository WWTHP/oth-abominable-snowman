using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using NQ = System.Numerics.Quaternion;
using NV = System.Numerics.Vector3;

namespace AbominableSnowman
{
    /// <summary>
    /// Volumetric, moving fur for the yeti.
    ///
    /// Hundreds of hair tufts (each a clump of tapered, double-sided strands) are
    /// scattered over ellipsoid patches on the body parts. All tufts of one yeti
    /// form a single GPU-skinned mesh: every tuft has a root bone and a tip bone,
    /// so the whole coat is one draw call and moving hair costs only one bone
    /// rotation per bone per frame.
    ///
    /// Motion: each tuft is a damped spring (2-axis tilt) driven by the
    /// acceleration of the body part it grows from (inertia when walking, lag on
    /// arm swings), the change of gravity in that part's frame (hair keeps
    /// hanging down when the arms go up), a gusting wind, and a flutter that
    /// becomes a bristle while he roars. The tip bone lags the root for a curl.
    ///
    /// All maths is System.Numerics (pure managed) to avoid interop overhead.
    /// </summary>
    internal sealed class YetiFur
    {
        /// <summary>One ellipsoid region of hair on a body part (in the part's local space).</summary>
        public sealed class Patch
        {
            public Transform Parent;
            public Vector3 Center, Radii;
            public int Count;
            public float LenMin, LenMax;
            public float Droop = 0.9f;      // how much hair hangs down (0 = sticks straight out)
            public Func<NV, bool> Skip;     // by surface normal (part local), e.g. the face
        }

        // ------------------------------------------------------------ template
        // Geometry is identical for every yeti (fixed seed), so the mesh is built once.

        private struct TuftDef
        {
            public int Patch;
            public NV LocalPos;
            public NQ LocalRot, InvRot;
            public NV GravRest;     // gravity in the tuft frame at rest
            public float Len, K, C, Phase;
        }

        private sealed class Template
        {
            public Mesh Mesh;
            public TuftDef[] Tufts;
            public float Density;
        }

        private static Template _template;
        private static Material[] _materials;
        private static bool _failed;

        // -------------------------------------------------------------- instance

        private sealed class Group
        {
            public Transform Parent;
            public NV Anchor;
            public NV LastPos, Vel, Acc;
            public bool Init;
            public NV AccL, GravL, WindL; // this frame, in the parent's frame
        }

        private readonly Transform _root;
        private readonly Transform[] _rootBones, _tipBones;
        private readonly int[] _groupOf;
        private readonly Group[] _groups;
        private readonly Vector2[] _d, _v, _d2;
        private readonly SkinnedMeshRenderer _smr;
        private readonly TuftDef[] _defs;

        private YetiFur(Transform root, Template tpl, Patch[] patches)
        {
            _root = root;
            _defs = tpl.Tufts;
            int n = _defs.Length;

            // one group per distinct parent
            var parents = new List<Transform>();
            var patchGroup = new int[patches.Length];
            for (int i = 0; i < patches.Length; i++)
            {
                int g = parents.IndexOf(patches[i].Parent);
                if (g < 0) { parents.Add(patches[i].Parent); g = parents.Count - 1; }
                patchGroup[i] = g;
            }
            _groups = new Group[parents.Count];
            for (int g = 0; g < _groups.Length; g++)
            {
                var anchor = NV.Zero; int c = 0;
                for (int i = 0; i < patches.Length; i++)
                    if (patchGroup[i] == g) { anchor += ToN(patches[i].Center); c++; }
                _groups[g] = new Group { Parent = parents[g], Anchor = anchor / Math.Max(1, c) };
            }

            _rootBones = new Transform[n];
            _tipBones = new Transform[n];
            _groupOf = new int[n];
            _d = new Vector2[n]; _v = new Vector2[n]; _d2 = new Vector2[n];
            var bones = new Transform[n * 2];
            for (int i = 0; i < n; i++)
            {
                ref var t = ref _defs[i];
                _groupOf[i] = patchGroup[t.Patch];
                var b = new GameObject("Hair").transform;
                b.SetParent(patches[t.Patch].Parent, false);
                b.localPosition = ToU(t.LocalPos);
                b.localRotation = ToU(t.LocalRot);
                var tip = new GameObject("Tip").transform;
                tip.SetParent(b, false);
                tip.localPosition = new Vector3(0f, t.Len * 0.5f, 0f);
                _rootBones[i] = b; _tipBones[i] = tip;
                bones[2 * i] = b; bones[2 * i + 1] = tip;
            }

            var go = new GameObject("Fur");
            go.transform.SetParent(root, false);
            _smr = go.AddComponent<SkinnedMeshRenderer>();
            _smr.sharedMesh = tpl.Mesh;
            _smr.bones = new Il2CppReferenceArray<Transform>(bones);
            _smr.rootBone = root;
            _smr.sharedMaterials = new Il2CppReferenceArray<Material>(_materials);
            _smr.localBounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(7f, 7f, 7f));
            _smr.updateWhenOffscreen = false;
            _smr.quality = SkinQuality.Bone2;
        }

        /// <summary>
        /// Adds fur to a freshly built model (still at the origin, rest pose).
        /// Returns null if fur is off or could not be created.
        /// </summary>
        public static YetiFur Attach(Transform root, Patch[] patches, Material light, Material mid)
        {
            float density = Mathf.Clamp(Settings.FurDensity.Value, 0f, 2f);
            if (_failed || density <= 0.01f || light == null) return null;
            try
            {
                _materials ??= new[] { light, mid ?? light };
                if (_template == null || !Mathf.Approximately(_template.Density, density))
                    _template = BuildTemplate(root, patches, density);
                return new YetiFur(root, _template, patches);
            }
            catch (Exception ex)
            {
                _failed = true;
                Mod.Log.Warning("Yeti fur disabled: " + ex);
                return null;
            }
        }

        // ------------------------------------------------------------ simulation

        private static readonly NV Down = new(0f, -1f, 0f);

        /// <param name="agitation">0..1, makes the hair bristle and flutter (roar / throw)</param>
        public void Update(float dt, float time, float agitation, Vector3 cameraPos)
        {
            if (_smr == null || !Settings.FurMovement.Value) return;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.05f);

            // Skip far-away yetis: their hair simply holds its last pose.
            var rp = _root.position;
            float cd = (rp - cameraPos).sqrMagnitude;
            if (cd > 90f * 90f) return;

            // Wind: slow direction change with gusts.
            float gust = 0.45f + 0.35f * Mathf.Sin(time * 0.37f) + 0.2f * Mathf.Sin(time * 1.13f + 1.7f);
            var wind = new NV(Mathf.Sin(time * 0.071f), 0f, Mathf.Cos(time * 0.053f)) * gust;

            foreach (var g in _groups)
            {
                var pos = ToN(g.Parent.TransformPoint(ToU(g.Anchor)));
                var rot = ToN(g.Parent.rotation);
                if (!g.Init || NV.DistanceSquared(pos, g.LastPos) > 25f)
                {
                    g.LastPos = pos; g.Vel = NV.Zero; g.Acc = NV.Zero; g.Init = true;
                }
                var vel = (pos - g.LastPos) / dt;
                var acc = (vel - g.Vel) / dt;
                if (acc.LengthSquared() > 60f * 60f) acc = NV.Normalize(acc) * 60f;
                g.Acc = NV.Lerp(g.Acc, acc, 1f - MathF.Exp(-dt * 25f)); // tame frame spikes
                g.Vel = vel;
                g.LastPos = pos;

                var inv = NQ.Conjugate(rot);
                g.AccL = NV.Transform(g.Acc, inv);
                g.GravL = NV.Transform(Down, inv);
                g.WindL = NV.Transform(wind, inv);
            }

            float flutterAmp = 0.02f + 0.18f * agitation;
            float lift = 0.55f * agitation;
            float tipK = 1f - MathF.Exp(-dt * 7f);

            for (int i = 0; i < _defs.Length; i++)
            {
                ref var t = ref _defs[i];
                var g = _groups[_groupOf[i]];

                var a = NV.Transform(g.AccL, t.InvRot);
                var gr = NV.Transform(g.GravL, t.InvRot);
                var w = NV.Transform(g.WindL, t.InvRot);

                float k = t.K;
                float ph = t.Phase;
                // forces on the tip displacement (x, z) in the tuft frame
                float fx = -a.X * 1.1f
                           + (gr.X - t.GravRest.X) * k * 0.6f
                           + w.X * k * 0.22f * (0.7f + 0.3f * MathF.Sin(time * 2.3f + ph))
                           + MathF.Sin(time * 7.1f + ph) * k * flutterAmp
                           - t.GravRest.X * k * lift;
                float fz = -a.Z * 1.1f
                           + (gr.Z - t.GravRest.Z) * k * 0.6f
                           + w.Z * k * 0.22f * (0.7f + 0.3f * MathF.Sin(time * 1.9f + ph * 1.3f))
                           + MathF.Cos(time * 5.3f + ph * 1.7f) * k * flutterAmp
                           - t.GravRest.Z * k * lift;

                var d = _d[i]; var v = _v[i];
                v.x += (fx - k * d.x - t.C * v.x) * dt;
                v.y += (fz - k * d.y - t.C * v.y) * dt;
                d.x += v.x * dt;
                d.y += v.y * dt;
                float m = d.magnitude;
                if (m > 1.1f) { d *= 1.1f / m; v *= 0.5f; }
                _d[i] = d; _v[i] = v;

                var d2 = _d2[i];
                d2 += (d - d2) * tipK;
                _d2[i] = d2;
                // the tip bends further while the root is still swinging
                var tipBend = (d - d2) * 0.9f + d * 0.3f;

                _rootBones[i].localRotation = ToU(t.LocalRot * Tilt(d.x, d.y));
                _tipBones[i].localRotation = ToU(Tilt(tipBend.x, tipBend.y));
            }
        }

        /// <summary>Rotation that moves +Y toward (+x, +z) by |(x, z)| radians.</summary>
        private static NQ Tilt(float x, float z)
        {
            float ang = MathF.Sqrt(x * x + z * z);
            if (ang < 1e-5f) return NQ.Identity;
            return NQ.CreateFromAxisAngle(new NV(z / ang, 0f, -x / ang), ang);
        }

        // ------------------------------------------------------------ building

        private const int StrandsPerTuft = 6;
        private const int Segments = 3;

        private static Template BuildTemplate(Transform root, Patch[] patches, float density)
        {
            var rng = new System.Random(4242);
            float R() => (float)rng.NextDouble();

            var defs = new List<TuftDef>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var tris = new[] { new List<int>(), new List<int>() };

            for (int pi = 0; pi < patches.Length; pi++)
            {
                var p = patches[pi];
                int count = Math.Max(1, (int)(p.Count * density));
                var c = ToN(p.Center);
                var r = ToN(p.Radii);
                // the part's pose in root space (model is at the origin, rest pose)
                var partRot = ToN(p.Parent.rotation);
                var partPos = ToN(p.Parent.position);
                var partScale = ToN(p.Parent.lossyScale);

                for (int j = 0; j < count; j++)
                {
                    // Fibonacci sphere direction, jittered
                    float yy = 1f - 2f * (j + 0.5f) / count;
                    float rad = MathF.Sqrt(MathF.Max(0f, 1f - yy * yy));
                    float th = j * 2.39996323f + (R() - 0.5f) * 0.6f;
                    var u = NV.Normalize(new NV(MathF.Cos(th) * rad, yy + (R() - 0.5f) * 0.08f, MathF.Sin(th) * rad));
                    var normal = NV.Normalize(u / r);
                    if (p.Skip != null && p.Skip(normal)) continue;

                    var surf = c + u * r * 0.97f;
                    // growth direction: out from the surface, drooping with gravity (part local)
                    var down = NV.Transform(Down, NQ.Conjugate(partRot));
                    var grow = normal + down * (p.Droop * (0.8f + 0.4f * R()));
                    grow = grow.LengthSquared() < 1e-4f ? normal : NV.Normalize(grow);

                    var rot = FromTo(NV.UnitY, grow) * NQ.CreateFromAxisAngle(NV.UnitY, R() * 6.2832f);
                    var inv = NQ.Conjugate(rot);
                    float len = p.LenMin + (p.LenMax - p.LenMin) * R();
                    float k = Math.Clamp(16f / len, 22f, 90f) * (0.85f + 0.3f * R());

                    var def = new TuftDef
                    {
                        Patch = pi,
                        LocalPos = surf - normal * 0.03f,
                        LocalRot = rot,
                        InvRot = inv,
                        GravRest = NV.Transform(down, inv),
                        Len = len,
                        K = k,
                        C = 2f * 0.28f * MathF.Sqrt(k),
                        Phase = R() * 100f,
                    };
                    int idx = defs.Count;
                    defs.Add(def);

                    // root-space pose of this tuft's bones
                    var boneRot = partRot * rot;
                    var bonePos = partPos + NV.Transform(def.LocalPos * partScale, partRot);
                    var nLocal = NV.Transform(normal, inv); // surface normal in the tuft frame
                    int sub = R() < 0.35f ? 1 : 0;

                    for (int s = 0; s < StrandsPerTuft; s++)
                    {
                        float a0 = R() * 6.2832f;
                        float br = (0.04f + 0.06f * len) * MathF.Sqrt(R());
                        var basePt = new NV(MathF.Cos(a0) * br, 0f, MathF.Sin(a0) * br);
                        float sl = len * (0.7f + 0.4f * R());
                        float bendA = R() * 6.2832f;
                        var bend = new NV(MathF.Cos(bendA), 0f, MathF.Sin(bendA)) * (0.18f * sl);
                        float sideA = R() * 6.2832f;
                        var side = new NV(MathF.Cos(sideA), 0f, MathF.Sin(sideA));
                        float w0 = Math.Clamp(0.05f + 0.06f * len, 0.04f, 0.09f);
                        var n = NV.Normalize(nLocal * 0.8f + NV.UnitY * 0.35f +
                                             new NV(R() - 0.5f, 0f, R() - 0.5f) * 0.3f);
                        var nRoot = NV.Transform(n, boneRot);

                        int first = verts.Count;
                        for (int row = 0; row <= Segments; row++)
                        {
                            float tt = row / (float)Segments;
                            var pt = basePt * (1f - 0.55f * tt) + NV.UnitY * (sl * tt) + bend * (tt * tt);
                            float w = MathF.Max(0.006f, w0 * (1f - tt * 0.9f));
                            for (int e = -1; e <= 1; e += 2)
                            {
                                var local = pt + side * (w * 0.5f * e);
                                verts.Add(ToU(bonePos + NV.Transform(local, boneRot)));
                                norms.Add(ToU(nRoot));
                                // root bone near the skin, tip bone toward the end
                                float wt = SmoothStep(0.3f, 1f, tt);
#pragma warning disable format // bone pairs on one line each
                                weights.Add(new BoneWeight
                                {
                                    m_BoneIndex0 = 2 * idx, m_Weight0 = 1f - wt,
                                    m_BoneIndex1 = 2 * idx + 1, m_Weight1 = wt,
                                });
#pragma warning restore format
                            }
                        }
                        var tl = tris[sub];
                        for (int row = 0; row < Segments; row++)
                        {
                            int a = first + row * 2, b = a + 1, c2 = a + 2, d = a + 3;
                            // both windings: visible from either side
                            tl.Add(a); tl.Add(c2); tl.Add(b);
                            tl.Add(b); tl.Add(c2); tl.Add(d);
                            tl.Add(a); tl.Add(b); tl.Add(c2);
                            tl.Add(b); tl.Add(d); tl.Add(c2);
                        }
                    }
                }
            }

            // bind poses: inverse of each bone's root-space matrix at rest
            var bind = new Matrix4x4[defs.Count * 2];
            for (int i = 0; i < defs.Count; i++)
            {
                var t = defs[i];
                var p = patches[t.Patch];
                var partRot = ToN(p.Parent.rotation);
                var partPos = ToN(p.Parent.position);
                var partScale = ToN(p.Parent.lossyScale);
                var boneRot = partRot * t.LocalRot;
                var bonePos = partPos + NV.Transform(t.LocalPos * partScale, partRot);
                var tipPos = bonePos + NV.Transform(new NV(0f, t.Len * 0.5f, 0f), boneRot);
                // parts are unscaled transforms; the root bone carries scale 1
                bind[2 * i] = InverseTR(bonePos, boneRot);
                bind[2 * i + 1] = InverseTR(tipPos, boneRot);
            }

            var mesh = new Mesh { name = "YetiFur" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = new Il2CppStructArray<Vector3>(verts.ToArray());
            mesh.normals = new Il2CppStructArray<Vector3>(norms.ToArray());
            mesh.boneWeights = new Il2CppStructArray<BoneWeight>(weights.ToArray());
            mesh.bindposes = new Il2CppStructArray<Matrix4x4>(bind);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new Il2CppStructArray<int>(tris[0].ToArray()), 0);
            mesh.SetTriangles(new Il2CppStructArray<int>(tris[1].ToArray()), 1);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;

            Mod.Log.Msg($"Yeti fur: {defs.Count} tufts, {verts.Count} vertices.");
            return new Template { Mesh = mesh, Tufts = defs.ToArray(), Density = density };
        }

        // ------------------------------------------------------------- helpers

        private static float SmoothStep(float a, float b, float x)
        {
            x = Math.Clamp((x - a) / (b - a), 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        private static NQ FromTo(NV a, NV b)
        {
            float dot = NV.Dot(a, b);
            if (dot < -0.9999f) return NQ.CreateFromAxisAngle(NV.UnitX, MathF.PI);
            var axis = NV.Cross(a, b);
            return NQ.Normalize(new NQ(axis, 1f + dot));
        }

        /// <summary>World→local matrix of a transform with position p, rotation q, unit scale.</summary>
        private static Matrix4x4 InverseTR(NV p, NQ q)
        {
            var inv = NQ.Conjugate(q);
            var ex = NV.Transform(NV.UnitX, inv);
            var ey = NV.Transform(NV.UnitY, inv);
            var ez = NV.Transform(NV.UnitZ, inv);
            var t = -NV.Transform(p, inv);
            // columns are the images of the basis vectors
#pragma warning disable format // keep the matrix laid out as rows
            return new Matrix4x4
            {
                m00 = ex.X, m10 = ex.Y, m20 = ex.Z, m30 = 0f,
                m01 = ey.X, m11 = ey.Y, m21 = ey.Z, m31 = 0f,
                m02 = ez.X, m12 = ez.Y, m22 = ez.Z, m32 = 0f,
                m03 = t.X, m13 = t.Y, m23 = t.Z, m33 = 1f,
            };
#pragma warning restore format
        }

        private static NV ToN(Vector3 v) => new(v.x, v.y, v.z);
        private static NQ ToN(Quaternion q) => new(q.x, q.y, q.z, q.w);
        private static Vector3 ToU(NV v) => new Vector3 { x = v.X, y = v.Y, z = v.Z };
        private static Quaternion ToU(NQ q) => new Quaternion { x = q.X, y = q.Y, z = q.Z, w = q.W };
    }
}
