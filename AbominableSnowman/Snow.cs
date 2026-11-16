using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;

namespace AbominableSnowman
{
    /// <summary>
    /// Decides whether a challenge trail is a snow trail by sampling the terrain
    /// texture (splat) layers under its waypoints. Canada's mountains paint snow
    /// with layers like "region1Snow" / "region1snowdeep".
    /// </summary>
    internal static class Snow
    {
        private static readonly Dictionary<IntPtr, float> TrailFraction = new();
        private static bool _warned;

        public static void Clear() => TrailFraction.Clear();

        /// <summary>Fraction (0..1) of the trail's waypoints that sit on snow.</summary>
        public static float FractionFor(ChallengeTrail trail)
        {
            if (TrailFraction.TryGetValue(trail.Pointer, out var f)) return f;
            var wps = trail.waypoints;
            if (wps == null || wps.Count == 0) return 0f;
            int snowy = 0, n = 0;
            for (int i = 0; i < wps.Count; i++)
            {
                var t = wps[i];
                if (t == null) continue;
                n++;
                if (SnowWeight(t.position) >= 0.4f) snowy++;
            }
            f = n == 0 ? 0f : (float)snowy / n;
            // Terrain tiles stream in; only cache a confident answer.
            if (f > 0f || n > 0 && TerrainAt(wps[0].position) != null) TrailFraction[trail.Pointer] = f;
            return f;
        }

        /// <summary>Sum of the weights of snow-like terrain layers at a world point (0..1).</summary>
        public static float SnowWeight(Vector3 pos)
        {
            var layers = LayersAt(pos);
            if (layers == null) return 0f;
            var words = Settings.SnowKeywords.Value.ToLowerInvariant()
                .Split(',', StringSplitOptions.RemoveEmptyEntries);
            float sum = 0f;
            foreach (var (name, w) in layers)
            {
                var n = name.ToLowerInvariant();
                foreach (var word in words)
                    if (n.Contains(word.Trim())) { sum += w; break; }
            }
            return Mathf.Clamp01(sum);
        }

        /// <summary>Human-readable dominant terrain layer at a point (for the menu).</summary>
        public static string Describe(Vector3 pos)
        {
            var layers = LayersAt(pos);
            if (layers == null) return "no terrain";
            string best = "?";
            float bw = -1f;
            foreach (var (name, w) in layers)
                if (w > bw) { bw = w; best = name; }
            return $"{best} ({bw:P0}), snow {SnowWeight(pos):P0}";
        }

        public static Terrain TerrainAt(Vector3 pos)
        {
            var all = Terrain.activeTerrains;
            if (all == null) return null;
            foreach (var t in all)
            {
                if (t == null || t.terrainData == null) continue;
                var o = t.GetPosition();
                var s = t.terrainData.size;
                if (pos.x >= o.x && pos.x <= o.x + s.x && pos.z >= o.z && pos.z <= o.z + s.z) return t;
            }
            return null;
        }

        private static unsafe List<(string, float)> LayersAt(Vector3 pos)
        {
            try
            {
                var t = TerrainAt(pos);
                if (t == null) return null;
                var td = t.terrainData;
                var tl = td.terrainLayers;
                int count = td.alphamapLayers;
                if (tl == null || count <= 0) return null;

                var o = t.GetPosition();
                var s = td.size;
                int res = td.alphamapWidth;
                int x = Mathf.Clamp((int)((pos.x - o.x) / s.x * res), 0, res - 1);
                int z = Mathf.Clamp((int)((pos.z - o.z) / s.z * td.alphamapHeight), 0, td.alphamapHeight - 1);

                // float[1,1,count] returned as a raw il2cpp multi-dim array:
                // header (klass, monitor, bounds, max_length) = 32 bytes, then data.
                var arr = td.GetAlphamaps(x, z, 1, 1);
                if (arr == null) return null;
                float* data = (float*)((byte*)arr.Pointer + 32);
                var list = new List<(string, float)>(count);
                for (int i = 0; i < count && i < tl.Length; i++)
                {
                    var layer = tl[i];
                    string name = layer == null ? "layer" + i : layer.name;
                    if (layer != null && layer.diffuseTexture != null) name += "/" + layer.diffuseTexture.name;
                    list.Add((name, data[i]));
                }
                return list;
            }
            catch (Exception ex)
            {
                if (!_warned) Mod.Log.Warning("Terrain sampling failed: " + ex);
                _warned = true;
                return null;
            }
        }
    }
}
