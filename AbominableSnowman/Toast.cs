using UnityEngine;

namespace AbominableSnowman
{
    /// <summary>Short on-screen messages at the top of the screen.</summary>
    internal static class Toast
    {
        private static string _text;
        private static float _until;

        public static void Show(string text, float seconds = 3.5f)
        {
            _text = text;
            _until = Time.unscaledTime + seconds;
            Mod.Log.Msg(text);
        }

        public static void Draw()
        {
            if (_text == null || Time.unscaledTime > _until || !Settings.ShowToasts.Value) return;
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float w = 640f, x = (Screen.width / scale - w) * 0.5f;
            var r = new Rect(x, 90f, w, 40f);
            GUI.Box(r, "");
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 14f, r.y + 10f, w - 28f, 24f), $"<b><color=#cfe8ff>{_text}</color></b>");
            GUI.matrix = Matrix4x4.identity;
        }
    }
}
