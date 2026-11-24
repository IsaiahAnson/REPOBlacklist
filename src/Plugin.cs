using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace REPOBlacklist
{
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "isaia.repoblacklist";
        public const string NAME = "REPOBlacklist";
        // Written by the build from version_number in manifest.json (see REPOBlacklist.csproj).
        public const string VERSION = PluginVersion.Value;

        private const string StoreFileName = "REPOBlacklist.json";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static BlacklistStore Store { get; private set; }

        public ConfigEntry<KeyCode> MenuToggleKey;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            MenuToggleKey = Config.Bind(
                "UI", "MenuToggleKey", KeyCode.F3,
                "Key that opens/closes the in-game blacklist menu.");

            Store = new BlacklistStore(Path.Combine(Paths.ConfigPath, StoreFileName), Log);

            _harmony = new Harmony(GUID);
            BlacklistFilter.Install(_harmony);

            gameObject.AddComponent<BlacklistUI>();

            Log.LogInfo($"{NAME} {VERSION} loaded. Menu: {MenuToggleKey.Value}. {Store.Total} blacklisted in {StoreFileName}. Working: {BlacklistFilter.HookSummary}.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
