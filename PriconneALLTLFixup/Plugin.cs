using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Linq;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup;

[BepInPlugin(MyPluginInfo.Guid, MyPluginInfo.Name, MyPluginInfo.Version)]
[BepInProcess(MyPluginInfo.ProcessName)]
public class Plugin : BasePlugin
{
    #region 1. Infrastructure Fields
    private readonly HarmonyPatchController _patchController = new(MyPluginInfo.HarmonyGuid, MyPluginInfo.Guid);
    #endregion

    #region 2. Global Accessor
    public static Plugin Instance { get; private set; } = null!;
    public static XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin AutoTranslatorPlugin;
    #endregion

    #region 3. Lifecycle Management (Load/Unload)
    public override void Load()
    {
        Instance = this;

        PriconneALLTLFixup.Log.Initialize(base.Log);
        SetupEnvironmentEncoding();

        CoroutineStarter.SetupMainThread();

        PriconneALLTLFixup.Log.Info($"=== {MyPluginInfo.Name} v{MyPluginInfo.Version} Deployment Started ===");

        if (!VerifySystemIntegrity()) return;

        try
        {
            ConfigurationManager.Initialize(Config);

            PriconneALLTLFixup.Log.Debug("Executing global asset pre-registration...");
            Util.PreloadGlobalResources();

            InitiatePatchDeployment();

            //InitializeSpecializedIntegrations();
            ConfigurationManager.SynchronizePatches(_patchController);

            LogExecutionSummary();
        }
        catch (Exception ex)
        {
            PriconneALLTLFixup.Log.Error("Critical failure during plugin initialization sequence.", ex);
        }
    }

    public override bool Unload()
    {
        try
        {
            PriconneALLTLFixup.Log.Info("Initiating safe teardown of translation modules...");
            _patchController.UnpatchAll();
            Instance = null!;
            return true;
        }
        catch (Exception ex)
        {
            PriconneALLTLFixup.Log.Error("Internal error during plugin decommissioning.", ex);
            return false;
        }
    }
    #endregion

    #region 4. Core Operation Methods
    private void InitiatePatchDeployment()
    {
        if (PriconneALLTLFixup.Log.IsDeveloperContext)
        {
            var sw = Stopwatch.StartNew();
            _patchController.ApplyAllSynchronous();
            sw.Stop();
            PriconneALLTLFixup.Log.Debug($"Full sync deployment completed in {sw.ElapsedMilliseconds}ms");
        }
        else
        {
            PriconneALLTLFixup.Log.Info("Background deployment mode active (Batch Size: 5)");
            _patchController.ApplySmartPatching();
        }
    }

    //private void InitializeSpecializedIntegrations()
    //{
    //    var harmony = _patchController.Instance;
    //    XUATPatch.InitializeIntegration(harmony);
    //    TranslationPreprocessorPatch.DoManualPatch(harmony);
    //}

    private bool VerifySystemIntegrity()
    {
        if (!IL2CPPChainloader.Instance.Plugins.ContainsKey("com.github.bbepis.xunity.autotranslator"))
        {
            PriconneALLTLFixup.Log.Fatal("ENVIRONMENT INTEGRITY FAILURE: XUnity.AutoTranslator is missing.");
            PriconneALLTLFixup.Log.Fatal("This core infrastructure component is required for the manager to function.");
            return false;
        }
        return true;
    }

    private void LogExecutionSummary()
    {
        var stats = _patchController.PatchProfiling;
        PriconneALLTLFixup.Log.Info("--------------------------------------------------");
        PriconneALLTLFixup.Log.Info($"{MyPluginInfo.Name}: Status Operational.");
        PriconneALLTLFixup.Log.Info($"- Modules Active: {stats.Count}");
        PriconneALLTLFixup.Log.Info($"- Performance Impact: {stats.Values.Sum()}ms total load time");

        if (PriconneALLTLFixup.Log.IsDeveloperContext && stats.Any())
        {
            var slowest = stats.OrderByDescending(x => x.Value).First();
            PriconneALLTLFixup.Log.Debug($"- Bottleneck Analysis: {slowest.Key} ({slowest.Value}ms)");
        }
        PriconneALLTLFixup.Log.Info("--------------------------------------------------");
    }
    #endregion

    #region 5. Environmental Helpers
    private void SetupEnvironmentEncoding()
    {
        try
        {
            if (Console.OutputEncoding.CodePage != 65001)
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                PriconneALLTLFixup.Log.Debug("Console output stream synchronized to UTF-8.");
            }
        }
        catch (Exception ex)
        {
            PriconneALLTLFixup.Log.Warn("Unable to force UTF-8 console encoding. Symbols may appear corrupted.");
            PriconneALLTLFixup.Log.Debug(ex);
        }
    }
    #endregion
}