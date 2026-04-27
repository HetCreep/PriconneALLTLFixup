using System;
using UnityEngine;
using HarmonyLib;
using Cute;

namespace PriconneALLTLFixup.Patches;

public static class DisplayModePatch
{
    #region 1. Environment Constants (Win32 API)
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_EX_ACCEPTFILES = 0x00000100L;
    #endregion

    #region 2. Runtime State Management
    private static bool _isTransitioning;
    private static int _lastWidth = 1280;
    private static int _lastHeight = 720;
    #endregion

    #region 3. Harmony Patch EntryPoints
    [HarmonyPatch(typeof(BootApp), "Start")]
    public static class LifecyclePatch
    {
        [HarmonyPostfix]
        [HarmonyWrapSafe]
        public static void Postfix()
        {
            if (!ConfigurationManager.Core.SystemIntegration.Value) return;

            Log.Info("[Display] Synchronizing system integration components...");

            ApplyWindowIntegrity();
            ApplyTransition(isInitialLoad: true);

            CoroutineStarter.OnFrameUpdate -= OnFrameUpdate;
            CoroutineStarter.OnFrameUpdate += OnFrameUpdate;
        }
    }

    [HarmonyPatch(typeof(StandaloneWindowResize), "getOptimizedWindowSize")]
    public static class InternalResolutionPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(StandaloneWindowResize __instance, ref Vector3 __result, int _width, int _height)
        {
            if (!ConfigurationManager.Core.SystemIntegration.Value) return true;

            int finalW = (_width <= 128) ? __instance.windowLastWidth : _width;
            int finalH = (_height <= 72) ? __instance.windowLastHeight : _height;

            float aspect = (float)finalW / finalH;
            __result = new Vector3(finalW, finalH, aspect);

            return false;
        }
    }

    [HarmonyPatch(typeof(StandaloneWindowResize), "DisableMaximizebox")]
    public static class InternalMaximizePatch
    {
        public static bool Prefix()
        {
            return !ConfigurationManager.Core.SystemIntegration.Value;
        }
    }
    #endregion

    #region 4. Operational Logic (Display & Input)
    private static void OnFrameUpdate()
    {
        if (_isTransitioning) return;

        bool requestToggle = Input.GetKeyDown(KeyCode.F11) ||
                           ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.Return));

        if (requestToggle) ApplyTransition(false);

        if (!Screen.fullScreen && Screen.width < Screen.currentResolution.width * 0.95f)
        {
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
        }
    }

    private static void ApplyTransition(bool isInitialLoad)
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        try
        {
            Resolution native = Screen.currentResolution;
            if (isInitialLoad || !Screen.fullScreen)
            {
                FullScreenMode targetMode = ConfigurationManager.Core.DisplayMode.Value;
                Screen.SetResolution(native.width, native.height, targetMode);
            }
            else
            {
                Screen.SetResolution(_lastWidth, _lastHeight, FullScreenMode.Windowed);
            }
        }
        catch (Exception ex) { Log.Error("Display transition encountered an error", ex); }
        finally { _isTransitioning = false; }
    }
    #endregion

    #region 5. OS Integrity Operations (Win32)
    private static void ApplyWindowIntegrity()
    {
        try
        {
            IntPtr hWnd = WindowsAPI.GetActiveWindow();
            if (hWnd == IntPtr.Zero) return;

            long style = WindowsAPI.GetWindowLong(hWnd, GWL_STYLE) | WS_MAXIMIZEBOX | WS_THICKFRAME;
            WindowsAPI.SetWindowLong(hWnd, GWL_STYLE, style);

            long exStyle = WindowsAPI.GetWindowLong(hWnd, GWL_EXSTYLE) | WS_EX_ACCEPTFILES;
            WindowsAPI.SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
        }
        catch (Exception ex) { Log.Debug($"[System] Window integrity synchronization skipped: {ex.Message}"); }
    }
    #endregion
}