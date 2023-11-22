using BaboonAPI.Hooks.Initializer;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.IO;
using TootTallyCore.Utils.TootTallyModules;
using TootTallySettings;
using UnityEngine;

namespace TootTallySpectator
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency("TootTallyTrombuddies", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("TootTallyWebsocketLibs", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin, ITootTallyModule
    {
        public static Plugin Instance;

        private const string CONFIG_NAME = "TootTally.cfg";
        private Harmony _harmony;
        public ConfigEntry<bool> ModuleConfigEnabled { get; set; }
        public bool IsConfigInitialized { get; set; }

        //Change this name to whatever you want
        public string Name { get => PluginInfo.PLUGIN_NAME; set => Name = value; }

        public static TootTallySettingPage settingPage;

        public static void LogInfo(string msg) => Instance.Logger.LogInfo(msg);
        public static void LogError(string msg) => Instance.Logger.LogError(msg);

        private void Awake()
        {
            if (Instance != null) return;
            Instance = this;
            _harmony = new Harmony(Info.Metadata.GUID);

            GameInitializationEvent.Register(Info, TryInitialize);
        }

        private void TryInitialize()
        {
            // Bind to the TTModules Config for TootTally
            ModuleConfigEnabled = TootTallyCore.Plugin.Instance.Config.Bind("Modules", "Spectator", true, "<insert module description here>");
            TootTallyModuleManager.AddModule(this);
            TootTallySettings.Plugin.Instance.AddModuleToSettingPage(this);
        }

        public void LoadModule()
        {
            string configPath = Path.Combine(Paths.BepInExRootPath, "config/");
            ConfigFile config = new ConfigFile(configPath + CONFIG_NAME, true);
            // Set your config here by binding them to the related ConfigEntry
            // Example:
            // Unlimited = config.Bind(CONFIG_FIELD, "Unlimited", DEFAULT_UNLISETTING)

           
            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("AllowSpectate", new Vector2(400, 50), "Allow Spectate", AllowSpectate, SpectatingManager.OnAllowHostConfigChange);
            TootTallySettings.Plugin.MainTootTallySettingPage.AddToggle("ShowSpectatorCount", new Vector2(400, 50), "Show Spectator Count", ShowSpectatorCount);

            _harmony.PatchAll(typeof(ModuleTemplatePatches));
            LogInfo($"Module loaded!");
        }

        public void UnloadModule()
        {
            _harmony.UnpatchSelf();
            settingPage.Remove();
            LogInfo($"Module unloaded!");
        }

        public static class ModuleTemplatePatches
        {
            // Apply your Trombone Champ patches here
        }

        public ConfigEntry<bool> AllowSpectate { get; private set; }
        public ConfigEntry<bool> ShowSpectatorCount { get; private set; }
    }
}