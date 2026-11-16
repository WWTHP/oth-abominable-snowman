using System;
using System.Collections.Generic;
using System.IO;
using Il2CppFMOD;
using Il2CppFMODUnity;
using MelonLoader.Utils;
using UnityEngine;

namespace AbominableSnowman
{
    /// <summary>
    /// The yeti's roar, played through FMOD's core API as a 3D sound.
    ///
    /// On first run three roar variations are synthesized into
    /// UserData/AbominableSnowman/ (roar_generated_N.wav). To use your own sound,
    /// drop any roar*.wav / roar*.ogg / roar*.mp3 there: if any such file exists,
    /// only your files are used (one is picked at random each time).
    /// </summary>
    internal static class Roar
    {
        private const int SynthVersion = 2;
        private const string GeneratedPrefix = "roar_generated_";
        private const int Variants = 3;

        private static readonly List<Sound> Sounds = new();
        private static bool _tried;
        private static readonly System.Random Pick = new();

        public static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "AbominableSnowman");
        private static string VersionPath => Path.Combine(Dir, GeneratedPrefix + "version.txt");

        /// <summary>Writes the generated roars if missing or out of date (call at startup).</summary>
        public static void Prepare()
        {
            try
            {
                Directory.CreateDirectory(Dir);

                // v0.1.0 wrote a single synthesized roar.wav (101474 bytes): replace it.
                var old = Path.Combine(Dir, "roar.wav");
                if (File.Exists(old) && new FileInfo(old).Length == 101474) File.Delete(old);

                bool current = File.Exists(VersionPath) && File.ReadAllText(VersionPath).Trim() == SynthVersion.ToString();
                for (int i = 1; i <= Variants; i++)
                {
                    var path = Path.Combine(Dir, $"{GeneratedPrefix}{i}.wav");
                    if (!current || !File.Exists(path)) File.WriteAllBytes(path, Synthesize(i));
                }
                File.WriteAllText(VersionPath, SynthVersion.ToString());
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Could not write roar sounds: " + ex.Message);
            }
        }

        public static void Play(Vector3 at, float volume)
        {
            float vol = Mathf.Clamp(Settings.RoarVolume.Value, 0f, 1.5f) * volume;
            if (vol <= 0f || !Ensure()) return;
            try
            {
                var core = RuntimeManager.CoreSystem;
                core.getMasterChannelGroup(out var group);
                var sound = Sounds[Pick.Next(Sounds.Count)];
                if (core.playSound(sound, group, true, out var ch) != RESULT.OK) return;
                var pos = at.ToFMODVector();
                var vel = Vector3.zero.ToFMODVector();
                ch.set3DAttributes(ref pos, ref vel);
                ch.set3DMinMaxDistance(8f, 260f);
                ch.setVolume(vol);
                ch.setPitch(0.92f + (float)Pick.NextDouble() * 0.14f);
                ch.setPaused(false);
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Roar failed: " + ex.Message);
            }
        }

        private static bool Ensure()
        {
            if (Sounds.Count > 0) return true;
            if (_tried) return false;
            _tried = true;
            try
            {
                Prepare();
                var user = new List<string>();
                var generated = new List<string>();
                foreach (var f in Directory.GetFiles(Dir, "roar*"))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".wav" && ext != ".ogg" && ext != ".mp3") continue;
                    (Path.GetFileName(f).StartsWith(GeneratedPrefix) ? generated : user).Add(f);
                }
                var files = user.Count > 0 ? user : generated;
                const MODE mode = MODE._3D | MODE.CREATESAMPLE | MODE.LOOP_OFF | MODE._3D_LINEARSQUAREROLLOFF;
                foreach (var f in files)
                {
                    var r = RuntimeManager.CoreSystem.createSound(f, mode, out var s);
                    if (r == RESULT.OK) Sounds.Add(s);
                    else Mod.Log.Warning($"FMOD could not load {f}: {r}");
                }
                if (user.Count > 0) Mod.Log.Msg($"Using {Sounds.Count} custom roar sound(s).");
            }
            catch (Exception ex)
            {
                Mod.Log.Warning("Roar unavailable: " + ex.Message);
            }
            return Sounds.Count > 0;
        }

        // ================================================================ synthesis

        private const int Rate = 44100;

        /// <summary>
        /// A large-creature roar: a short grunted intake, then a rising roar that
        /// breaks into a growl (period doubling) and sags into a snarl. Three detuned
        /// rough voices through four low formants (big vocal tract), pulse-synced rasp,
        /// breath noise, chest rumble, saturation and a mountain-echo reverb.
        /// </summary>
        private static byte[] Synthesize(int variant)
        {
            var rng = new System.Random(7919 * variant + 17);
            float R() => (float)rng.NextDouble();

            float len = 2.6f + 0.5f * R();
            float peakAt = 0.28f + 0.12f * R();         // where the pitch peaks (0..1)
            float peakF0 = 105f + 30f * R();
            float tailLen = 1.4f;                        // reverb tail
            int n = (int)(Rate * (len + tailLen));
            var dry = new float[n];

            // Voices: slight detune = "many throats" thickness.
            float[] detune = { 1f, 1.009f, 0.991f };
            float[] vgain = { 1f, 0.55f, 0.55f };
            var phase = new double[3];
            double subPhase = 0;

            // Formants (Hz) at the start → at full roar, scaled down for a huge tract.
            const float tract = 0.72f;
            float[] fA = { 320f, 820f, 2300f, 3200f };
            float[] fB = { 700f, 1150f, 2500f, 3400f };
            float[] fq = { 5f, 7f, 9f, 10f };
            float[] fg = { 1f, 0.7f, 0.32f, 0.18f };
            var bp = new Biquad[4];
            var breath = new Biquad();
            var rumbleLp = new OnePole();
            float drift = 0f, driftTarget = 0f, shimmer = 1f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                if (t > len) { dry[i] = 0f; continue; }
                float x = t / len;

                // --- loudness envelope: grunt (0-0.15 s), swell, sustain, decay
                float grunt = t < 0.16f ? Mathf.Sin(t / 0.16f * Mathf.PI) * 0.55f : 0f;
                float swell = Smooth(Mathf.InverseLerp(0.12f, 0.12f + len * 0.18f, t));
                float decay = 1f - Smooth(Mathf.InverseLerp(len * 0.62f, len, t));
                float env = Mathf.Max(grunt, swell * decay);

                // --- pitch contour + slow drift + per-cycle jitter
                if (i % 441 == 0) driftTarget = (R() - 0.5f) * 0.08f;
                drift += (driftTarget - drift) * 0.002f;
                float rise = Smooth(Mathf.Clamp01(x / peakAt));
                float fall = Smooth(Mathf.Clamp01((x - peakAt) / (1f - peakAt)));
                float f0 = t < 0.16f ? 70f : Mathf.Lerp(Mathf.Lerp(62f, peakF0, rise), 58f, fall * 0.85f);
                f0 *= 1f + drift + 0.012f * Mathf.Sin(2f * Mathf.PI * 5.5f * t); // drift + tremble

                // growl: strongest around the loud middle
                float growl = Mathf.Clamp01(swell * decay * 1.3f - 0.25f) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.9f * t + variant));

                // --- source: detuned polyBLEP saws with jitter/shimmer
                float src = 0f;
                for (int v = 0; v < 3; v++)
                {
                    double inc = f0 * detune[v] * (1f + (R() - 0.5f) * 0.03f) / Rate;
                    phase[v] += inc;
                    if (phase[v] >= 1.0) { phase[v] -= 1.0; if (v == 0) shimmer = 1f + (R() - 0.5f) * 0.35f; }
                    src += (float)(SawBlep(phase[v], inc) * vgain[v]);
                }
                src *= shimmer;

                // period doubling: every other cycle louder → sub-octave crackle
                subPhase += f0 * 0.5 / Rate;
                if (subPhase >= 1.0) subPhase -= 1.0;
                float sub = subPhase < 0.5 ? 1f : -1f;
                src *= 1f + growl * 0.7f * sub;

                // pulse-synced rasp (aspiration riding the glottal pulse)
                float noise = R() * 2f - 1f;
                float pulse = phase[0] < 0.25 ? 1f - (float)phase[0] * 4f : 0f;
                float rasp = noise * pulse * (0.35f + growl * 0.6f);

                // --- vocal tract
                float open = Smooth(Mathf.Clamp01(x / (peakAt + 0.1f))) * (1f - 0.4f * fall); // jaw opens then closes a bit
                float voiced = 0f;
                for (int k = 0; k < 4; k++)
                {
                    float fc = Mathf.Lerp(fA[k], fB[k], open) * tract;
                    bp[k].SetBandpass(fc, fq[k]);
                    voiced += bp[k].Process(src + rasp) * fg[k];
                }

                // breath: band-passed noise, more at the start and end
                breath.SetBandpass(1400f * tract, 1.2f);
                float br = breath.Process(noise) * (0.18f + 0.25f * (1f - swell) + 0.15f * fall);

                // chest rumble: low sine at f0 and f0/2
                float rumble = rumbleLp.Process(Mathf.Sin((float)(phase[0] * 2 * Math.PI)) * 0.6f + sub * 0.25f * growl, 0.05f);

                float y = voiced * 1.4f + br + rumble * 0.9f;
                y *= env;
                // flutter of the growl
                y *= 1f - growl * 0.35f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (26f + 6f * R()) * t));
                dry[i] = (float)Math.Tanh(y * 2.2f);
            }

            var wet = Reverb(dry);
            var outp = new float[n];
            float peak = 1e-4f;
            var dc = new OnePole();
            for (int i = 0; i < n; i++)
            {
                float s = dry[i] * 0.85f + wet[i] * 0.32f;
                s -= dc.Process(s, 0.004f); // remove DC / sub-sonic
                outp[i] = s;
                peak = Mathf.Max(peak, Mathf.Abs(s));
            }

            var pcm = new short[n];
            for (int i = 0; i < n; i++)
            {
                float fade = i > n - Rate / 10 ? (n - i) / (Rate / 10f) : 1f;
                pcm[i] = (short)(outp[i] / peak * 0.92f * fade * short.MaxValue);
            }
            return Wav(pcm);
        }

        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Band-limited sawtooth (polyBLEP) in [-1, 1].</summary>
        private static double SawBlep(double p, double dt)
        {
            double v = 2.0 * p - 1.0;
            if (p < dt) { double t = p / dt; v -= t + t - t * t - 1.0; }
            else if (p > 1.0 - dt) { double t = (p - 1.0) / dt; v -= t * t + t + t + 1.0; }
            return v;
        }

        /// <summary>Schroeder reverb: four damped combs into two all-passes.</summary>
        private static float[] Reverb(float[] input)
        {
            int[] combs = { 1557, 1617, 1491, 1422 };
            int[] alls = { 556, 225 };
            var outp = new float[input.Length];
            foreach (var d in combs)
            {
                var buf = new float[d];
                int idx = 0;
                float lp = 0f;
                for (int i = 0; i < input.Length; i++)
                {
                    float o = buf[idx];
                    lp = o * 0.6f + lp * 0.4f; // damping
                    buf[idx] = input[i] + lp * 0.82f;
                    if (++idx >= d) idx = 0;
                    outp[i] += o * 0.25f;
                }
            }
            foreach (var d in alls)
            {
                var buf = new float[d];
                int idx = 0;
                for (int i = 0; i < outp.Length; i++)
                {
                    float b = buf[idx];
                    float o = -outp[i] + b;
                    buf[idx] = outp[i] + b * 0.5f;
                    if (++idx >= d) idx = 0;
                    outp[i] = o;
                }
            }
            return outp;
        }

        private static byte[] Wav(short[] pcm)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            int n = pcm.Length;
            w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(n * 2);
            foreach (var s in pcm) w.Write(s);
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>RBJ band-pass (constant 0 dB peak), coefficients settable per sample.</summary>
        private struct Biquad
        {
            private float _b0, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public void SetBandpass(float freq, float q)
            {
                float w0 = 2f * Mathf.PI * Mathf.Min(freq, Rate * 0.45f) / Rate;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float a0 = 1f + alpha;
                _b0 = alpha / a0;
                _b2 = -alpha / a0;
                _a1 = -2f * Mathf.Cos(w0) / a0;
                _a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }

        private struct OnePole
        {
            private float _z;
            public float Process(float x, float k) => _z += (x - _z) * k;
        }
    }
}
