using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace GTagCameraMod
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.ggrrk.gtagcameramod";
        public const string PluginName = "GTagCameraMod";
        public const string PluginVersion = "0.7.0";

        internal static ManualLogSource Log = null!;

        private GameObject? _menuRoot;
        private float _menuSpawnAt;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"{PluginName} v{PluginVersion} loading. Menu will spawn after 5 seconds.");

            _menuSpawnAt = Time.time + 5f;
        }

        private void Update()
        {
            // Spawn the menu once the game has had a moment to set up Camera.main
            if (_menuRoot == null && Time.time >= _menuSpawnAt && Camera.main != null)
            {
                _menuRoot = new GameObject("GTagCameraMod_Menu");
                DontDestroyOnLoad(_menuRoot);
                _menuRoot.AddComponent<FovMenu>();
                Log.LogInfo("Floating FOV menu spawned.");
            }

            // Keyboard fallback in case hand-touch doesn't fire in-game
            FovMenu.HandleKeyboardFallback();
        }
    }
}
