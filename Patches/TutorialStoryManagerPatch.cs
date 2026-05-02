using HarmonyLib;
using System;
using System.Text.RegularExpressions;
using Elements;
using PriconneALLTLFixup;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch(typeof(TutorialStoryManager), nameof(TutorialStoryManager.execCommand))]
[HarmonyWrapSafe]
public static class TutorialStoryManagerPatch
{
    #region 1. Tutorial Command Constants
    private const int COMMAND_PRINT = 6;
    #endregion

    #region 2. Internal State
    private static bool _isTutorialAutoFeedPending;
    private static readonly Regex _colorTagStripper = new(@"\[([0-9A-F]{6,8})\]", RegexOptions.Compiled);
    #endregion

    #region 3. Harmony Hooks
    [HarmonyPrefix]
    public static void OnTutorialExecCommandPrefix(TutorialStoryManager __instance, int _commndIndex)
    {
        if (Settings.Visual.StoryEnhancement == null || !Settings.Visual.StoryEnhancement.Value) return;
        if (!__instance.IsSafe() || __instance.storyCommandList == null) return;

        var commands = __instance.storyCommandList.ToArray();
        if (_commndIndex < 0 || _commndIndex >= commands.Length) return;

        if ((int)commands[_commndIndex].Number == COMMAND_PRINT)
        {
            _isTutorialAutoFeedPending = true;

            Log.Debug($"[TutorialManager] Print detected at index: {_commndIndex}");
        }
    }

    [HarmonyPostfix]
    public static void OnTutorialExecCommandPostfix(TutorialStoryManager __instance, int _commndIndex)
    {
        if (Settings.Visual.StoryEnhancement == null || !Settings.Visual.StoryEnhancement.Value) return;
        if (!__instance.IsSafe() || __instance.storyCommandList == null) return;

        var commands = __instance.storyCommandList.ToArray();
        if (_commndIndex < 0 || _commndIndex >= commands.Length) return;

        var currentCommand = commands[_commndIndex];

        if ((int)currentCommand.Number != COMMAND_PRINT || !_isTutorialAutoFeedPending) return;

        string rawArgs = (currentCommand.Args != null && currentCommand.Args.Count > 1)
                         ? currentCommand.Args[1]
                         : "";

        if (string.IsNullOrEmpty(_colorTagStripper.Replace(rawArgs, "").Trim())) return;

        if (!StoryManagerPatch.IsNextCommandPrint(__instance))
        {
            return;
        }

        _isTutorialAutoFeedPending = false;
        __instance.FeedPage(true, true);
        __instance.SetTouchEnabled(true);
        __instance.touchDelegateList.Clear();

        Log.Debug($"[TutorialManager] Auto-feed executed at index: {_commndIndex}");
    }
    #endregion
}