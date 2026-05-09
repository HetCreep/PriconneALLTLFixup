#nullable enable
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Elements;
using HarmonyLib;
using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Unified Subtitle System patch module for movie cutscenes.
/// <para>Module A: Movie Load — enables the subtitle overlay for EVENT, STORY, and NONE movie types.</para>
/// <para>Module B: Pre-translation — walks subtitle records through XUAT's cache before the movie plays,
/// ensuring all lines are translated and ready when the cutscene begins.</para>
/// </summary>
[HarmonyPatch]
public static class SubtitleSystemPatch
{
    #region 1. Shared State
    /// <summary>Off-screen label used as a translation pump for subtitle records.</summary>
    private static CustomUILabel? _pretranslationLabel;

    private static readonly Vector3 _offscreenPosition    = new(0f, -1000f, 0f);
    private const float DefaultTranslationWaitSec         = 2.5f;
    private const long  WaitThresholdMs                   = 3_100L;
    #endregion

    // =========================================================================
    // MODULE A: Movie Load (MovieManager::Load)
    // =========================================================================

    #region 2. Module A — Hook
    [HarmonyPatch(typeof(MovieManager), "Load", new[]
    {
        typeof(eMovieType), typeof(long), typeof(bool), typeof(Il2CppSystem.Action),
        typeof(bool), typeof(long), typeof(bool), typeof(bool),
        typeof(bool), typeof(float), typeof(float), typeof(int)
    })]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixMovieLoad(MovieManager __instance, long _movieId, eMovieType _movieType)
    {
        if (!ConfigManager.UI.SubtitleFixes.Value) return;
        if (!__instance.IsSafe() || __instance.subtitle == null) return;

        // Only show subtitles for narrative movie types
        if (_movieType != eMovieType.EVENT
         && _movieType != eMovieType.STORY
         && _movieType != eMovieType.NONE)
        {
            FLog.Debug($"[Subtitle] Skipping subtitles for non-narrative movie type: {_movieType}");
            return;
        }

        FLog.Info($"[Subtitle] Enabling subtitles for {_movieType} movie (id={_movieId})");
        __instance.subtitle.Initialize(_movieId);
        __instance.subtitle.IsShow = true;
    }
    #endregion

    // =========================================================================
    // MODULE B: Pre-translation (SubtitleManager::Initialize)
    // =========================================================================

    #region 3. Module B — Hook
    [HarmonyPatch(typeof(SubtitleManager), "Initialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSubtitleInit(SubtitleManager __instance, long _movieId)
    {
        if (!ConfigManager.UI.SubtitleFixes.Value) return;
        if (!__instance.IsSafe()) return;

        if (__instance.data == null || __instance.data.Count == 0)
        {
            FLog.Debug($"[Subtitle] No subtitle data for movie id={_movieId}, skipping pre-translation.");
            return;
        }

        FLog.Info($"[Subtitle] Starting pre-translation pump for {__instance.data.Count} subtitle records (movie id={_movieId}).");

        EnsurePretranslationLabel();
        CoroutineStarter.Instance.StartCoroutine(
            BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(
                PretranslationCoroutine(__instance)));
    }
    #endregion

    #region 4. Module B — Logic
    private static void EnsurePretranslationLabel()
    {
        if (_pretranslationLabel.IsSafe()) return;

        var go = new GameObject("SubtitlePretranslationLabel");
        go.transform.localPosition = _offscreenPosition;
        _pretranslationLabel = go.AddComponent<CustomUILabel>();
        _pretranslationLabel.Persistent();
        FLog.Debug("[Subtitle] Pre-translation pump label created.");
    }

    private static IEnumerator PretranslationCoroutine(SubtitleManager manager)
    {
        yield return null; // wait one frame before starting

        var records = manager.data.recordList.ToArray();
        int    index       = 0;
        string lastText    = string.Empty;
        long   lastUpdateMs = NowMs();

        // Read translation wait delay from Util, fall back to default
        float xd = Util.GetXuatDelay();
        float waitSec = xd > 0f ? xd : DefaultTranslationWaitSec;

        while (_pretranslationLabel.IsSafe() && manager.data != null && index < manager.data.Count)
        {
            // If XUAT is still processing the previous line, keep waiting (up to threshold)
            if (lastText != string.Empty
             && lastText == _pretranslationLabel!.text
             && NowMs() - lastUpdateMs < WaitThresholdMs)
            {
                yield return null;
                continue;
            }

            var record = records[index];
            if (record?.text == null) { yield return null; continue; }

            lastUpdateMs               = NowMs();
            lastText                   = record.text;
            _pretranslationLabel!.text = lastText;
            index++;

            yield return new WaitForSecondsRealtime(waitSec);
        }

        // Clear the pump label when done
        if (_pretranslationLabel.IsSafe())
            _pretranslationLabel!.text = string.Empty;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long NowMs() => DateTime.Now.Ticks / 10_000L;
    #endregion

    // =========================================================================
    // MODULE C: Subtitle Display Refresh (MovieManager::dispSubTitle)
    // =========================================================================

    #region 5. Module C — Hook
    /// <summary>
    /// Postfix on <c>MovieManager.dispSubTitle</c>: restarts the subtitle display coroutine
    /// whenever the player's handle changes during playback.
    /// Without this patch the subtitle overlay stalls after a seek or skip event because
    /// the original coroutine has already exited and <c>IsShow</c> remains <c>true</c>.
    /// </summary>
    [HarmonyPatch(typeof(MovieManager), "dispSubTitle")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixDispSubTitle(MovieManager __instance, MovieManager.MoviePlayerInfo _playerInfo)
    {
        if (!__instance.IsSafe()) return;
        if (!__instance.subtitle.IsSafe() || !__instance.subtitle.IsShow) return;

        __instance.disposeSubTitleCoroutine();
        __instance.subTitleCoroutine = __instance.showSubtitle(_playerInfo.Handle);
        __instance.StartCoroutine(__instance.subTitleCoroutine);
    }
    #endregion

    // =========================================================================
    // MODULE D: Subtitle Live Text Injection (SubTitleTextPatch)
    // =========================================================================

    #region 6. Module D — Hook
    /// <summary>
    /// Prefix on <c>SubtitleManager.SetSubTitleText</c>:
    /// intercepts the live subtitle text assignment and sanitizes it before display.
    /// Equivalent to legacy <c>SubTitleTextPatch</c>.
    /// Guard: silently skips if <c>SetSubTitleText</c> does not exist in this game build.
    /// </summary>
    [HarmonyPatch(typeof(SubtitleManager), "SetSubTitleText")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixSetSubTitleText(SubtitleManager __instance, ref string text)
    {
        if (!ConfigManager.UI.SubtitleFixes.Value) return true;
        if (!__instance.IsSafe() || string.IsNullOrEmpty(text)) return true;

        text = text.Sanitize();
        FLog.Debug($"[Subtitle] SetSubTitleText injected: '{(text.Length > 40 ? text[..40] + "\u2026" : text)}'");
        return true;
    }
    #endregion
}
