#nullable enable
using BepInEx.Unity.IL2CPP.Utils.Collections;
using CodeStage.AntiCheat.ObscuredTypes;
using Elements;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSystem;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Unified Story Engine patch module covering command execution, dialog typewriting,
/// choice rendering, and auto-advance timing for both Story and Tutorial story managers.
/// <para>Module A: Command Execution — auto-feeds the page when translated text fills the dialog box.</para>
/// <para>Module B: Dialog Typewriting — accumulates dialog text across consecutive PRINT commands.</para>
/// <para>Module C: Choice Rendering — strips embedded color codes from choice button labels.</para>
/// <para>Module D: Auto-Advance Gate — enforces a timing constraint before accepting auto-click events.</para>
/// </summary>
[HarmonyPatch]
public static class StorySystemPatch
{
    #region 1. Shared State
    // Compiled regex: matches [RRGGBB] or [RRGGBBAA] color tags embedded in story text.
    private static readonly Regex _colorTagRegex =
        new(@"\[([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})\]", RegexOptions.Compiled);

    private static bool   _shouldFeedPage;
    private static string _currentName = string.Empty;
    private static string _currentText = string.Empty;

    // Timestamp (ms since epoch) after which the typewriter is considered complete.
    private static long _typewriterFinishTimeMs;
    #endregion

    // =========================================================================
    // MODULE A: Command Execution (StoryManager::execCommand)
    // =========================================================================

    #region 2. Module A — Hooks
    [HarmonyPatch(typeof(StoryManager), "execCommand")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixExecCommand(StoryManager __instance, int _index)
    {
        if (!ConfigManager.Visual.StoryFixes.Value) return;
        if (!__instance.IsSafe() || _index >= __instance.storyCommandList.Count) return;
        if (__instance.storyCommandList.ToArray()[_index].Number == CommandNumber.PRINT)
            _shouldFeedPage = true;
    }

    [HarmonyPatch(typeof(StoryManager), "execCommand")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixExecCommand(StoryManager __instance, int _index)
    {
        if (!__instance.IsSafe()) return;
        int count = __instance.storyCommandList.Count;
        if (_index >= count) return;

        var commands = __instance.storyCommandList.ToArray();
        var cmd      = commands[_index];

        if (cmd.Number != CommandNumber.PRINT) return;

        // Only feed page when the PRINT command carries actual text content
        string rawText = cmd.Args.ToArray()[1];
        if (_colorTagRegex.Replace(rawText, string.Empty).Length == 0) return;
        if (!_shouldFeedPage) return;
        if (!IsPrintNext(commands, _index)) return;

        _shouldFeedPage = false;
        __instance.FeedPage(true, true);
        __instance.SetTouchEnabled(true);
        __instance.touchDelegateList.Clear();
    }
    #endregion

    // =========================================================================
    // MODULE B: Dialog Typewriting (StoryCommandPrint::setPrintText)
    // =========================================================================

    #region 3. Module B — Hook
    [HarmonyPatch(typeof(StoryCommandPrint), "setPrintText")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixSetPrintText(StoryCommandPrint __instance, EventDelegate.Callback _typewriteFinishAction)
    {
        long nowMs = NowMs();
        _typewriterFinishTimeMs = (long)System.Math.Round(
            (_currentText.Length + __instance.newTextStr.Length)
            / (__instance.typewriteSpeed / 1.75f) * 1000f) + nowMs;

        ApplyPrintText(__instance, _typewriteFinishAction);
        return false; // suppress original
    }
    #endregion

    #region 4. Module B — Logic
    private static void ApplyPrintText(StoryCommandPrint instance, EventDelegate.Callback finishAction)
    {
        // Acquire or create the TypewriterEffect component
        var typewriter = instance.textLabel.gameObject.GetComponent<TypewriterEffect>();
        int resumeOffset = 0;

        if (typewriter == null)
        {
            typewriter = instance.textLabel.gameObject.AddComponent<TypewriterEffect>();
            typewriter.charsPerSecond = instance.typewriteSpeed;
        }
        else
        {
            EventDelegate.Remove(typewriter.onFinished, finishAction);
            typewriter.Finish();
        }

        // Accumulate text when the speaker has not changed mid-dialog
        if (!instance.isBetweenCommand && _currentName == instance.newNameStr)
        {
            resumeOffset        = instance.textLabel.text.Length;
            instance.newTextStr = _currentText + instance.newTextStr;
        }

        _currentName = instance.newNameStr;
        _currentText = instance.newTextStr;

        instance.NameText = instance.newNameStr;
        // SetText(eTextId, params object[]) — use the string overload via cast
        instance.nameLabel.text = instance.newNameStr;

        // Route through StoryManager or TutorialStoryManager
        bool isPrintFollowing = false;
        if (instance.storyManager?.storyCommandList != null)
        {
            isPrintFollowing = IsPrintNext(
                instance.storyManager.storyCommandList.ToArray(),
                instance.storyManager.currentCommandIndex);
        }
        else if (instance.tutorialStoryManager?.storyCommandList != null)
        {
            isPrintFollowing = IsPrintNext(
                instance.tutorialStoryManager.storyCommandList.ToArray(),
                instance.tutorialStoryManager.currentCommandIndex);
        }
        else
        {
            FLog.Error("[Story] Both StoryManager and TutorialStoryManager are null in setPrintText.");
        }

        if (!isPrintFollowing)
        {
            instance.textLabel.text = _currentText;
            instance.Text           = instance.newTextStr;
            _currentText            = string.Empty;
        }

        typewriter.ResetToOffset(resumeOffset);
        finishAction.Invoke();
        typewriter.Finish();
    }

    /// <summary>
    /// Returns <c>true</c> when the next significant command after <paramref name="index"/>
    /// is another PRINT (meaning the dialog box should keep accumulating text).
    /// Returns <c>false</c> if a CHOICE, TOUCH, or TOUCH_TO_START is encountered first.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsPrintNext(CommandStruct[] commands, int index)
    {
        for (int i = index + 1; i < commands.Length; i++)
        {
            var num = commands[i].Number;
            if (num == CommandNumber.CHOICE
             || num == CommandNumber.TOUCH
             || num == CommandNumber.TOUCH_TO_START) return false;
            if (num == CommandNumber.PRINT)           return true;
        }
        return false;
    }
    #endregion

    // =========================================================================
    // MODULE C: Choice Rendering (StoryChoiceController::OpenChoiceButton)
    // =========================================================================

    #region 5. Module C — Hook
    [HarmonyPatch(typeof(StoryChoiceController), "OpenChoiceButton")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixOpenChoiceButton(ref string _labelText)
    {
        // Strip color-code tags so they do not appear as raw text on choice buttons
        _labelText = _colorTagRegex.Replace(_labelText, string.Empty);
    }
    #endregion

    // =========================================================================
    // MODULE D: Auto-Advance Timing Gate (StoryScene::onClickNextCommand)
    // =========================================================================

    #region 6. Module D — Hook
    [HarmonyPatch(typeof(StoryScene), "onClickNextCommand")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixAutoAdvance(StoryScene __instance, bool _autoClick)
    {
        if (!__instance.storyManager.NoVoice)
        {
            // With voice: allow advance only when typewriter is done (or manual click)
            return !_autoClick || NowMs() >= _typewriterFinishTimeMs;
        }

        if (__instance.IsAutoPlay)
        {
            // No-voice auto-play: block if lip-sync is still running
            if (__instance.lipSyncCoroutineList != null
             && __instance.isPlayLipsync
             && __instance.lipSyncCoroutineList.Count > __instance.lipSyncIndex) return false;

            // No-voice auto-play: block until typewriter timer elapses
            if (NowMs() < _typewriterFinishTimeMs) return false;
        }

        return true;
    }
    #endregion

    #region 7. Internal Helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long NowMs() => System.DateTime.Now.Ticks / 10_000L;
    #endregion

    // =========================================================================
    // MODULE E: Tutorial Story Page-Feed (TutorialStoryManager::execCommand)
    // =========================================================================

    #region 8. Module E — Hooks
    private static bool _tutorialShouldFeedPage;

    /// <summary>
    /// Mirrors Module A for <see cref="TutorialStoryManager"/>:
    /// sets the feed-page flag when a PRINT command is about to execute.
    /// </summary>
    [HarmonyPatch(typeof(TutorialStoryManager), "execCommand")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixTutorialExecCommand(TutorialStoryManager __instance, int _commndIndex)
    {
        if (!__instance.IsSafe() || _commndIndex >= __instance.storyCommandList.Count) return;
        if (__instance.storyCommandList.ToArray()[_commndIndex].Number == CommandNumber.PRINT)
            _tutorialShouldFeedPage = true;
    }

    /// <summary>
    /// Mirrors Module A Postfix for <see cref="TutorialStoryManager"/>:
    /// auto-feeds the page when translated PRINT text is ready and the next command is also a PRINT.
    /// </summary>
    [HarmonyPatch(typeof(TutorialStoryManager), "execCommand")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixTutorialExecCommand(TutorialStoryManager __instance, int _commndIndex)
    {
        if (!__instance.IsSafe()) return;
        int count = __instance.storyCommandList.Count;
        if (_commndIndex >= count) return;

        var commands = __instance.storyCommandList.ToArray();
        var cmd      = commands[_commndIndex];

        if (cmd.Number != CommandNumber.PRINT) return;

        string rawText = cmd.Args.ToArray()[1];
        if (_colorTagRegex.Replace(rawText, string.Empty).Length == 0) return;
        if (!_tutorialShouldFeedPage) return;
        if (!IsPrintNext(commands, _commndIndex)) return;

        _tutorialShouldFeedPage = false;
        __instance.FeedPage(true, true);
        __instance.SetTouchEnabled(true);
        __instance.touchDelegateList.Clear();
    }
    #endregion

    // =========================================================================
    // MODULE F: Story Pre-translation Pump (StoryManager::StartStory)
    // =========================================================================

    #region 9. Module F — Hook & Logic
    private static CustomUILabel? _storyPretranslationLabel;
    private const float DefaultStoryTranslationWaitSec = 2.5f;
    private const long  StoryWaitThresholdMs           = 5_000L;

    // Regex reused from shared state (_colorTagRegex) — no duplicate needed

    // {0} placeholder used in Japanese story scripts for the player's name
    private static readonly System.Text.RegularExpressions.Regex _playerNameRegex =
        new(@"\{0\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Postfix on <c>StoryManager.StartStory</c>: ensures the off-screen pump label exists
    /// and launches the pre-translation coroutine that walks PRINT/CHOICE/TITLE commands
    /// through XUAT before the player reaches them.
    /// </summary>
    [HarmonyPatch(typeof(StoryManager), "StartStory")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixStartStory(StoryManager __instance)
    {
        if (!__instance.IsSafe()) return;
        EnsureStoryPretranslationLabel();
        CoroutineStarter.Instance.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(
                StoryPretranslationCoroutine(__instance)));
    }

    private static void EnsureStoryPretranslationLabel()
    {
        if (_storyPretranslationLabel.IsSafe()) return;
        var go = new UnityEngine.GameObject("StoryPretranslationLabel");
        go.transform.localPosition = new UnityEngine.Vector3(0f, -1000f, 0f);
        _storyPretranslationLabel  = go.AddComponent<CustomUILabel>();
        _storyPretranslationLabel.Persistent();
        FLog.Debug("[Story] Pre-translation pump label created.");
    }

    private static System.Collections.IEnumerator StoryPretranslationCoroutine(StoryManager manager)
    {
        if (manager.storyCommandList == null)
        {
            FLog.Error("[Story] Story command list is null — pre-translation aborted.");
            yield break;
        }

        var commands = manager.storyCommandList.ToArray();
        int  index      = 0;
        string lastText = string.Empty;
        long lastUpdate = NowMs();

        float xd      = Util.GetXuatDelay();
        float waitSec = xd > 0f ? xd : DefaultStoryTranslationWaitSec;

        yield return null; // one frame warm-up

        while (_storyPretranslationLabel.IsSafe() && manager.IsSafe() && !manager.IsEndStory)
        {
            if (index >= commands.Length - 1) break;

            // Pause while a movie is playing inside the story
            if (manager.movieManager.IsSafe() && manager.movieManager.IsPlay())
            {
                yield return null;
                continue;
            }

            // Wait for XUAT to finish translating the previous text (up to threshold)
            if (lastText != string.Empty
             && lastText == _storyPretranslationLabel!.text
             && NowMs() - lastUpdate < StoryWaitThresholdMs)
            {
                yield return null;
                continue;
            }

            // Catch up to the current command index if the player advanced quickly
            if (manager.currentCommandIndex > index && index > 12)
                index = manager.currentCommandIndex + 1;

            string text = GetNextStoryString(commands, index, ref index);
            text        = ReplacePlayerName(text);
            text        = _colorTagRegex.Replace(text, string.Empty);

            lastUpdate                         = NowMs();
            lastText                           = text;
            _storyPretranslationLabel!.text    = text;

            yield return new UnityEngine.WaitForSecondsRealtime(waitSec);
        }

        if (_storyPretranslationLabel.IsSafe())
            _storyPretranslationLabel!.text = string.Empty;
    }

    /// <summary>Replaces <c>{0}</c> placeholders with the current player's username.</summary>
    private static string ReplacePlayerName(string text)
    {
        var userData = Singleton<UserData>.Instance;
        if (!userData.IsSafe() || userData.UserInfo == null) return text;
        string name = userData.UserInfo.UserName;
        return string.IsNullOrEmpty(name)
            ? text
            : _playerNameRegex.Replace(text, name).ReplaceNewLineWithString("\n");
    }

    /// <summary>
    /// Walks <paramref name="commands"/> from <paramref name="startIndex"/> forward to find
    /// the next translatable text segment (PRINT block, CHOICE, TITLE, OUTLINE, or SITUATION).
    /// Updates <paramref name="currentIndex"/> to the command after the returned segment.
    /// </summary>
    private static string GetNextStoryString(CommandStruct[] commands, int startIndex, ref int currentIndex)
    {
        if (startIndex >= commands.Length) return string.Empty;

        int nextStop = GetNextStopIndex(commands, startIndex);
        if (nextStop == -1) { currentIndex = commands.Length - 1; return string.Empty; }

        // Check for high-priority single-line commands before the next TOUCH/CHOICE stop
        for (int i = startIndex; i < nextStop; i++)
        {
            var num = commands[i].Number;
            if (num == CommandNumber.CHOICE
             || num == CommandNumber.TITLE
             || num == CommandNumber.OUTLINE
             || num == CommandNumber.SITUATION)
            {
                currentIndex = i + 1;
                return commands[i].Args.ToArray()[0];
            }
        }

        currentIndex = nextStop + 1;

        if (nextStop == startIndex)
            return GetNextStoryString(commands, nextStop + 1, ref currentIndex);

        // Concatenate all PRINT args in the block
        return GetPrintBlock(commands, startIndex, nextStop);
    }

    /// <summary>Returns the index of the next TOUCH / TOUCH_TO_START / CHOICE / TITLE / OUTLINE / SITUATION command.</summary>
    private static int GetNextStopIndex(CommandStruct[] commands, int index)
    {
        for (int i = index; i < commands.Length; i++)
        {
            var n = commands[i].Number;
            if (n == CommandNumber.TOUCH || n == CommandNumber.TOUCH_TO_START
             || n == CommandNumber.CHOICE || n == CommandNumber.TITLE
             || n == CommandNumber.OUTLINE || n == CommandNumber.SITUATION)
                return i;
        }
        return -1;
    }

    /// <summary>Concatenates the text arguments of all PRINT commands in [startIndex, endIndex).</summary>
    private static string GetPrintBlock(CommandStruct[] commands, int startIndex, int endIndex)
    {
        if (startIndex >= commands.Length) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int i = startIndex; i < commands.Length && i < endIndex; i++)
            if (commands[i].Number == CommandNumber.PRINT)
                sb.Append(commands[i].Args.ToArray()[1]);
        return sb.ToString().Replace("\\n", "\n");
    }
    #endregion

    // =========================================================================
    // MODULE G: Story Color Tag Removal (StoryCommandPrint + StoryLogContainer)
    // =========================================================================

    #region 10. Module G — Hooks
    /// <summary>
    /// Postfix on <c>StoryCommandPrint.SetCommandParam</c>: strips embedded colour-code
    /// tags of the form <c>[RRGGBB]</c> or <c>[RRGGBBAA]</c> from the dialog-line text
    /// so that translated content is never rendered with incorrect Japanese palette values.
    /// The original method body could not be decompiled; this reconstruction applies
    /// the strip directly to the label's text field after all parameters have been applied.
    /// </summary>
    [HarmonyPatch(typeof(StoryCommandPrint), "SetCommandParam")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixStoryColorRemoval(StoryCommandPrint __instance)
    {
        if (!__instance.IsSafe()) return;

        // StoryCommandPrint is not a MonoBehaviour; probe its fields for a CustomUILabel via reflection
        CustomUILabel? label = null;
        foreach (var field in __instance.GetType().GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public   |
            System.Reflection.BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(CustomUILabel) || field.FieldType.IsSubclassOf(typeof(CustomUILabel)))
            {
                label = field.GetValue(__instance) as CustomUILabel;
                if (label.IsSafe()) break;
            }
        }
        if (!label.IsSafe()) return;

        string raw = label!.text;
        if (string.IsNullOrEmpty(raw)) return;

        string stripped = _colorTagRegex.Replace(raw, string.Empty);
        if (stripped != raw)
        {
            label.text = stripped;
            FLog.Debug("[Story] Color tags stripped from dialog label.");
        }
    }

    /// <summary>
    /// Prefix on <c>StoryLogContainer.SetupLogData</c>: strips colour-code tags from the
    /// <see cref="StoryLogInfo.Text"/> field before the log entry is rendered.
    /// Must run before the log row template copies the text to its label.
    /// </summary>
    [HarmonyPatch(typeof(StoryLogContainer), "SetupLogData")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixStoryLogColor(ref StoryLogInfo _logInfo)
    {
        _logInfo.Text = _colorTagRegex.Replace(_logInfo.Text ?? string.Empty, string.Empty);
    }
    #endregion

    // =========================================================================
    // MODULE H: Story Place Name Layout (StoryScene::ViewPlaceName)
    // =========================================================================

    #region 11. Module H — Hook
    /// <summary>
    /// Prefix (replaces) on <c>StoryScene.ViewPlaceName</c>:
    /// suppresses the default two-label (header + sub) layout and instead renders only the
    /// sub-label, centred at x=40, with the underline sprite width fitted to the text.
    /// This prevents the Japanese header label from appearing alongside translated place names.
    /// Returns <see langword="false"/> to skip the original method entirely.
    /// </summary>
    [HarmonyPatch(typeof(StoryScene), "ViewPlaceName")]
    [HarmonyPrefix]
    [HarmonyPriority(100)]
    [HarmonyWrapSafe]
    public static bool PrefixViewPlaceName(StoryScene __instance, string _placeName)
    {
        if (!__instance.IsSafe()) return true; // allow original on failure

        var icon = __instance.placeIcon;
        if (!icon.IsSafe()) return true;

        icon!.placeObjAnimation.enabled = false;
        icon.placeHeaderLabel.gameObject.SetActive(false);
        icon.placeSubLabel.SetText(_placeName, (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>?)null);

        var lp = icon.placeSubLabel.transform.localPosition;
        lp.x = 40f;
        icon.placeSubLabel.transform.localPosition = lp;

        icon.lineSprite.width = 83 + icon.placeSubLabel.width;
        icon.StartAnimation();

        return false; // skip original
    }
    #endregion

    // =========================================================================
    // MODULE I: Tutorial Pre-translation Pump (TutorialStoryManager::StartStory)
    // =========================================================================

    #region 12. Module I — Hook & Logic
    private static int _tutorialTranslationIndex;

    /// <summary>
    /// Postfix on <c>TutorialStoryManager.StartStory</c>: ensures the off-screen pump label
    /// exists (shared with Module F) and launches the tutorial-specific pre-translation
    /// coroutine so that XUAT can cache PRINT/CHOICE text before the player reaches it.
    /// </summary>
    [HarmonyPatch(typeof(TutorialStoryManager), "StartStory")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixTutorialStartStory(TutorialStoryManager __instance)
    {
        if (!__instance.IsSafe()) return;

        // Reuse the pump label created by Module F
        EnsureStoryPretranslationLabel();
        _tutorialTranslationIndex = 0;

        CoroutineStarter.Instance.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(
                TutorialPretranslationCoroutine(__instance)));
    }

    private static System.Collections.IEnumerator TutorialPretranslationCoroutine(TutorialStoryManager manager)
    {
        if (manager.storyCommandList == null)
        {
            FLog.Error("[Tutorial] Story command list is null — pre-translation aborted.");
            yield return null;
            yield break;
        }

        var commands = manager.storyCommandList.ToArray();
        string lastText  = string.Empty;
        long   lastUpdate = NowMs();

        float xd      = Util.GetXuatDelay();
        float waitSec = xd > 0f ? xd : DefaultStoryTranslationWaitSec;

        yield return null; // one-frame warm-up

        while (_storyPretranslationLabel.IsSafe()
            && manager.IsSafe()
            && !manager.IsEndStory)
        {
            if (_tutorialTranslationIndex >= commands.Length - 1) break;

            // Throttle: wait while the current text is still being translated
            if (lastText != string.Empty
             && lastText == _storyPretranslationLabel!.text
             && NowMs() - lastUpdate < StoryWaitThresholdMs)
            {
                yield return null;
                continue;
            }

            // Catch up if player advanced past the pump index
            if (manager.currentCommandIndex > _tutorialTranslationIndex
             && _tutorialTranslationIndex > 10)
                _tutorialTranslationIndex = manager.currentCommandIndex + 1;

            string text = GetNextStoryString(commands, _tutorialTranslationIndex, ref _tutorialTranslationIndex);
            text = ReplacePlayerName(text);
            text = _colorTagRegex.Replace(text, string.Empty);

            lastUpdate  = NowMs();
            lastText    = text;
            _storyPretranslationLabel!.text = text;

            yield return new UnityEngine.WaitForSecondsRealtime(waitSec);
        }

        if (_storyPretranslationLabel.IsSafe())
            _storyPretranslationLabel!.text = string.Empty;
    }
    #endregion
}
