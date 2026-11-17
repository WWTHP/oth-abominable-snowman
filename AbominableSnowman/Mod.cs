using MelonLoader;

[assembly: MelonInfo(typeof(AbominableSnowman.Mod), "AbominableSnowman", AbominableSnowman.Mod.Version, "jims")]
[assembly: MelonGame(null, "over the hill")]

namespace AbominableSnowman
{
    /// <summary>
    /// Adds the Abominable Snowman to snowy challenge trails. He paces back and
    /// forth across the trail; time your run past him, or he grabs your car and
    /// hurls it a couple hundred feet in a random direction. Synced with every
    /// player in the session who has the mod. F6 opens the menu.
    /// </summary>
    public class Mod : MelonMod
    {
        public const string Version = "0.1.5";
        internal static MelonLogger.Instance Log;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Settings.Init();
            CrashGuard.Init();
            Roar.Prepare();
            Net.Init();
            Yetis.Init();
            Log.Msg($"Abominable Snowman loaded. {Settings.MenuKey.Value} = menu. Sync: {Net.Status}");
        }

        public override void OnUpdate()
        {
            Menu.Update();
            Net.EnsureHooked();
            Net.ProcessInbox();
            Cars.Update();
            Yetis.Update();
        }

        public override void OnApplicationQuit() => Net.Unhook();

        public override void OnGUI()
        {
            Toast.Draw();
            Menu.Draw();
        }
    }
}
