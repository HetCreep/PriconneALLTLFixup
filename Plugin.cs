#nullable enable
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.IO;
using XUnity.AutoTranslator.Plugin.Core;
using PriconneALLTLFixup.Patches;

namespace PriconneALLTLFixup;

[BepInPlugin(MyPluginInfo.Guid, MyPluginInfo.Name, MyPluginInfo.Version)]
[BepInProcess(MyPluginInfo.ProcessName)]
[BepInDependency("com.github.bbepis.xunity.autotranslator", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("gravydevsupreme.xunity.autotranslator", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BasePlugin
{
    #region 1. Infrastructure Fields
    private readonly HarmonyPatchController _patchController = new(MyPluginInfo.HarmonyGuid, MyPluginInfo.Guid);
    #endregion

    #region 2. Global Accessor
    public static Plugin Instance { get; private set; } = null!;

    public static AutoTranslationPlugin? Xuat { get; internal set; }
    #endregion

    #region 3. Lifecycle Management (Load/Unload)
    public override void Load()
    {
        Instance = this;
        FLog.Initialize(base.Log);
        LogFilter.Install(); // suppress HarmonyX IL2CPP assembly-scan noise before any patches fire

        try
        {
            string configPath = Path.Combine(Paths.ConfigPath, "PriconneALLTLFixup.cfg");
            var metadata = new BepInPlugin(MyPluginInfo.Guid, MyPluginInfo.Name, MyPluginInfo.Version);
            var customConfig = new ConfigFile(configPath, true, metadata);

            ConfigManager.Initialize(customConfig);
            FLog.Info($"Config initialized: {Path.GetFileName(configPath)}");

            VerifySystemIntegrity();
            SetupEnvironmentEncoding();
            CoroutineStarter.SetupMainThread();
            Util.PreloadGlobalResources();

            InitiatePatchDeployment();

            ConfigManager.SynchronizePatches(_patchController);

            LogExecutionSummary();
        }
        catch (Exception ex)
        {
            FLog.Error("Critical load error during plugin initialization.", ex);
        }
    }    

    public override bool Unload()
    {
        try
        {
            FLog.Info("Initiating safe teardown of Master Framework...");

            NumberComponentPatch.ClearCaches();
            TextRegistryPatch.ClearCache();
            UIComponentPatch.ClearCaches();
            AdaptiveTextLayoutProcessor.ClearCaches();

            // ถอดถอน Patch ทั้งหมด
            _patchController.UnpatchAll();

            Instance = null!;
            LogFilter.Uninstall();
            FLog.Info("Teardown complete. Plugin decommissioned successfully.");
            return true;
        }
        catch (Exception ex)
        {
            FLog.Error("Internal error during plugin decommissioning.", ex);
            return false;
        }
    }
    #endregion

    #region 4. Core Operation Methods
    private void InitiatePatchDeployment()
    {
        var critical = new List<Type>
        {
            typeof(Patches.EngineBridgePatch),
            typeof(Patches.TextSafetyPatch)
        };

        var features = new List<Type>();

        _patchController.Activate(critical, features);
    }

    private void LogExecutionSummary()
    {
        var stats = _patchController.PatchProfiling;
        FLog.Info("==================================================");
        FLog.Info($"{MyPluginInfo.Name}: Status Operational.");
        FLog.Info($"- Modules Active: {stats.Count}");
        FLog.Info($"- Impact: {stats.Values.Sum()}ms total load time");
        FLog.Info("==================================================");
    }
    #endregion

    #region 5. Environmental Helpers
    private void SetupEnvironmentEncoding()
    {
        try
        {
            if (Console.OutputEncoding.CodePage != 65001)
                Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch { /* Failsafe */ }
    }

    private bool VerifySystemIntegrity()
    {
        var loader = IL2CPPChainloader.Instance;
        if (loader?.Plugins == null) return false;

        bool foundTranslator = loader.Plugins.ContainsKey("com.github.bbepis.xunity.autotranslator") ||
                               loader.Plugins.ContainsKey("gravydevsupreme.xunity.autotranslator");

        if (ConfigManager.Core.DebugMode.Value)
        {
            FLog.Info("--- [System Scan] Plugin Discovery ---");
            foreach (var plugin in loader.Plugins)
                FLog.Info($"Detected: [{plugin.Key}] | {plugin.Value.Metadata.Name}");
            FLog.Info("--- [System Scan] End ---");
        }

        if (foundTranslator) FLog.Info("[Bridge] XUnity.AutoTranslator link ready.");
        else FLog.Warn("[Bridge] XUnity.AutoTranslator not found. Some features restricted.");

        return true;
    }
    #endregion
}