using HarmonyLib;
using System.Reflection;

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
        if (ui is not UnityEngine.Component component) return false;

        if (!Util.IsSafe(component)) return false;

        return true;
    }
}