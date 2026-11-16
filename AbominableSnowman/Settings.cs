using System;
using MelonLoader;
using UnityEngine.InputSystem;

namespace AbominableSnowman
{
    /// <summary>
    /// User settings, stored in UserData/MelonPreferences.cfg [AbominableSnowman].
    /// The "behaviour" group is synced across every player in the session who
    /// has the mod (last change wins; the host's values are sent to joiners).
    /// </summary>
    internal static class Settings
    {
        internal static MelonPreferences_Category Cat;

        // Local-only
        internal static MelonPreferences_Entry<string> MenuKey;
        internal static MelonPreferences_Entry<bool> SpawnOnSnowTrails;
        internal static MelonPreferences_Entry<bool> SpawnOnAllTrails;
        internal static MelonPreferences_Entry<string> SnowKeywords;
        internal static MelonPreferences_Entry<float> SnowTrailFraction;
        internal static MelonPreferences_Entry<int> YetisPerTrail;
        internal static MelonPreferences_Entry<float> SpawnAheadDistance;
        internal static MelonPreferences_Entry<float> RoarVolume;
        internal static MelonPreferences_Entry<bool> ShowToasts;
        internal static MelonPreferences_Entry<bool> MultiplayerSync;
        internal static MelonPreferences_Entry<float> FurDensity;
        internal static MelonPreferences_Entry<bool> FurMovement;

        // Synced behaviour (order matters: it is the wire order)
        internal static MelonPreferences_Entry<float> Size;
        internal static MelonPreferences_Entry<float> WalkSpeed;
        internal static MelonPreferences_Entry<float> PatrolHalfWidth;
        internal static MelonPreferences_Entry<float> PauseTime;
        internal static MelonPreferences_Entry<float> Reach;
        internal static MelonPreferences_Entry<float> ThrowDistanceFeet;
        internal static MelonPreferences_Entry<float> LaunchAngle;
        internal static MelonPreferences_Entry<float> Tumble;
        internal static MelonPreferences_Entry<float> Cooldown;

        internal static MelonPreferences_Entry<float>[] Synced;

        public static void Init()
        {
            Cat = MelonPreferences.CreateCategory("AbominableSnowman");

            MenuKey = Cat.CreateEntry("MenuKey", "F6",
                description: "Key that opens the Abominable Snowman menu (Unity Input System key name).");
            SpawnOnSnowTrails = Cat.CreateEntry("SpawnOnSnowTrails", true,
                description: "Put yetis on challenge trails that run through snow.");
            SpawnOnAllTrails = Cat.CreateEntry("SpawnOnAllTrails", false,
                description: "Put yetis on every challenge trail, snowy or not.");
            SnowKeywords = Cat.CreateEntry("SnowKeywords", "snow,ice,glacier,frost",
                description: "Comma-separated words; terrain layers whose name contains one count as snow.");
            SnowTrailFraction = Cat.CreateEntry("SnowTrailFraction", 0.3f,
                description: "Fraction of a trail's waypoints that must be on snow for it to count as a snow trail.");
            YetisPerTrail = Cat.CreateEntry("YetisPerTrail", 3,
                description: "How many yetis guard each snow challenge trail.");
            SpawnAheadDistance = Cat.CreateEntry("SpawnAheadDistance", 35f,
                description: "Distance (m) ahead of the car for the 'spawn in front' test button.");
            RoarVolume = Cat.CreateEntry("RoarVolume", 1.0f,
                description: "Volume of the yeti roar (0-1.5).");
            ShowToasts = Cat.CreateEntry("ShowToasts", true,
                description: "Show on-screen messages when someone gets thrown.");
            FurDensity = Cat.CreateEntry("FurDensity", 1.0f,
                description: "Amount of hair on the yeti (0 = none, 1 = normal, 2 = extra shaggy). Applies to newly spawned yetis.");
            FurMovement = Cat.CreateEntry("FurMovement", true,
                description: "Animate the yeti's hair (sway, wind, bristling).");
            MultiplayerSync = Cat.CreateEntry("MultiplayerSync", true,
                description: "Sync yetis with other players who have the mod (takes effect on the next map load).");

            Size = Cat.CreateEntry("Size", 1.0f, description: "[synced] Yeti size multiplier (1 = about 3.3 m tall).");
            WalkSpeed = Cat.CreateEntry("WalkSpeed", 3.0f, description: "[synced] Yeti walking speed across the trail (m/s).");
            PatrolHalfWidth = Cat.CreateEntry("PatrolHalfWidth", 9f, description: "[synced] How far either side of the trail the yeti walks (m).");
            PauseTime = Cat.CreateEntry("PauseTime", 1.5f, description: "[synced] Seconds the yeti waits at each side before crossing back.");
            Reach = Cat.CreateEntry("Reach", 3.2f, description: "[synced] Grab radius (m, scaled by size).");
            ThrowDistanceFeet = Cat.CreateEntry("ThrowDistanceFeet", 200f, description: "[synced] How far a caught car is thrown (feet, on flat ground).");
            LaunchAngle = Cat.CreateEntry("LaunchAngle", 40f, description: "[synced] Throw launch angle above horizontal (degrees).");
            Tumble = Cat.CreateEntry("Tumble", 2.5f, description: "[synced] Spin given to a thrown car (rad/s).");
            Cooldown = Cat.CreateEntry("Cooldown", 6f, description: "[synced] Seconds a yeti rests after a throw before he can grab again.");

            Synced = new[] { Size, WalkSpeed, PatrolHalfWidth, PauseTime, Reach, ThrowDistanceFeet, LaunchAngle, Tumble, Cooldown };
        }

        public static void Save() => Cat.SaveToFile(false);

        public static void ResetToDefaults()
        {
            foreach (var e in Cat.Entries) e.ResetToDefault();
        }

        public static bool Pressed(MelonPreferences_Entry<string> entry)
        {
            var kb = Keyboard.current;
            if (kb == null || !Enum.TryParse(entry.Value, true, out Key key) || key == Key.None) return false;
            return kb[key].wasPressedThisFrame;
        }
    }
}
