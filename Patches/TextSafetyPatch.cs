using HarmonyLib;
using System.Reflection;
using UnityEngine;
using System;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// UI stability guard. Intercepts XUAT's internal <c>TextTranslationInfo.ResizeUI</c>
/// and blocks the call whenever the target is not a valid, non-destroyed text
/// component. This prevents access-violation crashes when XUAT chases a stale
/// IL2CPP reference after Unity has already collected the underlying object —
/// the most common cause of "ResizeUI" hard crashes in IL2CPP builds.
/// Controlled by <c>ConfigManager.Core.UIStabilityGuard</c>; strongly recommended
/// to leave enabled in production.
/// </summary>
[HarmonyPatch]
public static class TextSafetyPatch
{
    /// <summary>
    /// Resolves XUAT's internal <c>TextTranslationInfo.ResizeUI</c> via string
    /// lookup because the type is internal and not directly referenceable.
    /// </summary>
    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method("XUnity.AutoTranslator.Plugin.Core.TextTranslationInfo:ResizeUI");
    }

    /// <summary>
    /// Returns <c>false</c> to skip the original <c>ResizeUI</c> when the target
    /// component has been destroyed or isn't a text element. Returns <c>true</c>
    /// to let the original run when the component is safe.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool Prefix(object ui)
    {
        if (!ConfigManager.Core.UIStabilityGuard.Value) return true;

        if (ui is not UnityEngine.Component component) return false;

        if (!component.IsSafe() || !component.IsTextElement())
        {
            if (FLog.IsDeveloperContext)
                FLog.Debug($"[Safety] Blocked invalid ResizeUI attempt on: {ui?.GetType().Name ?? "Null"}");

            return false;
        }

        return true;
    }
}