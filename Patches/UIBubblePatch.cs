#nullable enable
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Elements;
using HarmonyLib;
using UnityEngine;


namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class UIBubblePatch
{
    private const int MaxSkillNameLength = 40;

    #region 1. Battle Skill Bubble (LifeGaugeController)
    [HarmonyPatch(typeof(LifeGaugeController), "IndicateSkillName")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixSkillBubble(LifeGaugeController __instance, string _skillName)
    {
        if (!ConfigManager.UI.BubbleFixes.Value) return true;
        if (!__instance.IsSafe() || string.IsNullOrEmpty(_skillName))
            return false;

        if (__instance.owner.ComponentSortOrder.IsFront && __instance.battleManager.BlackOutUnitList.Count != 0)
            return false;

        if (!__instance.gameObject.activeSelf)
            __instance.gameObject.SetActive(true);

        __instance.skillNameLabel.text = _skillName;
        __instance.skillBalloonVisible = true;
        __instance.skillNameBalloon.gameObject.SetActive(true);

        __instance.skillNameBalloon.width = GetUniversalBubbleWidth(__instance.skillNameLabel);

        __instance.battleManager.AppendCoroutine(
            UpdateSkillBalloonCoroutine(__instance, _skillName).WrapToIl2Cpp(),
            (Elements.ePauseType)2,
            null
        );

        return false;
    }

    private static IEnumerator UpdateSkillBalloonCoroutine(LifeGaugeController controller, string skillName)
    {
        float timer = 1f;
        while (!string.IsNullOrEmpty(skillName))
        {
            if (!controller.IsSafe() || !controller.skillNameLabel.IsSafe())
                yield break;

            string text = controller.skillNameLabel.text;
            if (text.Length > MaxSkillNameLength + 3)
            {
                text = text.Substring(0, MaxSkillNameLength) + "...";
            }

            controller.skillNameLabel.text = text;

            controller.skillNameBalloon.width = GetUniversalBubbleWidth(controller.skillNameLabel);

            timer -= controller.battleManager.DeltaTime;

            if (timer < 0f || (controller.owner.ComponentSortOrder.IsFront && controller.battleManager.BlackOutUnitList.Count != 0))
            {
                controller.skillNameBalloon.gameObject.SetActive(false);
                if (!controller.isMoving && !controller.skillBalloonVisible && controller.iconCount == 0)
                {
                    controller.gameObject.SetActive(false);
                }
                yield break;
            }
            yield return null;
        }
    }

    private static int GetUniversalBubbleWidth(CustomUILabel label)
    {
        if (!label.IsSafe()) return 48;

        label.ProcessText();

        return Mathf.CeilToInt(label.printedSize.x) + 48;
    }
    #endregion

    #region 2. Guildhouse Speech Bubble (PartsRoomBalloon)
    [HarmonyPatch(typeof(PartsRoomBalloon), "ShowSpeakingText")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixRoomBubble(PartsRoomBalloon __instance, string setText, RoomCharacter character)
    {
        if (!__instance.IsSafe() || character == null || string.IsNullOrEmpty(setText))
            return;

        character.StartCoroutine(UpdateSpeechBubbleCoroutine(__instance, setText).WrapToIl2Cpp());
    }

    private static IEnumerator UpdateSpeechBubbleCoroutine(PartsRoomBalloon balloon, string originalText)
    {
        string text = originalText;

        while (balloon.IsSafe() && balloon.SpeakingActive)
        {
            if (balloon.speakingText.text != text)
            {
                text = balloon.speakingText.text;

                balloon.speakingText.ProcessText();

                balloon.speakingBalloon.SetDimensions(
                    balloon.speakingBalloon.width,
                    Mathf.CeilToInt(balloon.speakingText.printedSize.y) + 30
                );
            }
            yield return null;
        }
    }
    #endregion

    #region 3. SpineDrama Text Balloon (SpineDramaController)
    /// <summary>
    /// Prefix (replaces) on <c>SpineDramaController.commandTextPlay</c>:
    /// widens the speech balloon sprite to 500 px and enables <c>ResizeHeight</c> overflow
    /// so that translated text wrapping across multiple lines does not get clipped.
    /// After setting the text, launches <see cref="UpdateSpineBubble"/> to dynamically
    /// resize the balloon frame to match the final printed dimensions.
    /// Returns <see langword="false"/> to skip the original method.
    /// </summary>
    [HarmonyPatch(typeof(SpineDramaController), "commandTextPlay")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixSpineBalloon(SpineDramaController __instance)
    {
        if (!__instance.IsSafe()) return true; // allow original if instance bad

        if (__instance.isSkip) return false;

        int balloonId = __instance.intParse(__instance.nextParam());

        if (!__instance.textBalloonDic.ContainsKey(balloonId))
        {
            FLog.Warn($"[Spine] textBalloonDic does not contain key {balloonId}.");
            return false;
        }

        var balloon = __instance.textBalloonDic[balloonId];
        if (!balloon.IsSafe()) return false;

        // Widen balloon and enable height-resize overflow for translated text
        balloon!.balloonSprite.width      = 500;
        balloon.textLabel.width           = balloon.balloonSprite.width - 40;
        balloon.textLabel.overflowMethod  = UILabel.Overflow.ResizeHeight;
        balloon.textLabel.spacingY        = 6;

        string text         = __instance.nextParam();
        int    charsPerSec  = __instance.intParse(__instance.nextParam());
        bool   flag1        = __instance.boolParse(__instance.nextParam());
        bool   flag2        = __instance.boolParse(__instance.nextParam());
        float  preDelay     = __instance.floatParse(__instance.nextParam());
        float  postDelay    = __instance.floatParse(__instance.nextParam());
        float  baseDelay    = __instance.floatParse(__instance.nextParam());

        // Compute total display time: base + length / speed (with fallback)
        float totalDelay = baseDelay;
        if (text.Length > 0 && (!flag1 || !flag2))
        {
            if (charsPerSec > 0)
                totalDelay += (float)text.Length / charsPerSec;
            else
            {
                totalDelay += 5f;
                FLog.Warn("[Spine] charsPerSecond was 0, defaulting to +5 s display time.");
            }
        }

        balloon.textLabel.SetText(text, (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>?)null);

        // If XUAT has already replaced the text, use the translated version for timing
        if (balloon.textLabel.text != text)
            text = balloon.textLabel.text;

        __instance.coroutineDic[__instance.CurrentCommandId] =
            __instance.coroutinePlayer.StartCoroutine(
                balloon.PlayCoroutine(text, charsPerSec, flag1, flag2, preDelay, postDelay, totalDelay));

        __instance.coroutinePlayer.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(
                UpdateSpineBubble(balloon)));

        return false;
    }

    /// <summary>
    /// Coroutine: polls the balloon's text label each frame and resizes the sprite frame
    /// to match the printed dimensions (with <see cref="SpineDramaTextBalloon.MARGIN"/> padding),
    /// clamped to <see cref="SpineDramaTextBalloon.MIN_SIZE"/>.
    /// Finishes the typewriter effect once the text has stabilised.
    /// </summary>
    private static IEnumerator UpdateSpineBubble(SpineDramaTextBalloon balloon)
    {
        if (!balloon.IsSafe()) yield break;

        string lastText = string.Empty;

        while (balloon.IsSafe() && balloon!.isActiveAndEnabled)
        {
            if (!balloon.textLabel.IsSafe()) yield break;

            string current = balloon.textLabel.text?.Trim() ?? string.Empty;

            // Wait until the first non-empty text appears
            if (lastText == string.Empty && current == string.Empty)
            {
                yield return null;
                continue;
            }

            if (current != lastText)
            {
                lastText = current;
                balloon.textLabel.ProcessText();

                var size = balloon.textLabel.printedSize;
                size.x += balloon.MARGIN * 2;
                size.y += balloon.MARGIN * 2;
                size.x  = Mathf.Max(size.x, balloon.MIN_SIZE.x);
                size.y  = Mathf.Max(size.y, balloon.MIN_SIZE.y);

                balloon.balloonSprite.SetDimensions((int)size.x, (int)size.y);

                // Anchor text label inside the balloon based on pivot direction
                balloon.textLabel.transform.localPosition =
                    balloon.balloonSprite.pivot == UIWidget.Pivot.Left
                        ? new Vector3(balloon.MARGIN, size.y * 0.5f - balloon.MARGIN)
                        : new Vector3(-size.x * 0.5f + balloon.MARGIN, size.y - balloon.MARGIN);

                if (balloon.typewriterEffect.IsSafe() && balloon.typewriterEffect!.isActive)
                    balloon.typewriterEffect.Finish();
            }

            yield return null;
        }
    }
    #endregion
}