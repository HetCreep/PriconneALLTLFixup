using HarmonyLib;
using System;
using System.Text.RegularExpressions;
using Elements;
using PriconneALLTLFixup;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch(typeof(StoryManager), nameof(StoryManager.execCommand))]
[HarmonyWrapSafe]
public static class StoryManagerPatch
{
    #region 1. Story Command Constants
    private const int COMMAND_PRINT = 6;
    private const int COMMAND_TOUCH_WAIT = 10;
    private const int COMMAND_WAIT = 11;
    private const int COMMAND_CHOICE = 104;
    #endregion

    #region 2. Internal State
    private static bool _isAutoFeedPending;
    private static readonly Regex _colorTagStripper = new(@"\[([0-9A-F]{6,8})\]", RegexOptions.Compiled);
    #endregion

    #region 3. Harmony Hooks
    [HarmonyPrefix]
    public static void OnExecCommandPrefix(StoryManager __instance, int _index)
    {
        if (Settings.Visual.StoryEnhancement == null || !Settings.Visual.StoryEnhancement.Value) return;
        if (!__instance.IsSafe() || __instance.storyCommandList == null) return;

        var commands = __instance.storyCommandList.ToArray();
        if (_index < 0 || _index >= commands.Length) return;

        if ((int)commands[_index].Number == COMMAND_PRINT)
        {
            _isAutoFeedPending = true;
            Log.Debug($"[StoryManager] Print Command Detected at index: {_index}");
        }
    }

    [HarmonyPostfix]
    public static void OnExecCommandPostfix(StoryManager __instance, int _index)
    {
        if (Settings.Visual.StoryEnhancement == null || !Settings.Visual.StoryEnhancement.Value) return;
        if (!__instance.IsSafe() || __instance.storyCommandList == null) return;

        var commands = __instance.storyCommandList.ToArray();
        if (_index < 0 || _index >= commands.Length) return;

        var currentCommand = commands[_index];

        if ((int)currentCommand.Number != COMMAND_PRINT || !_isAutoFeedPending) return;

        string rawArgs = (currentCommand.Args != null && currentCommand.Args.Count > 1)
                         ? currentCommand.Args[1]
                         : "";

        if (string.IsNullOrEmpty(_colorTagStripper.Replace(rawArgs, "").Trim())) return;

        if (!IsNextCommandPrint(__instance)) return;

        _isAutoFeedPending = false;
        __instance.FeedPage(true, true);
        __instance.SetTouchEnabled(true);
        __instance.touchDelegateList.Clear();

        Log.Debug($"[StoryManager] Auto-Feed Triggered after Print Command at index: {_index}");
    }
    #endregion

    #region 4. Professional Pipeline Logic
    public static bool IsNextCommandPrint(StoryManager instance)
    {
        if (!instance.IsSafe() || instance.storyCommandList == null) return false;
        var commands = instance.storyCommandList.ToArray();
        return LookaheadForPrintCommand(commands, instance.currentCommandIndex);
    }

    public static bool IsNextCommandPrint(TutorialStoryManager instance)
    {
        if (!instance.IsSafe() || instance.storyCommandList == null) return false;
        var commands = instance.storyCommandList.ToArray();
        return LookaheadForPrintCommand(commands, instance.currentCommandIndex);
    }

    private static bool LookaheadForPrintCommand(CommandStruct[] commands, int startIndex)
    {
        if (startIndex >= commands.Length) return false;

        for (int i = startIndex + 1; i < commands.Length; i++)
        {
            int cmdNum = (int)commands[i].Number;

            if (cmdNum == COMMAND_WAIT || cmdNum == COMMAND_TOUCH_WAIT || cmdNum == COMMAND_CHOICE)
            {
                return false;
            }

            if (cmdNum == COMMAND_PRINT)
            {
                return true;
            }
        }

        return false;
    }
    #endregion
}