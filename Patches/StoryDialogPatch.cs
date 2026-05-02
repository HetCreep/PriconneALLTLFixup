using Elements;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using PriconneALLTLFixup;
using System;
using UnityEngine;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch(typeof(StoryCommandPrint), nameof(StoryCommandPrint.setPrintText))]
[HarmonyWrapSafe]
public class StoryDialogPatch
{
    #region 1. Internal Context & State
    private static string _lastActorName = "";
    private static string _dialogBuffer = "";

    public static long GlobalTypewriterFinishTime { get; private set; }
    #endregion

    #region 2. Harmony Hook (Entry Point)
    [HarmonyPrefix]
    public static bool OnSetPrintTextPrefix(StoryCommandPrint __instance, EventDelegate.Callback _typewriteFinishAction)
    {
        if (Settings.Visual.StoryEnhancement == null || !Settings.Visual.StoryEnhancement.Value) return true;

        if (!__instance.IsSafe() || __instance.textLabel == null) return true;

        try
        {
            ProcessStoryDialogue(__instance, _typewriteFinishAction);
            return false;
        }
        catch (Exception ex)
        {
            Log.Debug($"[Story] Dialogue Pipeline Error: {ex.Message}");
            return true;
        }
    }
    #endregion

    #region 3. Dialogue Execution Pipeline
    private static void ProcessStoryDialogue(StoryCommandPrint instance, EventDelegate.Callback onFinish)
    {
        string actorName = instance.newNameStr ?? "";
        string incomingText = instance.newTextStr ?? "";

        incomingText = incomingText.Clean();

        int startIndex = 0;
        if (!instance.isBetweenCommand && _lastActorName == actorName)
        {
            startIndex = instance.textLabel.text.Length;
            incomingText = _dialogBuffer + incomingText;
        }

        incomingText = InjectPlayerData(incomingText);

        _lastActorName = actorName;
        _dialogBuffer = incomingText;

        UpdateUserInterface(instance, actorName, incomingText, startIndex, onFinish);
    }

    private static void UpdateUserInterface(StoryCommandPrint instance, string name, string content, int offset, EventDelegate.Callback onFinish)
    {
        instance.NameText = name;
        instance.nameLabel.SetText(name);

        var labelObj = instance.textLabel.gameObject;
        var typewriter = labelObj.GetComponent<TypewriterEffect>() ?? labelObj.AddComponent<TypewriterEffect>();

        EventDelegate.Remove(typewriter.onFinished, onFinish);
        typewriter.Finish();

        typewriter.charsPerSecond = CalculateDynamicSpeed(instance.typewriteSpeed);

        CalculateFinishTimestamp(content.Length, typewriter.charsPerSecond);

        instance.textLabel.SetText(content);
        instance.Text = content;

        if (!IsNextCommandPrinting(instance))
        {
            _dialogBuffer = "";
        }

        typewriter.ResetToOffset(offset);
        onFinish?.Invoke();
        typewriter.Finish();
    }
    #endregion

    #region 4. Professional Standard Implementation
    private static int CalculateDynamicSpeed(int baseSpeed)
    {
        string lang = Util.GetTargetLanguage();
        return lang switch
        {
            "th" or "vi" => (int)(baseSpeed * 1.2f),
            "ja" or "cn" or "kr" => baseSpeed,
            _ => (int)(baseSpeed * 1.1f)
        };
    }

    private static void CalculateFinishTimestamp(int textLength, int cps)
    {
        long now = DateTime.Now.Ticks / 10000L;
        float durationMs = (textLength / (float)cps) * 1000f;
        GlobalTypewriterFinishTime = (long)Math.Round(durationMs) + now;
    }

    private static string InjectPlayerData(string text)
    {
        try
        {
            return text.Replace("{playername}", Singleton<UserData>.Instance.UserInfo.UserName);
        }
        catch { return text; }
    }

    private static bool IsNextCommandPrinting(StoryCommandPrint instance)
    {
        if (instance.storyManager.IsSafe())
            return StoryManagerPatch.IsNextCommandPrint(instance.storyManager);

        if (instance.tutorialStoryManager.IsSafe())
            return StoryManagerPatch.IsNextCommandPrint(instance.tutorialStoryManager);

        return false;
    }
    #endregion
}