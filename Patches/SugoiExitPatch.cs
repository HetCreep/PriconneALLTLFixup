#nullable enable
using Cute;
using Elements;
using HarmonyLib;
using SugoiOfflineTranslator;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Manages the lifecycle of the Sugoi Offline Translator process.
/// Captures the process handle on startup and terminates it cleanly when the game exits,
/// preventing orphaned background processes from persisting after the game closes.
/// </summary>
[HarmonyPatch]
public static class SugoiExitPatch
{
    #region 1. Internal State
    /// <summary>Handle to the Sugoi translator child process. Null if not running.</summary>
    private static Process? _process;
    #endregion

    #region 2. Module A: Process Capture (StartProcess)
    [HarmonyPatch(typeof(SugoiOfflineTranslatorEndpoint), "StartProcess")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixStartProcess(Process ___process)
    {
        if (!ConfigManager.Gameplay.SugoiCleanup.Value) return;
        _process = ___process;
        FLog.Info("[Sugoi] Offline translator process captured and registered for lifecycle management.");
    }
    #endregion

    #region 3. Module B: Clean Shutdown (ApplicationQuit)
    // NOTE: Toolbox removed in 13-May-2026 game update.
    // Fallback: Plugin.Unload() now calls PrefixApplicationQuit() directly.
    [HarmonyPatch(typeof(Toolbox), "ApplicationQuit")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixApplicationQuit()
    {
        TerminateProcess();
    }
    #endregion

    #region 4. Internal Logic
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TerminateProcess()
    {
        if (_process == null) return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                FLog.Info("[Sugoi] Offline translator process terminated cleanly on application exit.");
            }
        }
        catch (Exception ex)
        {
            FLog.Debug($"[Sugoi] Process termination bypassed: {ex.Message}");
        }
        finally
        {
            _process = null;
        }
    }
    #endregion

    // =========================================================================
    // MODULE C: Subtitle Coroutine Cleanup on Scene Change (SugoiExitPatch2)
    // =========================================================================

    #region 5. Module C — Scene Unload Cleanup
    /// <summary>
    /// Postfix on <c>MovieManager.OnDestroy</c> (via ManagerSingleton base):
    /// disposes the subtitle display coroutine when the movie manager is destroyed
    /// (e.g., on scene change or movie skip). Equivalent to legacy SugoiExitPatch2.
    /// Prevents subtitle coroutine memory leaks from persisting across scenes.
    /// </summary>
    [HarmonyPatch(typeof(MovieManager), "OnDestroy")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMovieManagerDestroy(MovieManager __instance)
    {
        if (!__instance.IsSafe()) return;

        try
        {
            if (__instance.subTitleCoroutine != null)
            {
                __instance.disposeSubTitleCoroutine();
                FLog.Debug("[Sugoi] Subtitle display coroutine disposed on MovieManager destroy.");
            }
        }
        catch (Exception ex)
        {
            FLog.Debug($"[Sugoi] Subtitle coroutine cleanup bypassed: {ex.Message}");
        }
    }
    #endregion
}
