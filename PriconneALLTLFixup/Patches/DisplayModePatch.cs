using System;
using UnityEngine;
using HarmonyLib;
using Cute;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch(typeof(BootApp), "Start")]
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

    #region 3. Initialization
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void InitializeDisplay()
    {
        if (!ConfigurationManager.Core.SystemIntegration.Value)
        {
            Log.Debug("[Display] System integration is disabled via configuration.");
            return;
        }

        Log.Info("[Display] Harmonizing application environment...");

        ApplyWindowIntegrity();

        ApplyTransition(true);

        CoroutineStarter.OnFrameUpdate -= OnFrameUpdate;
        CoroutineStarter.OnFrameUpdate += OnFrameUpdate;
    }
    #endregion

    #region 4. Input Monitoring (Hotkeys)
    private static void OnFrameUpdate()
    {
        if (_isTransitioning) return;

        bool requestToggle = Input.GetKeyDown(KeyCode.F11) ||
                           ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.Return));

        if (requestToggle)
        {
            ApplyTransition(isInitialLoad: false);
        }

        if (!Screen.fullScreen && Screen.width < Screen.currentResolution.width * 0.95f)
        {
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
        }
    }
    #endregion

    #region 5. Core Transition Engine (Unity Display)
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

                string familiarName = targetMode switch
                {
                    FullScreenMode.ExclusiveFullScreen => "Fullscreen",
                    FullScreenMode.FullScreenWindow => "Window Borderless",
                    FullScreenMode.MaximizedWindow => "Maximized Window",
                    FullScreenMode.Windowed => "Windowed",
                    _ => targetMode.ToString()
                };

                Log.Debug($"[Display] Transitioning to {familiarName}: {native.width}x{native.height}");
                Screen.SetResolution(native.width, native.height, targetMode);
            }
            else
            {
                Log.Debug($"[Display] Reverting to Windowed Mode: {_lastWidth}x{_lastHeight}");
                Screen.SetResolution(_lastWidth, _lastHeight, FullScreenMode.Windowed);
            }
        }
        catch (Exception ex) { Log.Error("Display mode transition failed", ex); }
        finally { _isTransitioning = false; }
    }
    #endregion

    #region 6. OS Integration Logic (Win32 Style)
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

            Log.Debug("[System] Native window styles synchronized with OS.");
        }
        catch (Exception ex) { Log.Debug($"[System] Window integrity check bypassed: {ex.Message}"); }
    }
    #endregion
}