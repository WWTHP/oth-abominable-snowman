using UnityEngine;

namespace AbominableSnowman
{
    /// <summary>
    /// A procedurally built abominable snowman: shaggy white body made of
    /// primitives (no colliders, so he never touches the game's physics), a dark
    /// face with glowing red eyes, long arms, and simple walk / idle / roar / throw
    /// animation driven from code.
    /// </summary>
    internal sealed class YetiModel
    {
        public readonly GameObject Root;
        private readonly Transform _body, _head, _jaw, _armL, _armR, _legL, _legR;
        private float _walkPhase;

        private static Material _fur, _furShade, _under, _hairLight, _hairMid, _skin, _eyes, _mouth;
        private readonly YetiFur _hair;

        public YetiModel(string name)
        {
            EnsureMaterials();
            Root = new GameObject(name);
            Object.DontDestroyOnLoad(Root);

            _body = Empty("Body", Root.transform, new Vector3(0f, 1.25f, 0f));

            // Torso: barrel chest, hunched forward a little.
            Part(PrimitiveType.Capsule, _body, new Vector3(0f, 0.75f, 0f), new Vector3(1.35f, 0.95f, 1.0f), _under, new Vector3(12f, 0f, 0f));
            Part(PrimitiveType.Sphere, _body, new Vector3(0f, 0.55f, 0.32f), new Vector3(1.05f, 1.15f, 0.6f), _furShade);
            // Shaggy tufts on shoulders and back for silhouette.
            Part(PrimitiveType.Sphere, _body, new Vector3(0.55f, 1.3f, -0.1f), new Vector3(0.7f, 0.55f, 0.7f), _fur);
            Part(PrimitiveType.Sphere, _body, new Vector3(-0.55f, 1.3f, -0.1f), new Vector3(0.7f, 0.55f, 0.7f), _fur);
            Part(PrimitiveType.Sphere, _body, new Vector3(0f, 1.15f, -0.4f), new Vector3(0.9f, 0.7f, 0.6f), _fur);

            // Head
            _head = Empty("Head", _body, new Vector3(0f, 1.6f, 0.28f));
            Part(PrimitiveType.Sphere, _head, new Vector3(0f, 0.25f, 0f), new Vector3(0.85f, 0.85f, 0.85f), _under);
            Part(PrimitiveType.Sphere, _head, new Vector3(0f, 0.6f, -0.05f), new Vector3(0.55f, 0.45f, 0.55f), _fur); // crest
            Part(PrimitiveType.Sphere, _head, new Vector3(0f, 0.18f, 0.3f), new Vector3(0.6f, 0.55f, 0.4f), _skin);   // face
            Part(PrimitiveType.Capsule, _head, new Vector3(0f, 0.38f, 0.42f), new Vector3(0.6f, 0.07f, 0.12f), _fur, new Vector3(0f, 0f, 90f)); // brow
            Part(PrimitiveType.Sphere, _head, new Vector3(0.14f, 0.3f, 0.47f), Vector3.one * 0.11f, _eyes);
            Part(PrimitiveType.Sphere, _head, new Vector3(-0.14f, 0.3f, 0.47f), Vector3.one * 0.11f, _eyes);
            _jaw = Empty("Jaw", _head, new Vector3(0f, 0.08f, 0.38f));
            Part(PrimitiveType.Sphere, _jaw, new Vector3(0f, -0.04f, 0.06f), new Vector3(0.32f, 0.12f, 0.12f), _mouth);
            Part(PrimitiveType.Cube, _jaw, new Vector3(0.09f, 0.02f, 0.11f), new Vector3(0.04f, 0.07f, 0.03f), _fur); // fangs
            Part(PrimitiveType.Cube, _jaw, new Vector3(-0.09f, 0.02f, 0.11f), new Vector3(0.04f, 0.07f, 0.03f), _fur);

            // Long arms hanging from the shoulders (pivot at the shoulder).
            _armL = Limb("ArmL", _body, new Vector3(0.82f, 1.2f, 0.05f), 1.55f, 0.42f, out _);
            _armR = Limb("ArmR", _body, new Vector3(-0.82f, 1.2f, 0.05f), 1.55f, 0.42f, out _);
            // Short thick legs (pivot at the hip).
            _legL = Limb("LegL", Root.transform, new Vector3(0.38f, 1.3f, 0f), 1.25f, 0.5f, out var footL);
            _legR = Limb("LegR", Root.transform, new Vector3(-0.38f, 1.3f, 0f), 1.25f, 0.5f, out var footR);
            Part(PrimitiveType.Sphere, footL, new Vector3(0f, 0f, 0.18f), new Vector3(0.45f, 0.2f, 0.75f), _skin);
            Part(PrimitiveType.Sphere, footR, new Vector3(0f, 0f, 0.18f), new Vector3(0.45f, 0.2f, 0.75f), _skin);

            _hair = YetiFur.Attach(Root.transform, new[]
            {
                // torso coat
                new YetiFur.Patch { Parent = _body, Center = new Vector3(0f, 0.8f, 0f), Radii = new Vector3(0.66f, 0.95f, 0.55f),
                                    Count = 170, LenMin = 0.3f, LenMax = 0.5f, Droop = 1.0f },
                // long shaggy mane over shoulders and back
                new YetiFur.Patch { Parent = _body, Center = new Vector3(0f, 1.3f, -0.1f), Radii = new Vector3(0.85f, 0.42f, 0.58f),
                                    Count = 80, LenMin = 0.45f, LenMax = 0.7f, Droop = 1.3f, Skip = n => n.Y < -0.2f },
                // head, leaving the face bare
                new YetiFur.Patch { Parent = _head, Center = new Vector3(0f, 0.32f, -0.03f), Radii = new Vector3(0.45f, 0.47f, 0.45f),
                                    Count = 75, LenMin = 0.16f, LenMax = 0.32f, Droop = 0.55f, Skip = n => n.Z > 0.3f && n.Y < 0.6f },
                // arms: long hair hanging off the forearms, not the hands
                new YetiFur.Patch { Parent = _armL, Center = new Vector3(0f, -0.72f, 0f), Radii = new Vector3(0.25f, 0.85f, 0.25f),
                                    Count = 65, LenMin = 0.28f, LenMax = 0.5f, Droop = 0.9f, Skip = n => n.Y < -0.8f },
                new YetiFur.Patch { Parent = _armR, Center = new Vector3(0f, -0.72f, 0f), Radii = new Vector3(0.25f, 0.85f, 0.25f),
                                    Count = 65, LenMin = 0.28f, LenMax = 0.5f, Droop = 0.9f, Skip = n => n.Y < -0.8f },
                // legs: shaggy "trousers" down to the feet
                new YetiFur.Patch { Parent = _legL, Center = new Vector3(0f, -0.55f, 0f), Radii = new Vector3(0.3f, 0.72f, 0.3f),
                                    Count = 55, LenMin = 0.28f, LenMax = 0.45f, Droop = 1.1f, Skip = n => n.Y < -0.75f },
                new YetiFur.Patch { Parent = _legR, Center = new Vector3(0f, -0.55f, 0f), Radii = new Vector3(0.3f, 0.72f, 0.3f),
                                    Count = 55, LenMin = 0.28f, LenMax = 0.45f, Droop = 1.1f, Skip = n => n.Y < -0.75f },
            }, _hairLight, _hairMid);
        }

        public void Destroy()
        {
            if (Root != null) Object.Destroy(Root);
        }

        /// <param name="speed">ground speed m/s (drives the stride)</param>
        /// <param name="roar">0..1 roar pose (arms up, mouth open)</param>
        /// <param name="throwT">0..1 throw swing, or negative for none</param>
        public void Animate(float dt, float speed, float roar, float throwT, float time)
        {
            float size = Mathf.Max(0.1f, Root.transform.localScale.y);
            _walkPhase += dt * Mathf.Clamp(speed / size, 0f, 8f) * 2.4f;
            float walk = Mathf.Clamp01(speed / Mathf.Max(0.5f, size));
            float swing = Mathf.Sin(_walkPhase) * 32f * walk;

            _legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);

            float breathe = Mathf.Sin(time * 1.7f);
            float armIdle = 6f + breathe * 3f;
            float armL = -swing * 0.8f + armIdle;
            float armR = swing * 0.8f + armIdle;
            float spread = 8f;

            // Roar: arms thrown wide and up.
            armL = Mathf.Lerp(armL, -150f, roar);
            armR = Mathf.Lerp(armR, -150f, roar);
            spread = Mathf.Lerp(spread, 35f, roar);

            // Throw: both arms from overhead swung down and forward.
            if (throwT >= 0f)
            {
                float k = throwT < 0.35f ? throwT / 0.35f : 1f - (throwT - 0.35f) / 0.65f;
                float a = throwT < 0.35f ? Mathf.Lerp(-40f, -170f, k) : Mathf.Lerp(-170f, -60f, (throwT - 0.35f) / 0.65f);
                armL = armR = Mathf.Lerp(armL, a, Mathf.Clamp01(k * 3f));
                spread = 12f;
            }

            _armL.localRotation = Quaternion.Euler(armL, 0f, spread);
            _armR.localRotation = Quaternion.Euler(armR, 0f, -spread);

            // Body bob and lean; leans back while roaring.
            float bob = Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.08f * walk;
            _body.localPosition = new Vector3(0f, 1.25f + bob + breathe * 0.02f, 0f);
            _body.localRotation = Quaternion.Euler(Mathf.Lerp(8f * walk, -12f, roar), 0f, Mathf.Sin(_walkPhase) * 3f * walk);
            _head.localRotation = Quaternion.Euler(Mathf.Lerp(0f, -25f, roar), Mathf.Sin(time * 0.6f) * 12f * (1f - roar), 0f);
            _jaw.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 35f, Mathf.Max(roar, throwT >= 0f ? 0.6f : 0f)), 0f, 0f);
            _jaw.localScale = new Vector3(1f, 1f + roar * 1.5f, 1f);

            float agitation = Mathf.Max(roar, throwT >= 0f ? 1f - throwT : 0f);
            _hair?.Update(dt, time, agitation, CameraPos());
        }

        private static int _camFrame = -1;
        private static Vector3 _camPos;

        private static Vector3 CameraPos()
        {
            if (Time.frameCount != _camFrame)
            {
                _camFrame = Time.frameCount;
                var cam = Camera.main;
                _camPos = cam != null ? cam.transform.position : new Vector3(1e6f, 1e6f, 1e6f);
            }
            return _camPos;
        }

        // ------------------------------------------------------------- building

        private static Transform Empty(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        private static Transform Limb(string name, Transform parent, Vector3 pivot, float length, float thickness, out Transform end)
        {
            var p = Empty(name, parent, pivot);
            Part(PrimitiveType.Capsule, p, new Vector3(0f, -length * 0.5f, 0f), new Vector3(thickness, length * 0.5f, thickness), _under);
            Part(PrimitiveType.Sphere, p, new Vector3(0f, -0.1f, 0f), Vector3.one * thickness * 1.25f, _fur); // shaggy joint
            end = Empty(name + "End", p, new Vector3(0f, -length, 0f));
            if (name.StartsWith("Arm"))
                Part(PrimitiveType.Sphere, end, Vector3.zero, new Vector3(0.38f, 0.45f, 0.32f), _skin); // hand
            return p;
        }

        private static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }

        // ------------------------------------------------------------- materials

        private static void EnsureMaterials()
        {
            if (_fur != null) return;
            var lit = FindShader("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Standard");
            var unlit = FindShader("Universal Render Pipeline/Unlit", "Unlit/Color") ?? lit;
            if (lit == null) Mod.Log.Warning("No lit shader found for the yeti; using the primitive default.");

            _fur = Make(lit, new Color(0.93f, 0.95f, 0.98f), 0.15f);
            _furShade = Make(lit, new Color(0.78f, 0.82f, 0.88f), 0.1f);
            _under = Make(lit, new Color(0.62f, 0.66f, 0.72f), 0.05f);
            _hairLight = Make(lit, new Color(0.95f, 0.96f, 0.99f), 0.28f);
            _hairMid = Make(lit, new Color(0.84f, 0.86f, 0.9f), 0.22f);
            _skin = Make(lit, new Color(0.18f, 0.2f, 0.27f), 0.35f);
            _mouth = Make(unlit, new Color(0.25f, 0.02f, 0.04f), 0f);
            _eyes = Make(unlit, new Color(1f, 0.15f, 0.05f), 0f);
            if (_eyes != null)
            {
                _eyes.EnableKeyword("_EMISSION");
                _eyes.SetColor("_EmissionColor", new Color(3f, 0.4f, 0.1f));
            }
        }

        private static Shader FindShader(params string[] names)
        {
            foreach (var n in names)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            // Not findable by name: borrow it from any loaded material that uses it.
            try
            {
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                {
                    var sh = m != null ? m.shader : null;
                    if (sh == null) continue;
                    foreach (var n in names)
                        if (sh.name == n) return sh;
                }
            }
            catch (System.Exception ex)
            {
                Mod.Log.Warning("Shader search failed: " + ex.Message);
            }
            return null;
        }

        private static Material Make(Shader shader, Color c, float smooth)
        {
            if (shader == null) return null;
            var m = new Material(shader) { name = "Yeti_" + c };
            m.SetColor("_BaseColor", c);
            m.SetColor("_Color", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Glossiness", smooth);
            m.SetFloat("_Metallic", 0f);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset; // survive the game's asset cleanup on map load
            return m;
        }
    }
}
