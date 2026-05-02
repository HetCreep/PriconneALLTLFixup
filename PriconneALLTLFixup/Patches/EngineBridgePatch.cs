using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class EngineBridgePatch
{
    #region 1. Engine Integrity (Font Protection)
    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadAsset), typeof(string), typeof(Il2CppSystem.Type))]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixLoadAsset(AssetBundle __instance, ref UnityEngine.Object __result, string name, Il2CppSystem.Type type)
    {
        if (__instance == null || string.IsNullOrEmpty(name)) return true;
        if (type.Equals(Il2CppType.Of<Font>()) || name.IndexOf("font", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            try
            {
                var asset = __instance.LoadAsset(name, type);
                if (asset != null)
                {
                    __result = asset.Cast<UnityEngine.Object>();
                    return false;
                }
            }
            catch (Exception ex) { FLog.Debug($"[Bridge] Asset redirection failed: {name} | {ex.Message}"); }
        }
        return true;
    }

    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadAsset), typeof(string), typeof(Il2CppSystem.Type))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixLoadAsset(UnityEngine.Object __result)
    {
        if (__result != null && __result.IsSafe())
        {
            __result.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        }
    }
    #endregion

    #region 2. XUAT Synchronization & Policy
    [HarmonyPatch(typeof(AutoTranslationPlugin), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixXUAT(AutoTranslationPlugin __instance)
    {
        if (__instance == null) return;

        PriconneALLTLFixup.Plugin.Xuat = __instance;

        if (!ConfigManager.Core.TranslatorIntegration.Value) return;

        FLog.Info("[Bridge] XUAT High-performance link established.");

        SyncLanguagePolicy();
        SynchronizeEngineTelemetry(__instance);
    }

    private static void SyncLanguagePolicy()
    {
        string effectiveLang = Util.GetXuatLanguage();
        FLog.Info($"[Bridge] Language synchronized to: {effectiveLang}");

        ConfigManager.Translation.Code.Value = effectiveLang;
    }

    private static void SynchronizeEngineTelemetry(AutoTranslationPlugin instance)
    {
        try
        {
            var manager = instance.GetType().GetProperty("TranslationManager", Util.UniversalFlags)?.GetValue(instance);
            var endpoint = manager?.GetType().GetProperty("CurrentEndpoint", Util.UniversalFlags)?.GetValue(manager);

            if (endpoint != null)
            {
                var endpointId = endpoint.GetType().GetProperty("Endpoint", Util.UniversalFlags)?.GetValue(endpoint);
                var delay = endpoint.GetType().GetProperty("TranslationDelay", Util.UniversalFlags)?.GetValue(endpoint);
                FLog.Info($"[XUAT] Active Endpoint: {endpointId} | Latency: {delay}s");
            }
        }
        catch { /* Failsafe */ }
    }
    #endregion
}