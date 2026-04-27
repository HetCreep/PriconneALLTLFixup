using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using HarmonyLib;
using Cute;
using PriconneALLTLFixup;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class WindowCorePatch
{
    #region 1. System Constants (Win32)
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_MAXIMIZEBOX = 0x00010000L;
    private const long WS_THICKFRAME = 0x00040000L;
    private const long WS_EX_ACCEPTFILES = 0x00000100L;
    #endregion

    #region 2. State Control
    private static bool _isTransitioning;
    private static int _lastWidth = 1280;
    private static int _lastHeight = 720;
    #endregion

    #region 3. Core Hooks (Integrated)
    [HarmonyPatch(typeof(BootApp), "Start")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void InitializeWindow()
    {
        if (!ConfigurationManager.Core.SystemIntegration.Value) return;

        Log.Info("[Window] Initializing Core Display System...");

        ApplyWindowIntegrity();
        ApplyTransition(isInitialLoad: true);

        CoroutineStarter.OnFrameUpdate -= OnHandleInput;
        CoroutineStarter.OnFrameUpdate += OnHandleInput;
    }

    [HarmonyPatch(typeof(StandaloneWindowResize), "getOptimizedWindowSize")]
    [HarmonyPrefix]
    public static bool PrefixGetSize(StandaloneWindowResize __instance, ref Vector3 __result, int _width, int _height)
    {
        if (!ConfigurationManager.Core.SystemIntegration.Value) return true;

        int finalW = (_width <= 128) ? __instance.windowLastWidth : _width;
        int finalH = (_height <= 72) ? __instance.windowLastHeight : _height;

        __result = new Vector3(finalW, finalH, (float)finalW / finalH);
        return false;
    }

    [HarmonyPatch(typeof(StandaloneWindowResize), "DisableMaximizebox")]
    [HarmonyPrefix]
    public static bool PrefixDisableMaximize() => !ConfigurationManager.Core.SystemIntegration.Value;
    #endregion

    #region 4. Operational Logic
    private static void OnHandleInput()
    {
        if (_isTransitioning) return;

        bool isAltEnter = (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.Return);
        if (Input.GetKeyDown(KeyCode.F11) || isAltEnter)
        {
            ApplyTransition(false);
        }

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
            var native = Screen.currentResolution;
            if (isInitialLoad || !Screen.fullScreen)
            {
                var mode = ConfigurationManager.Core.DisplayMode.Value;
                Screen.SetResolution(native.width, native.height, mode);
                Log.Debug($"[Window] Transitioning to {mode} ({native.width}x{native.height})");
            }
            else
            {
                Screen.SetResolution(_lastWidth, _lastHeight, FullScreenMode.Windowed);
                Log.Debug($"[Window] Restoring Windowed mode ({_lastWidth}x{_lastHeight})");
            }
        }
        catch (Exception ex) { Log.Error("Display transition failed", ex); }
        finally { _isTransitioning = false; }
    }
    #endregion

    #region 5. OS Integrity API (Win32)
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

            Log.Debug("[System] Window styles synchronized with OS.");
        }
        catch (Exception ex) { Log.Debug($"[System] OS Integrity Sync bypassed: {ex.Message}"); }
    }
    #endregion
}