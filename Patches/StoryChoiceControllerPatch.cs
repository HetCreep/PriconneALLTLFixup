using HarmonyLib;
using System;
using System.Text.RegularExpressions;
using Elements;
using PriconneALLTLFixup;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch(typeof(StoryChoiceController), nameof(StoryChoiceController.OpenChoiceButton))]
[HarmonyWrapSafe]
public static class StoryChoicePatch
{
    #region 1. Internal Resources
    private static readonly Regex _colorStripper = new(@"\[([0-9A-F]{6,8})\]", RegexOptions.Compiled);
    #endregion

    #region 2. Harmony Hook (Entry Point)
    [HarmonyPrefix]
    public static void OnOpenChoiceButtonPrefix(ref string _labelText)
    {
        if (Settings.Visual.StoryEnhancement == null || !Settings.Visual.StoryEnhancement.Value) return;

        if (string.IsNullOrEmpty(_labelText)) return;

        try
        {
            _labelText = ProcessChoiceText(_labelText);
        }
        catch (Exception ex)
        {
            Log.Debug($"[StoryChoice] Failed to process choice text: {ex.Message}");
        }
    }
    #endregion

    #region 3. Choice Processing Pipeline
    private static string ProcessChoiceText(string text)
    {
        string cleaned = text.Clean();

        cleaned = _colorStripper.Replace(cleaned, "");

        try
        {
            cleaned = cleaned.Replace("{playername}", Singleton<UserData>.Instance.UserInfo.UserName);
        }
        catch { /*  */ }

        if (text != cleaned)
        {
            Log.Debug($"[StoryChoice] Refined choice: '{text.Substring(0, Math.Min(10, text.Length))}...' -> '{cleaned}'");
        }

        return cleaned;
    }
    #endregion
}