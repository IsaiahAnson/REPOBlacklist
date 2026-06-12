using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace REPOBlacklist
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "isaia.repoblacklist";
        public const string NAME = "REPOBlacklist";
        public const string VERSION = "0.2.2";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static BlacklistStore Store { get; private set; }

        public ConfigEntry<KeyCode> MenuToggleKey;

        private Harmony _harmony;
        private BlacklistUI _ui;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            MenuToggleKey = Config.Bind(
                "UI", "MenuToggleKey", KeyCode.F3,
                "Key that opens/closes the in-game blacklist menu.");

            var storePath = Path.Combine(Paths.ConfigPath, "REPOBlacklist.json");
            Store = new BlacklistStore(storePath);
            Store.Changed += OnBlacklistChanged;

            _harmony = new Harmony(GUID);
            try { _harmony.PatchAll(Assembly.GetExecutingAssembly()); }
            catch (Exception e) { Log.LogWarning($"Harmony patch issues (non-fatal): {e.Message}"); }
            InputBlocker.PatchInputSystem(_harmony);

            _ui = gameObject.AddComponent<BlacklistUI>();

            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.LogInfo($"{NAME} {VERSION} loaded. Blacklist: {storePath}");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
            if (Store != null) Store.Changed -= OnBlacklistChanged;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Defer one frame so directors finish their own Awake before we filter.
            StartCoroutine(ApplyNextFrame());
        }

        private System.Collections.IEnumerator ApplyNextFrame()
        {
            yield return null;
            BlacklistFilter.Apply();
        }

        private void OnBlacklistChanged()
        {
            BlacklistFilter.Apply();
        }
    }
}
