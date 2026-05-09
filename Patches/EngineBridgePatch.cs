using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using XUnity.AutoTranslator.Plugin.Core;
using System;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class EngineBridgePatch
{
    #region 1. Internal State
    private static System.Func<object, object> _managerGetter = null!;
    private static System.Func<object, object> _endpointGetter = null!;
    private static System.Func<object, object> _idGetter = null!;
    #endregion

    #region 1. Engine Integrity (Asset Protection)

    [HarmonyPatch(typeof(AssetBundle), nameof(AssetBundle.LoadAsset), typeof(string), typeof(Il2CppSystem.Type))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixLoadAsset(AssetBundle __instance, UnityEngine.Object __result, string name, Il2CppSystem.Type type)
    {
        if (__result == null || !__result.IsSafe()) return;

        bool isFont = type.Equals(Il2CppType.Of<Font>()) ||
                     name.Contains("font", StringComparison.OrdinalIgnoreCase);

        if (isFont)
        {
            __result.hideFlags |= HideFlags.DontUnloadUnusedAsset;

            if (FLog.IsDeveloperContext)
                FLog.Debug($"[Bridge] Font Asset Protected: {name}");
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
            // 1. Build Accessors using CreateAccessor (compiled delegates for speed)
            if (_managerGetter == null)
            {
                var managerProp = instance.GetType().GetProperty("TranslationManager", Util.UniversalFlags);
                if (managerProp != null) _managerGetter = Util.CreateAccessor<object>(managerProp);
            }

            var manager = _managerGetter?.Invoke(instance);
            if (manager == null) return;

            if (_endpointGetter == null)
            {
                var endpointProp = manager.GetType().GetProperty("CurrentEndpoint", Util.UniversalFlags);
                if (endpointProp != null) _endpointGetter = Util.CreateAccessor<object>(endpointProp);
            }

            var endpoint = _endpointGetter?.Invoke(manager);
            if (endpoint == null) return;

            // 2. Read endpoint identifier
            if (_idGetter == null)
            {
                var idProp = endpoint.GetType().GetProperty("Endpoint", Util.UniversalFlags);
                if (idProp != null) _idGetter = Util.CreateAccessor<object>(idProp);
            }

            var endpointId = _idGetter?.Invoke(endpoint);
            FLog.Info($"[XUAT] Active Engine: {endpointId ?? "Unknown"}");

            // 3. Detect Sugoi Offline — log optimizations
            bool isSugoi = endpointId?.ToString()?.Contains("Sugoi", StringComparison.OrdinalIgnoreCase) == true;
            if (isSugoi)
                FLog.Info("[XUAT] Sugoi offline engine detected — subtitle coroutine cleanup enabled.");
        }
        catch (Exception ex)
        {
            FLog.Debug($"[Bridge] Telemetry link bypassed: {ex.Message}");
        }
    }
    #endregion

    // =========================================================================
    // REGION 3: Universal Translation Preprocessor (TranslationPreprocessorPatch)
    // =========================================================================

    #region 3. Universal Preprocessor
    /// <summary>
    /// Prefix on the internal XUAT filter-mode setter:
    /// prevents XUAT from switching to a restrictive "IsEnglish" ASCII-only filter
    /// that silently drops translations for Thai, Vietnamese, and other non-ASCII scripts.
    /// Equivalent to legacy <c>TranslationPreprocessorPatch</c>.
    /// This is the critical fix that makes the mod work for ALL languages globally.
    /// </summary>
    [HarmonyPatch(typeof(AutoTranslationPlugin), "Initialize")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixUniversalInit(AutoTranslationPlugin __instance)
    {
        // Remove the ASCII-only (IsEnglish) filter that the legacy mod was bypassing.
        // XUAT's internal 'allowStartTranslationImmediate' and 'ignoreComponentState' flags
        // are controlled via reflection since the API is internal.
        try
        {
            var type = __instance.GetType();

            // Allow all Unicode scripts by removing English-only content gating.
            // The field name may vary across XUAT versions; probe common names.
            foreach (string fieldName in new[] { "_filter", "filterMode", "_filterMode", "ContentFilter" })
            {
                var field = type.GetField(fieldName, Util.UniversalFlags)
                         ?? type.GetProperty(fieldName, Util.UniversalFlags) as System.Reflection.MemberInfo;
                if (field == null) continue;

                // Set to 0 (None/Passthrough) — removes the ASCII gate
                if (field is System.Reflection.FieldInfo fi && fi.FieldType == typeof(int))
                {
                    fi.SetValue(__instance, 0);
                    FLog.Debug($"[Bridge] XUAT filter '{fieldName}' set to passthrough for universal Unicode support.");
                    break;
                }
                if (field is System.Reflection.PropertyInfo pi && pi.PropertyType == typeof(int) && pi.CanWrite)
                {
                    pi.SetValue(__instance, 0);
                    FLog.Debug($"[Bridge] XUAT filterMode '{fieldName}' set to passthrough for universal Unicode support.");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            FLog.Debug($"[Bridge] Universal preprocessor bypass failed (non-critical): {ex.Message}");
        }
    }
    #endregion
}