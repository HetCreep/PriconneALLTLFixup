using HarmonyLib;
using System.Reflection;
using UnityEngine;
using System;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class TextSafetyPatch
{
    [HarmonyTargetMethod]
    public static MethodBase TargetMethod()
    {
        return AccessTools.Method("XUnity.AutoTranslator.Plugin.Core.TextTranslationInfo:ResizeUI");
    }

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