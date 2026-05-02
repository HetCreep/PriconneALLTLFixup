using HarmonyLib;
using System;
using Elements;
using PriconneALLTLFixup;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch(typeof(StoryScene), nameof(StoryScene.onClickNextCommand))]
[HarmonyWrapSafe]
public class StoryAutoDelayPatch
{
    #region 1. Harmony Hook (Entry Point)
    [HarmonyPrefix]
    public static bool OnOnClickNextCommandPrefix(StoryScene __instance, bool _autoClick)
    {
        if (Settings.Visual.StoryEnhancement == null || !Settings.Visual.StoryEnhancement.Value) return true;

        if (!__instance.IsSafe() || !__instance.storyManager.IsSafe()) return true;

        try
        {
            return EvaluateClickPermission(__instance, _autoClick);
        }
        catch (Exception ex)
        {
            Log.Debug($"[StoryDelay] Logic Error: {ex.Message}");
            return true;
        }
    }
    #endregion

    #region 2. Professional Delay Logic
    private static bool EvaluateClickPermission(StoryScene instance, bool isAutoClick)
    {
        long currentTimeMs = DateTime.Now.Ticks / 10000L;

        if (!instance.storyManager.NoVoice)
        {
            if (isAutoClick && StoryDialogPatch.GlobalTypewriterFinishTime > currentTimeMs)
            {
                return false;
            }
            return true;
        }

        if (instance.IsAutoPlay)
        {
            if (instance.isPlayLipsync && instance.lipSyncCoroutineList != null)
            {
                if (instance.lipSyncCoroutineList.Count > instance.lipSyncIndex)
                {
                    return false;
                }
            }

            if (StoryDialogPatch.GlobalTypewriterFinishTime > currentTimeMs)
            {
                Log.Debug("[StoryDelay] Still typing... blocking auto-advance.");
                return false;
            }
        }

        return true;
    }
    #endregion
}