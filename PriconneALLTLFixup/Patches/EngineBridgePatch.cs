using System;
using HarmonyLib;
using UnityEngine;
using Il2CppInterop.Runtime;
using XUnity.AutoTranslator.Plugin.Core;
using PriconneALLTLFixup;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class EngineBridgePatch
{
    #region 1. Engine Integrity
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
                    __result = asset.Cast<UnityEngine.Object>().Persistent();
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"[Bridge] Asset redirection failed: {name} | {ex.Message}");
            }
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

    #region 2. XUAT Synchronization
    [HarmonyPatch(typeof(AutoTranslationPlugin), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixXUAT(AutoTranslationPlugin __instance)
    {
        if (__instance == null) return;

        Plugin.AutoTranslatorPlugin = __instance;

        if (!ConfigurationManager.Core.TranslatorIntegration.Value) return;

        Log.Info("[XUAT] Translation engine bridge established.");

        SynchronizeEngineTelemetry(__instance);
        EnforceLanguagePolicy(__instance);
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

                Log.Info($"[XUAT] Active Endpoint: {endpointId} | Latency: {delay}s");
            }
            else
            {
                Log.Warn("[XUAT] No active translation endpoint detected.");
            }
        }
        catch (Exception ex) { Log.Debug($"[XUAT] Telemetry acquisition bypassed: {ex.Message}"); }
    }

    private static void EnforceLanguagePolicy(AutoTranslationPlugin instance)
    {
        try
        {
            string targetLang = ConfigurationManager.Translation.Code.Value;
            var settings = instance.GetType().GetProperty("Settings", Util.UniversalFlags)?.GetValue(instance);
            var langProp = settings?.GetType().GetProperty("Language", Util.UniversalFlags);

            if (langProp != null && langProp.CanWrite)
            {
                string currentLang = langProp.GetValue(settings)?.ToString();

                if (!string.Equals(currentLang, targetLang, StringComparison.OrdinalIgnoreCase))
                {
                    langProp.SetValue(settings, targetLang);
                    Log.Info($"[XUAT] Language Policy Enforced: Forced sync to '{targetLang}'");
                }
            }
        }
        catch (Exception ex) { Log.Debug($"[XUAT] Language sync interrupted: {ex.Message}"); }
    }
    #endregion
}