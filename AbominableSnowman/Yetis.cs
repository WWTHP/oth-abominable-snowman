using System;
using System.Collections.Generic;
using System.IO;
using Il2Cpp;
using UnityEngine;

namespace AbominableSnowman
{
    /// <summary>
    /// The yetis themselves. Each yeti paces back and forth across the trail on a
    /// fixed schedule (walk across, pause, walk back, pause) computed from the
    /// session's shared network clock, so every player sees him in the same place
    /// without streaming his position. Drive past while he is off to the side.
    ///
    /// Each player's game only checks its own car. When he gets you, your game
    /// picks a random direction, throws your car, and tells everyone (so they see
    /// the throw, and so whoever owns your car's physics applies it too).
    /// </summary>
    internal static class Yetis
    {
        private const float TrailYetiLifetime = 15f;  // s without a keepalive
        private const float KeepaliveInterval = 5f;
        private const float ThrowAnimTime = 1.1f;
        private const float RoarAnimTime = 1.8f;
        private const float RoarRange = 45f;
        private const float RoarRepeat = 9f;
        private const float ImmunityAfterThrow = 3f;

        private sealed class Yeti
        {
            public uint Id, Group;
            public Vector3 Center, Axis;
            public float Phase;
            public float Expires;              // unscaled time; 0 = never (manual)
            public YetiModel Model;
            public float LastThrowNet = -1e6f;  // shared clock
            public float ThrowStart = -1e6f, RoarStart = -1e6f; // local clock
            public Vector3 FaceTarget;
            public bool HasFaceTarget;
            public float GroundY;
            public bool HaveGround;
            public Vector3 LastPos;
        }

        private static readonly Dictionary<uint, Yeti> _yetis = new();
        private static readonly HashSet<int> _peers = new();
        private static float _immuneUntil;
        private static IntPtr _runnerPtr;
        private static float _helloAgainAt = -1f;
        private static float[] _sentSettings;
        private static float _settingsChangedAt = -1f;
        private static readonly System.Random Rng = new();

        // Local challenge trail
        private static IntPtr _trailPtr;
        private static List<(uint id, uint group, Vector3 c, Vector3 a, float ph)> _trailSpecs;
        private static float _nextTrailCheck, _nextKeepalive;
        public static string TrailStatus { get; private set; } = "no active challenge";

        public static int Count => _yetis.Count;
        public static int ModdedPeers => _peers.Count;

        public static void Init() => Net.OnMessage = OnMessage;

        // ------------------------------------------------------------------ clock

        /// <summary>Raw shared clock: Fusion simulation time once sync is up, else local time.</summary>
        private static float RawNetTime()
        {
            // SimulationTime throws until the first server state arrives (while loading).
            var run = Net.Runner();
            if (run == null || !Net.Ready) return Time.time;
            try { return run.SimulationTime; }
            catch { return Time.time; }
        }

        private static float _clock;
        private static bool _clockInit;

        /// <summary>
        /// Smooth shared clock. Fusion's SimulationTime advances in network ticks, so
        /// driving the yetis from it directly made them stutter. We advance a local
        /// clock every frame and nudge it gently toward the network time.
        /// </summary>
        public static float NetTime() => _clockInit ? _clock : RawNetTime();

        private static void ClockUpdate(float dt)
        {
            float target = RawNetTime();
            if (!_clockInit) { _clock = target; _clockInit = true; return; }
            _clock += dt;
            float err = target - _clock;
            if (Mathf.Abs(err) > 2f) _clock = target;               // session change / big jump
            else _clock += err * (1f - Mathf.Exp(-dt * 0.8f));        // slew
        }

        // ------------------------------------------------------------------ frame

        public static void Update()
        {
            ConnectionUpdate();
            SettingsSyncUpdate();
            TrailUpdate();

            float now = Time.unscaledTime;
            float dt = Time.deltaTime;
            ClockUpdate(dt);
            float t = NetTime();
            var local = Cars.Local;
            int mask = ~0;
            if (local?.Body != null) mask &= ~(1 << local.Body.gameObject.layer);

            List<uint> dead = null;
            foreach (var y in _yetis.Values)
            {
                if (y.Expires > 0f && now > y.Expires || y.Model == null || y.Model.Root == null)
                {
                    (dead ??= new List<uint>()).Add(y.Id);
                    continue;
                }
                Simulate(y, t, now, dt, mask, local);
            }
            if (dead != null)
                foreach (var id in dead) Remove(id);
        }

        private static void Simulate(Yeti y, float t, float now, float dt, int mask, Cars.Car local)
        {
            float size = Mathf.Clamp(Settings.Size.Value, 0.3f, 4f);
            var root = y.Model.Root.transform;
            root.localScale = Vector3.one * size;

            Patrol(y, t, out float s, out float dir, out float walkSpeed);
            float hw = Mathf.Max(1f, Settings.PatrolHalfWidth.Value);
            var flat = y.Center + y.Axis * (s * hw);

            // Ground height: the terrain, or a road/rock just above it. The ray starts
            // only a little above the ground so trees, overhangs and airborne cars
            // can't lift him into the sky.
            float baseY;
            var terr = Snow.TerrainAt(flat);
            if (terr != null) baseY = terr.SampleHeight(flat) + terr.GetPosition().y;
            else baseY = y.HaveGround ? y.GroundY : y.Center.y;
            float gy = baseY;
            if (Physics.Raycast(new Vector3(flat.x, baseY + 2.5f, flat.z), Vector3.down, out var hit, 8f, mask, QueryTriggerInteraction.Ignore)
                && hit.rigidbody == null && hit.point.y <= baseY + 2f)
                gy = hit.point.y;
            if (!y.HaveGround || Mathf.Abs(gy - y.GroundY) > 6f) y.GroundY = gy; // first frame / big step: snap
            else y.GroundY = Mathf.Lerp(y.GroundY, gy, 1f - Mathf.Exp(-dt * 12f));
            y.HaveGround = true;
            var pos = new Vector3(flat.x, y.GroundY, flat.z);
            y.LastPos = pos;
            root.position = pos;
            float speed = walkSpeed;

            // Facing: the way he walks, else toward the trail; a victim overrides.
            var face = dir != 0f ? y.Axis * dir : -y.Axis * Mathf.Sign(s == 0f ? 1f : s);
            float roar = Curve(now - y.RoarStart, RoarAnimTime);
            float thr = now - y.ThrowStart;
            float throwT = thr < ThrowAnimTime ? thr / ThrowAnimTime : -1f;
            if ((roar > 0f || throwT >= 0f) && y.HasFaceTarget) face = y.FaceTarget - pos;
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f)
            {
                var want = Quaternion.LookRotation(face.normalized, Vector3.up);
                root.rotation = Quaternion.Slerp(root.rotation, want, 1f - Mathf.Exp(-dt * 6f));
            }
            y.Model.Animate(dt, roar > 0f || throwT >= 0f ? 0f : speed, roar, throwT, now + y.Phase);

            if (local?.Body == null) return;
            var car = local.Body.position;
            var d = car - pos;
            float horiz = new Vector2(d.x, d.z).magnitude;

            // Roar as a warning when you get close.
            if (horiz < RoarRange && now - y.RoarStart > RoarRepeat && now - y.ThrowStart > RoarRepeat)
            {
                y.RoarStart = now;
                y.FaceTarget = car;
                y.HasFaceTarget = true;
                Roar.Play(pos + Vector3.up * 3f * size, 0.8f);
            }
            if (roar > 0f) y.FaceTarget = car;

            // The grab.
            bool rested = t - y.LastThrowNet > Settings.Cooldown.Value;
            if (rested && now > _immuneUntil
                && horiz < Settings.Reach.Value * size + 1f
                && d.y > -2f * size && d.y < 3.5f * size)
            {
                Catch(y, local, t, now);
            }
        }

        /// <summary>s in [-1, 1] across the trail; dir = +1/-1 while walking, 0 while paused.</summary>
        private static void Patrol(Yeti y, float t, out float s, out float dir, out float speedMs)
        {
            float hw = Mathf.Max(1f, Settings.PatrolHalfWidth.Value);
            float speed = Mathf.Max(0.2f, Settings.WalkSpeed.Value);
            float pause = Mathf.Max(0f, Settings.PauseTime.Value);
            float cross = 2f * hw / speed;
            float period = 2f * (cross + pause);
            float u = Mathf.Repeat(t + y.Phase, period);

            float x;
            if (u < pause) { s = -1f; dir = 0f; x = -1f; }
            else if ((u -= pause) < cross) { x = u / cross; s = -1f + 2f * Ease(x); dir = 1f; }
            else if ((u -= cross) < pause) { s = 1f; dir = 0f; x = -1f; }
            else { u -= pause; x = u / cross; s = 1f - 2f * Ease(x); dir = -1f; }
            // d(position)/dt of the eased crossing, in m/s.
            speedMs = x < 0f ? 0f : 2f * hw * (6f * x * (1f - x)) / cross;
        }

        private static float Ease(float x) => x * x * (3f - 2f * x);

        /// <summary>0 → 1 → 0 bump over <paramref name="length"/> seconds.</summary>
        private static float Curve(float since, float length)
        {
            if (since < 0f || since > length) return 0f;
            float x = since / length;
            return Mathf.Clamp01(Mathf.Min(x * 5f, (1f - x) * 3f));
        }

        // ------------------------------------------------------------------ throw

        private static void Catch(Yeti y, Cars.Car victim, float t, float now)
        {
            float heading = (float)Rng.NextDouble() * 360f;
            float dist = Mathf.Max(1f, Settings.ThrowDistanceFeet.Value) * 0.3048f;
            var vel = Cars.ThrowVelocity(heading, dist, Settings.LaunchAngle.Value);
            var spin = UnityEngine.Random.onUnitSphere * Settings.Tumble.Value;

            _immuneUntil = now + ImmunityAfterThrow;
            Cars.Apply(victim, vel, spin);
            OnThrown(y, victim.Body.position, t, now);
            Toast.Show($"The Abominable Snowman threw you {Settings.ThrowDistanceFeet.Value:F0} ft!");
            Mod.Log.Msg($"Yeti {y.Id:X8} threw the local car heading {heading:F0}° at {vel.magnitude:F1} m/s");

            var w = Net.Begin(Net.Msg.Throw, out var ms);
            w.Write(y.Id);
            w.Write(victim.Player);
            Write(w, vel);
            Write(w, spin);
            w.Write(t);
            Net.Broadcast(ms);
        }

        private static void OnThrown(Yeti y, Vector3 victimPos, float t, float now)
        {
            if (y == null) return;
            y.LastThrowNet = t;
            y.ThrowStart = now;
            y.FaceTarget = victimPos;
            y.HasFaceTarget = true;
            Roar.Play(y.Model.Root.transform.position + Vector3.up * 3f, 1f);
        }

        // ---------------------------------------------------------------- spawning

        /// <summary>Test helper: a yeti pacing across the road just ahead of the car.</summary>
        public static void SpawnInFront()
        {
            var car = Cars.Local;
            Transform tr = car?.Body != null ? car.Body.transform : Camera.main?.transform;
            if (tr == null) { Toast.Show("No car found to spawn in front of."); return; }

            var fwd = tr.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            var axis = Vector3.Cross(Vector3.up, fwd).normalized;
            var center = tr.position + fwd * Settings.SpawnAheadDistance.Value;
            uint id = (uint)Rng.Next(1, int.MaxValue) | 0x80000000u;

            // Start him at the side, about to walk across in ~2 s.
            float pause = Mathf.Max(0f, Settings.PauseTime.Value);
            float phase = -NetTime() + Mathf.Max(0f, pause - 2f);

            Upsert(id, 0, center, axis, phase, 0f);
            SendSpawn(new[] { (id, 0u, center, axis, phase) });
            Toast.Show("Yeti spawned ahead. Time your run!");
        }

        public static void DespawnAll(bool broadcast)
        {
            foreach (var id in new List<uint>(_yetis.Keys)) Remove(id);
            _trailPtr = IntPtr.Zero; // re-spawn trail yetis on the next check
            if (!broadcast) return;
            Net.Begin(Net.Msg.DespawnAll, out var ms);
            Net.Broadcast(ms);
        }

        public static void ClearLocal()
        {
            foreach (var id in new List<uint>(_yetis.Keys)) Remove(id);
            _trailPtr = IntPtr.Zero;
            _peers.Clear();
            Net.ClearPeers();
            Snow.Clear();
        }

        private static void Upsert(uint id, uint group, Vector3 center, Vector3 axis, float phase, float expires)
        {
            if (!_yetis.TryGetValue(id, out var y))
            {
                y = new Yeti { Id = id, Model = new YetiModel($"AbominableSnowman_{id:X8}") };
                y.LastPos = center;
                _yetis[id] = y;
            }
            y.Group = group;
            y.Center = center;
            y.Axis = axis.sqrMagnitude > 0.01f ? axis.normalized : Vector3.right;
            y.Phase = phase;
            y.Expires = expires;
        }

        private static void Remove(uint id)
        {
            if (_yetis.TryGetValue(id, out var y)) y.Model?.Destroy();
            _yetis.Remove(id);
        }

        private static void SendSpawn(IList<(uint id, uint group, Vector3 c, Vector3 a, float ph)> specs)
        {
            var w = Net.Begin(Net.Msg.Spawn, out var ms);
            WriteSpecs(w, specs);
            Net.Broadcast(ms);
        }

        private static void WriteSpecs(BinaryWriter w, IList<(uint id, uint group, Vector3 c, Vector3 a, float ph)> specs)
        {
            w.Write((ushort)specs.Count);
            foreach (var s in specs)
            {
                w.Write(s.id);
                w.Write(s.group);
                Write(w, s.c);
                w.Write(s.a.x);
                w.Write(s.a.z);
                w.Write(s.ph);
            }
        }

        private static void ReadSpecs(BinaryReader r)
        {
            int n = r.ReadUInt16();
            for (int i = 0; i < n; i++)
            {
                uint id = r.ReadUInt32(), group = r.ReadUInt32();
                var c = Read(r);
                var a = new Vector3(r.ReadSingle(), 0f, r.ReadSingle());
                float ph = r.ReadSingle();
                Upsert(id, group, c, a, ph, group == 0 ? 0f : Time.unscaledTime + TrailYetiLifetime);
            }
        }

        // ------------------------------------------------------------- challenge

        private static void TrailUpdate()
        {
            float now = Time.unscaledTime;
            if (now < _nextTrailCheck) return;
            _nextTrailCheck = now + 0.5f;

            ChallengeTrail trail = null;
            try { trail = ChallengeTrail.ActiveTrail; } catch { }
            if (trail == null || trail.WasCollected)
            {
                _trailPtr = IntPtr.Zero;
                _trailSpecs = null;
                TrailStatus = "no active challenge";
                return;
            }

            if (trail.Pointer != _trailPtr)
            {
                _trailPtr = trail.Pointer;
                _trailSpecs = null;
                float frac = Snow.FractionFor(trail);
                bool snowy = frac >= Settings.SnowTrailFraction.Value;
                bool want = Settings.SpawnOnAllTrails.Value || Settings.SpawnOnSnowTrails.Value && snowy;
                TrailStatus = $"challenge {trail.name}: {frac:P0} snow" + (want ? ", yetis on patrol" : ", no yetis");
                if (!want)
                {
                    // Terrain may still be streaming in; look again shortly.
                    if (frac == 0f) _trailPtr = IntPtr.Zero;
                    _nextTrailCheck = now + 3f;
                    return;
                }
                _trailSpecs = BuildTrailSpecs(trail, preferSnow: !Settings.SpawnOnAllTrails.Value);
                _nextKeepalive = 0f;
                Mod.Log.Msg($"{TrailStatus} ({_trailSpecs.Count} yetis)");
            }

            if (_trailSpecs == null) return;
            foreach (var s in _trailSpecs) Upsert(s.id, s.group, s.c, s.a, s.ph, now + TrailYetiLifetime);
            if (now >= _nextKeepalive)
            {
                _nextKeepalive = now + KeepaliveInterval;
                SendSpawn(_trailSpecs);
            }
        }

        private static List<(uint, uint, Vector3, Vector3, float)> BuildTrailSpecs(ChallengeTrail trail, bool preferSnow)
        {
            var specs = new List<(uint, uint, Vector3, Vector3, float)>();
            var wps = trail.waypoints;
            if (wps == null || wps.Count == 0) return specs;

            var pts = new List<Vector3>();
            for (int i = 0; i < wps.Count; i++) if (wps[i] != null) pts.Add(wps[i].position);
            if (pts.Count == 0) return specs;

            // Interior waypoints (not the start or finish), snowy ones first.
            var cand = new List<int>();
            for (int i = 1; i < pts.Count - 1; i++) cand.Add(i);
            if (cand.Count == 0) for (int i = 0; i < pts.Count; i++) cand.Add(i);
            if (preferSnow)
            {
                var snowy = cand.FindAll(i => Snow.SnowWeight(pts[i]) >= 0.4f);
                if (snowy.Count > 0) cand = snowy;
            }

            uint group = trail.DataId != 0 ? trail.DataId : Hash((uint)pts[0].x.GetHashCode(), (uint)pts[0].z.GetHashCode());
            if (group == 0) group = 1;
            int k = Mathf.Clamp(Settings.YetisPerTrail.Value, 0, cand.Count);
            for (int j = 0; j < k; j++)
            {
                int i = cand[(int)((j + 0.5f) * cand.Count / k)];
                var prev = pts[Mathf.Max(0, i - 1)];
                var next = pts[Mathf.Min(pts.Count - 1, i + 1)];
                var along = next - prev; along.y = 0f;
                if (along.sqrMagnitude < 0.01f) along = wps[i].forward;
                var axis = Vector3.Cross(Vector3.up, along.normalized);
                uint id = Hash(group, (uint)i) & 0x7FFFFFFFu;
                float phase = (id % 9973u) / 9973f * 30f;
                specs.Add((id, group, pts[i], axis, phase));
            }
            return specs;
        }

        private static uint Hash(uint a, uint b)
        {
            uint h = 2166136261u;
            foreach (var v in new[] { a, b })
                for (int i = 0; i < 4; i++) { h ^= (v >> (8 * i)) & 0xFF; h *= 16777619u; }
            return h;
        }

        // ------------------------------------------------------------- networking

        private static bool _helloSent;

        private static void ConnectionUpdate()
        {
            var run = Net.Runner();
            var ptr = run?.Pointer ?? IntPtr.Zero;
            if (ptr != _runnerPtr)
            {
                _runnerPtr = ptr;
                _helloSent = false;
                _helloAgainAt = -1f;
                ClearLocal(); // new session (or back to the menu)
            }
            if (!_helloSent && Net.Ready && run != null)
            {
                _helloSent = true;
                if (!run.IsSinglePlayer)
                {
                    // Announce ourselves; the host answers with its yetis + settings.
                    SendHello();
                    _helloAgainAt = Time.unscaledTime + 5f;
                }
            }
            if (_helloAgainAt > 0f && Time.unscaledTime > _helloAgainAt && run != null)
            {
                _helloAgainAt = -1f;
                if (_peers.Count == 0) SendHello();
            }
        }

        private static void SendHello()
        {
            var w = Net.Begin(Net.Msg.Hello, out var ms);
            w.Write(Mod.Version);
            Net.Broadcast(ms);
        }

        private static void SettingsSyncUpdate()
        {
            var cur = CurrentSettings();
            if (_sentSettings == null) { _sentSettings = cur; return; }
            bool changed = false;
            for (int i = 0; i < cur.Length; i++) if (!Mathf.Approximately(cur[i], _sentSettings[i])) changed = true;
            if (changed)
            {
                _sentSettings = cur;
                _settingsChangedAt = Time.unscaledTime; // debounce while dragging sliders
            }
            if (_settingsChangedAt > 0f && Time.unscaledTime - _settingsChangedAt > 0.4f)
            {
                _settingsChangedAt = -1f;
                var w = Net.Begin(Net.Msg.Settings, out var ms);
                WriteSettings(w);
                Net.Broadcast(ms);
            }
        }

        private static float[] CurrentSettings()
        {
            var a = new float[Settings.Synced.Length];
            for (int i = 0; i < a.Length; i++) a[i] = Settings.Synced[i].Value;
            return a;
        }

        private static void WriteSettings(BinaryWriter w)
        {
            w.Write((byte)Settings.Synced.Length);
            foreach (var e in Settings.Synced) w.Write(e.Value);
        }

        private static void ReadSettings(BinaryReader r)
        {
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                float v = r.ReadSingle();
                if (i < Settings.Synced.Length && !float.IsNaN(v) && !float.IsInfinity(v))
                    Settings.Synced[i].Value = v;
            }
            _sentSettings = CurrentSettings(); // don't echo it back
            _settingsChangedAt = -1f;
        }

        private static void OnMessage(Net.Msg type, int sender, BinaryReader r)
        {
            if (sender > 0) _peers.Add(sender);
            switch (type)
            {
                case Net.Msg.Hello:
                {
                    var run = Net.Runner();
                    if (!Net.IsAuthority(run)) break;
                    var w = Net.Begin(Net.Msg.Snapshot, out var ms);
                    WriteSettings(w);
                    var manual = new List<(uint, uint, Vector3, Vector3, float)>();
                    foreach (var y in _yetis.Values)
                        if (y.Group == 0) manual.Add((y.Id, 0u, y.Center, y.Axis, y.Phase));
                    WriteSpecs(w, manual);
                    Net.SendTo(sender, ms);
                    Mod.Log.Msg($"Player {sender} has the Abominable Snowman mod; sent {manual.Count} yetis + settings.");
                    break;
                }
                case Net.Msg.Snapshot:
                    ReadSettings(r);
                    ReadSpecs(r);
                    Toast.Show("Abominable Snowman: synced with host");
                    break;
                case Net.Msg.Settings:
                    ReadSettings(r);
                    break;
                case Net.Msg.Spawn:
                    ReadSpecs(r);
                    break;
                case Net.Msg.DespawnAll:
                    DespawnAll(false);
                    break;
                case Net.Msg.Throw:
                {
                    uint id = r.ReadUInt32();
                    int victim = r.ReadInt32();
                    var vel = Read(r);
                    var spin = Read(r);
                    float t = r.ReadSingle();
                    var car = Cars.ByPlayer(victim);
                    if (car != null && car.HasStateAuthority && !car.IsLocal) Cars.Apply(car, vel, spin);
                    _yetis.TryGetValue(id, out var y);
                    OnThrown(y, car?.Body != null ? car.Body.position : Vector3.zero, t, Time.unscaledTime);
                    if (y != null && car?.Body == null) y.HasFaceTarget = false;
                    Toast.Show($"Player {victim} got thrown by the Abominable Snowman!");
                    break;
                }
            }
        }

        private static void Write(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static Vector3 Read(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    }
}
