// Where a game runs: hosted on Tupic (WebGL inside the games-core play frame, values from the page) or standalone /
// in the Editor (no host; sign in with TupicAuth, or play as a guest).
using System.Runtime.InteropServices;

namespace Tupic.Unity
{
    public static class TupicHost
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string TupicBridge_Get(string name);
        [DllImport("__Internal")] static extern string TupicBridge_Language();
        public static bool IsHosted => !string.IsNullOrEmpty(TupicBridge_Get("gameId"));
        public static string Get(string name) => TupicBridge_Get(name);
        public static string Language => TupicBridge_Language();
#else
        public static bool IsHosted => false;
        public static string Get(string name) => "";
        public static string Language => System.Globalization.CultureInfo.CurrentCulture.Name;
#endif
        public static string Token => Get("token");
        public static string GameId => Get("gameId");
    }
}
