#nullable enable
using BepInEx;
using BepInEx.Unity.IL2CPP;
using System.Diagnostics;
using System.Linq;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup;

[BepInPlugin(MyPluginInfo.Guid, MyPluginInfo.Name, MyPluginInfo.Version)]
[BepInProcess(MyPluginInfo.ProcessName)]
[BepInDependency("com.github.bbepis.xunity.autotranslator", BepInDependency.DependencyFlags.HardDependency)]
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

        try
        {
            ConfigManager.Initialize(Config);
            FLog.Info("Config initialized successfully.");
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"[Fixup] Config Fail: {ex.Message}");
        }

        if (!VerifySystemIntegrity())
        {
            FLog.Warn("System integrity check failed. Plugin will stop, but config was created.");
            return;
        }

        try
        {
            SetupEnvironmentEncoding();
            CoroutineStarter.SetupMainThread();
            Util.PreloadGlobalResources();
            InitiatePatchDeployment();
            ConfigManager.SynchronizePatches(_patchController);
        }
        catch (Exception ex)
        {
            FLog.Error("Load error", ex);
        }
    }

    private bool VerifySystemIntegrity()
    {
        var loader = IL2CPPChainloader.Instance;
        if (loader == null || loader.Plugins == null) return false;

        if (!loader.Plugins.ContainsKey("com.github.bbepis.xunity.autotranslator"))
        {
            FLog.Fatal("XUnity.AutoTranslator missing!");
            return false;
        }
        return true;
    }

    public override bool Unload()
    {
        try
        {
            FLog.Info("Initiating safe teardown of modules...");

            Patches.NumberComponentPatch.ClearCaches();
            Patches.TextRegistryPatch.ClearCache();
            Patches.UIComponentPatch.ClearCaches();
            AdaptiveTextLayoutProcessor.ClearCaches();

            _patchController.UnpatchAll();
            Instance = null!;
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
        if (FLog.IsDeveloperContext)
        {
            var sw = Stopwatch.StartNew();
            _patchController.ApplyAllSynchronous();
            sw.Stop();
            FLog.Debug($"Sync deployment finished in {sw.ElapsedMilliseconds}ms");
        }
        else
        {
            _patchController.ApplySmartPatching();
        }
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
            if (System.Console.OutputEncoding.CodePage != 65001)
                System.Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch { /* Failsafe */ }
    }
    #endregion
}