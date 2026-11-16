using MelonLoader;
using UnityEngine;

namespace AbominableSnowman
{
    /// <summary>
    /// IMGUI settings window, toggled with MenuKey (default F6). Behaviour sliders
    /// are synced to every modded player; changes save on Save/Close.
    /// </summary>
    internal static class Menu
    {
        public static bool IsOpen { get; private set; }

        private const float W = 540f, RowH = 26f, Pad = 12f, LabelW = 200f, ValueW = 70f;
        private static Vector2 _scroll;
        private static float _contentH = 900f, _y;
        private static bool _dirty;
        private static CursorLockMode _prevLock;
        private static bool _prevVisible;

        public static void Update()
        {
            if (Settings.Pressed(Settings.MenuKey)) Toggle();
            if (IsOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private static void Toggle()
        {
            IsOpen = !IsOpen;
            if (IsOpen)
            {
                _prevLock = Cursor.lockState;
                _prevVisible = Cursor.visible;
            }
            else
            {
                if (_dirty) { Settings.Save(); _dirty = false; }
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevVisible;
            }
        }

        public static void Draw()
        {
            if (!IsOpen) return;

            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float viewH = Mathf.Min(Screen.height / scale - 80f, _contentH + 90f);
            var win = new Rect(Screen.width / scale - W - 40f, 40f, W, viewH);

            GUI.Box(win, "");
            GUI.Box(win, "");
            GUI.Label(new Rect(win.x + Pad, win.y + 8f, W - 2 * Pad, RowH),
                $"<b>Abominable Snowman</b>   ({Settings.MenuKey.Value} to close)");

            var view = new Rect(win.x, win.y + 38f, W, viewH - 38f - 44f);
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0f, 0f, W - 24f, _contentH));
            _y = 0f;

            Section("Test");
            if (Button("Spawn yeti in front of car")) Yetis.SpawnInFront();
            if (Button("Despawn all yetis (everyone)")) Yetis.DespawnAll(true);
            if (Button("Test roar")) Roar.Play(Camera.main != null ? Camera.main.transform.position : Vector3.zero, 1f);
            Slider(Settings.SpawnAheadDistance, "Spawn distance ahead (m)", 10f, 100f, "F0");

            Section("Status");
            Info("Network: " + Net.Describe());
            Info("Yetis: " + Yetis.Count + "   " + Yetis.TrailStatus);
            var car = Cars.Local;
            Info("Ground under car: " + (car?.Body != null ? Snow.Describe(car.Body.position) : "no car"));

            Section("Behaviour (synced to all players with the mod)");
            Slider(Settings.ThrowDistanceFeet, "Throw distance (ft)", 25f, 600f, "F0");
            Slider(Settings.LaunchAngle, "Launch angle (°)", 15f, 75f, "F0");
            Slider(Settings.Tumble, "Tumble (rad/s)", 0f, 10f, "F1");
            Slider(Settings.WalkSpeed, "Walk speed (m/s)", 0.5f, 10f, "F1");
            Slider(Settings.PatrolHalfWidth, "Patrol half-width (m)", 3f, 25f, "F1");
            Slider(Settings.PauseTime, "Pause at each side (s)", 0f, 8f, "F1");
            Slider(Settings.Reach, "Grab reach (m)", 1f, 8f, "F1");
            Slider(Settings.Cooldown, "Rest after a throw (s)", 1f, 30f, "F0");
            Slider(Settings.Size, "Size", 0.5f, 3f, "F2");

            Section("Challenge trails (this player)");
            Toggle(Settings.SpawnOnSnowTrails, "Yetis on snow challenge trails");
            Toggle(Settings.SpawnOnAllTrails, "Yetis on every challenge trail");
            IntSlider(Settings.YetisPerTrail, "Yetis per trail", 1, 10);
            Slider(Settings.SnowTrailFraction, "Snow needed on trail", 0.05f, 1f, "P0");
            Info("Snow layer words: " + Settings.SnowKeywords.Value + "  (edit in MelonPreferences.cfg)");

            Section("Looks (this player)");
            Slider(Settings.FurDensity, "Hair amount (new yetis)", 0f, 2f, "F2");
            Toggle(Settings.FurMovement, "Moving hair");

            Section("Other");
            Slider(Settings.RoarVolume, "Roar volume", 0f, 1.5f, "F2");
            Toggle(Settings.ShowToasts, "On-screen messages");
            Toggle(Settings.MultiplayerSync, "Sync yetis with other players (next map load)");
            if (CrashGuard.DisabledStages.Count > 0)
            {
                Info("Switched off after the game crashed in it: " + string.Join(", ", CrashGuard.DisabledStages));
                if (Button("Re-enable (restart the game)")) CrashGuard.ReEnableAll();
            }
            Info("Own roar: put roar*.wav/ogg/mp3 in UserData/AbominableSnowman");

            _contentH = _y + Pad;
            GUI.EndScrollView();

            float by = win.yMax - 36f;
            if (GUI.Button(new Rect(win.x + Pad, by, 110f, 26f), "Save")) { Settings.Save(); _dirty = false; }
            if (GUI.Button(new Rect(win.x + Pad + 120f, by, 140f, 26f), "Reset defaults")) { Settings.ResetToDefaults(); _dirty = true; }
            if (GUI.Button(new Rect(win.xMax - Pad - 110f, by, 110f, 26f), "Close")) Toggle();

            GUI.matrix = Matrix4x4.identity;
        }

        private static void Section(string title)
        {
            _y += 8f;
            GUI.Label(new Rect(Pad, _y, W, RowH), $"<b><color=#9fd3ff>{title}</color></b>");
            _y += RowH;
        }

        private static void Info(string text)
        {
            GUI.Label(new Rect(Pad + 8f, _y, W - 3 * Pad, RowH), text);
            _y += RowH;
        }

        private static bool Button(string text)
        {
            bool hit = GUI.Button(new Rect(Pad + 8f, _y + 2f, 260f, RowH - 4f), text);
            _y += RowH;
            return hit;
        }

        private static void Toggle(MelonPreferences_Entry<bool> e, string label)
        {
            bool v = GUI.Toggle(new Rect(Pad + 8f, _y, W - 3 * Pad, RowH), e.Value, " " + label);
            if (v != e.Value) { e.Value = v; _dirty = true; }
            _y += RowH;
        }

        private static float SliderRow(string label, float value, float min, float max, string shown)
        {
            GUI.Label(new Rect(Pad + 8f, _y, LabelW, RowH), label);
            float sx = Pad + 8f + LabelW;
            float sw = W - 24f - sx - ValueW - Pad;
            float v = GUI.HorizontalSlider(new Rect(sx, _y + 8f, sw, RowH), value, min, max);
            GUI.Label(new Rect(sx + sw + 8f, _y, ValueW, RowH), shown);
            _y += RowH;
            return v;
        }

        private static void Slider(MelonPreferences_Entry<float> e, string label, float min, float max, string fmt)
        {
            float v = SliderRow(label, e.Value, min, max, e.Value.ToString(fmt));
            if (!Mathf.Approximately(v, e.Value)) { e.Value = v; _dirty = true; }
        }

        private static void IntSlider(MelonPreferences_Entry<int> e, string label, int min, int max)
        {
            int v = Mathf.RoundToInt(SliderRow(label, e.Value, min, max, e.Value.ToString()));
            if (v != e.Value) { e.Value = v; _dirty = true; }
        }
    }
}
