using Cute;
using HarmonyLib;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Unified Windows system patch module combining display management and WndProc interception.
/// <para>Module A: Display, fullscreen toggle, hotkeys, and Win32 window style enforcement.</para>
/// <para>Module B: WndProc hook providing pixel-accurate resize handles and cursor feedback.</para>
/// </summary>
[HarmonyPatch]
public static class WindowSystemPatch
{
    #region 1. Shared State
    private static volatile bool _isTransitioning;

    // Last known windowed position (Win32 screen coords) — fallback: top-left origin
    private static int _lastX      = 0;
    private static int _lastY      = 0;

    // Last known windowed size — fallback: computed smart default (DPI-aware, not hardcoded 1280x720)
    private static int _lastWidth  = 0; // 0 = not yet computed; resolved in PostfixBoot
    private static int _lastHeight = 0;
    #endregion

    // =========================================================================
    // SMART SIZE UTILITY
    // =========================================================================

    /// <summary>
    /// Computes a sensible default windowed size based on native screen resolution.
    /// Targets ~65 % of native height, rounds to the nearest multiple of 90 lines
    /// to stay on clean 16:9 steps (720p → 900p → 1080p → 1440p …), then derives
    /// width at exactly 16:9 ratio.  Enforced minimum: 1280 × 720.
    /// </summary>
    /// <remarks>
    /// Examples:
    ///   1920 × 1080  →  1280 × 720   (65 % = 702 → rounds to 720)
    ///   2560 × 1440  →  1600 × 900   (65 % = 936 → rounds to 900)
    ///   3840 × 2160  →  2560 × 1440  (65 % = 1404 → rounds to 1440)
    ///   3840 × 1600  →  1760 × 990   (65 % = 1040 → rounds to 990)
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int w, int h) ComputeSmartWindowSize()
    {
        var native = Screen.currentResolution;

        // 65 % of native height, rounded to nearest multiple of 90
        int step    = 90;
        int rawH    = (int)(native.height * 0.65f);
        int targetH = (int)Math.Round((double)rawH / step) * step;
        targetH = Math.Max(720, targetH);                              // never below 720p

        // 16:9 width derived from height, capped so window fits with margin
        int targetW = targetH * 16 / 9;
        int maxW    = native.width  - 60;
        int maxH    = native.height - 60;
        targetW = Math.Min(targetW, maxW);
        targetH = Math.Min(targetH, maxH);

        FLog.Debug($"[Window] Smart size: native {native.width}×{native.height} → windowed {targetW}×{targetH}");
        return (targetW, targetH);
    }

    // =========================================================================
    // MODULE A: Display, Fullscreen & Hotkey Management
    // =========================================================================

    #region 2. Module A — Hooks
    [HarmonyPatch(typeof(BootApp), "Start")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixBoot()
    {
        if (!ConfigManager.Core.SystemIntegration.Value) return;

        FLog.Info("[Window] Module A: Display system initializing.");

        // Record current windowed size. If the game is already at a sensible resolution
        // (e.g. the user previously sized the window), use that directly.
        // Otherwise, fall back to a DPI-aware smart default instead of hardcoded 1280×720.
        if (Screen.width > 128 && Screen.height > 72)
        {
            _lastWidth  = Screen.width;
            _lastHeight = Screen.height;
        }
        else
        {
            // Screen not yet valid at this hook — compute smart default
            (_lastWidth, _lastHeight) = ComputeSmartWindowSize();
            FLog.Info($"[Window] Screen size unavailable at boot — smart fallback: {_lastWidth}×{_lastHeight}");
        }

        // Try to capture the initial Win32 window position (for accurate restore after F11 toggle).
        // Falls back to X=0, Y=0 (safe centre-screen default) if the handle is not ready yet.
        CoroutineStarter.DelayExecute(0.2f, () =>
        {
            try
            {
                IntPtr hWnd = WindowsAPI.GetActiveWindow();
                if (hWnd != IntPtr.Zero && WindowsAPI.GetWindowRect(hWnd, out var r)
                    && r.Left >= 0 && r.Top >= 0)
                {
                    _lastX = r.Left;
                    _lastY = r.Top;
                    FLog.Debug($"[Window] Initial position captured: X={_lastX}, Y={_lastY}");
                }
                else
                {
                    // Fallback: centre the window on the primary monitor
                    var native = Screen.currentResolution;
                    _lastX = Math.Max(0, (native.width  - _lastWidth)  / 2);
                    _lastY = Math.Max(0, (native.height - _lastHeight) / 2);
                    FLog.Debug($"[Window] Position not available — centred fallback: X={_lastX}, Y={_lastY}");
                }
            }
            catch (Exception ex) { FLog.Debug($"[Window] Position capture failed: {ex.Message}"); }
        });

        // Apply Win32 window chrome (resize handles, maximize button, drag-and-drop)
        CoroutineStarter.DelayExecute(0.5f, ApplyWindowIntegrity);

        // Register hotkey handler
        CoroutineStarter.OnFrameUpdate -= OnHandleInput;
        CoroutineStarter.OnFrameUpdate += OnHandleInput;

        FLog.Info($"[Window] Display system online — hotkeys (F11/Alt+Enter) registered. Current: {Screen.width}×{Screen.height} ({Screen.fullScreenMode})");
    }

    [HarmonyPatch(typeof(StandaloneWindowResize), "getOptimizedWindowSize")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixGetSize(StandaloneWindowResize __instance, ref Vector3 __result, int _width, int _height)
    {
        if (!ConfigManager.Core.SystemIntegration.Value) return true;

        int finalW = (_width  <= 128) ? __instance.windowLastWidth  : _width;
        int finalH = (_height <= 72)  ? __instance.windowLastHeight : _height;

        __result = new Vector3(finalW, finalH, (float)finalW / finalH);
        return false;
    }

    [HarmonyPatch(typeof(StandaloneWindowResize), "DisableMaximizebox")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixDisableMaximize() => !ConfigManager.Core.SystemIntegration.Value;
    #endregion

    #region 3. Module A — Operational Logic
    private static void OnHandleInput()
    {
        if (!ConfigManager.Core.SystemIntegration.Value || _isTransitioning) return;

        bool isAltEnter = (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                        && Input.GetKeyDown(KeyCode.Return);

        if (Input.GetKeyDown(KeyCode.F11) || isAltEnter)
            ApplyTransition();

        // Track windowed size AND position every frame while in windowed mode.
        // The 0.95 threshold filters out borderless-fullscreen states that report native width.
        if (!Screen.fullScreen && Screen.width < Screen.currentResolution.width * 0.95f)
        {
            _lastWidth  = Screen.width;
            _lastHeight = Screen.height;

            // Capture Win32 position for accurate restore after toggling back from fullscreen
            try
            {
                IntPtr hWnd = WindowsAPI.GetActiveWindow();
                if (hWnd != IntPtr.Zero && WindowsAPI.GetWindowRect(hWnd, out var r))
                {
                    _lastX = r.Left;
                    _lastY = r.Top;
                }
            }
            catch { /* position tracking is best-effort */ }
        }
    }

    /// <summary>
    /// Toggles between the configured fullscreen mode and windowed.
    /// Called ONLY by hotkey (F11 / Alt+Enter) — never on startup.
    /// </summary>
    private static void ApplyTransition()
    {
        if (_isTransitioning) return;
        _isTransitioning = true;
        try
        {
            var native = Screen.currentResolution;

            if (!Screen.fullScreen)
            {
                // Currently windowed → go fullscreen using configured mode
                var mode = ConfigManager.Core.DisplayMode.Value;
                Screen.SetResolution(native.width, native.height, mode);
                FLog.Info($"[Window] Toggling → {mode} ({native.width}×{native.height})");
            }
            else
            {
                // Currently fullscreen → restore windowed size.
                // If _lastWidth is 0 (first launch before any windowed state was tracked),
                // compute a smart default now.
                if (_lastWidth <= 128 || _lastHeight <= 72)
                    (_lastWidth, _lastHeight) = ComputeSmartWindowSize();

                Screen.SetResolution(_lastWidth, _lastHeight, FullScreenMode.Windowed);
                FLog.Info($"[Window] Restoring → Windowed ({_lastWidth}×{_lastHeight}) at ({_lastX}, {_lastY})");

                // Restore window position via Win32 after Unity processes the resolution change
                int capturedX = _lastX;
                int capturedY = _lastY;
                CoroutineStarter.DelayExecute(0.15f, () =>
                {
                    try
                    {
                        IntPtr hWnd = WindowsAPI.GetActiveWindow();
                        if (hWnd != IntPtr.Zero && (capturedX > 0 || capturedY > 0))
                            WindowsAPI.SetWindowPos(hWnd, IntPtr.Zero, capturedX, capturedY, 0, 0, WindowsAPI.SWP_MOVE_ONLY);
                    }
                    catch (Exception ex) { FLog.Debug($"[Window] Position restore failed: {ex.Message}"); }
                });
            }
        }
        catch (Exception ex) { FLog.Error("Display transition failed", ex); }
        finally { _isTransitioning = false; }
    }

    private static void ApplyWindowIntegrity()
    {
        try
        {
            IntPtr hWnd = WindowsAPI.GetActiveWindow();
            if (hWnd == IntPtr.Zero) return;

            long style = WindowsAPI.GetWindowLong(hWnd, WindowsAPI.GWL_STYLE)
                         | WindowsAPI.WS_MAXIMIZEBOX
                         | WindowsAPI.WS_THICKFRAME;
            WindowsAPI.SetWindowLong(hWnd, WindowsAPI.GWL_STYLE, style);

            long exStyle = WindowsAPI.GetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE)
                           | WindowsAPI.WS_EX_ACCEPTFILES;
            WindowsAPI.SetWindowLong(hWnd, WindowsAPI.GWL_EXSTYLE, exStyle);

            FLog.Debug("[System] Window styles synchronized with OS.");
        }
        catch (Exception ex) { FLog.Debug($"[System] OS Integrity Sync bypassed: {ex.Message}"); }
    }
    #endregion

    // =========================================================================
    // MODULE B: WndProc — Pixel-Accurate Resize Handles & Cursor Feedback
    // =========================================================================

    #region 4. Module B — Hooks
    /// <summary>Intercepts the instance WndProc to inject resize hit-testing.</summary>
    [HarmonyPatch(typeof(StandaloneWindowResize), "WndProc")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixWndProc(IntPtr _hWnd, uint _msg, IntPtr _wParam, IntPtr lParam, ref IntPtr __result)
    {
        return HandleWndProc(_hWnd, _msg, _wParam, lParam, ref __result);
    }

    /// <summary>Intercepts the static WndProc delegate — delegates to the same handler.</summary>
    [HarmonyPatch(typeof(StandaloneWindowResize), "WndProcStatic")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixWndProcStatic(IntPtr _hWnd, uint _msg, IntPtr _wParam, IntPtr _lParam, ref IntPtr __result)
    {
        return HandleWndProc(_hWnd, _msg, _wParam, _lParam, ref __result);
    }
    #endregion

    #region 5. Module B — Core WndProc Logic
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HandleWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref IntPtr result)
    {
        // Fast-path: cursor feedback on WM_SETCURSOR
        if (msg == WindowsAPI.WM_SETCURSOR && WindowsAPI.GetCursorPos(out var pt))
        {
            int hit = ComputeHitTest(hWnd, pt.X, pt.Y);
            IntPtr cursor = GetResizeCursor(hit);
            if (cursor != IntPtr.Zero)
            {
                WindowsAPI.SetCursor(cursor);
                result = (IntPtr)1;
                return false;
            }
        }

        // Default: delegate to the game's original WndProc
        result = WindowsAPI.CallWindowProc(StandaloneWindowResize.oldWndProcPtr, hWnd, msg, wParam, lParam);

        // Override hit-test result with our own border detection
        if (msg == WindowsAPI.WM_NCHITTEST)
        {
            int x   = (short)(lParam.ToInt32() & 0xFFFF);
            int y   = (short)((lParam.ToInt32() >> 16) & 0xFFFF);
            int hit = ComputeHitTest(hWnd, x, y);
            if (hit != WindowsAPI.HTCLIENT)
                result = (IntPtr)hit;
        }

        return false;
    }

    /// <summary>Computes the resize hit-test zone from cursor position relative to window rect.</summary>
    private static int ComputeHitTest(IntPtr hWnd, int x, int y)
    {
        if (!WindowsAPI.GetWindowRect(hWnd, out var rect)) return WindowsAPI.HTCLIENT;

        int fromLeft   = x - rect.Left;
        int fromRight  = rect.Right  - x;
        int fromTop    = y - rect.Top;
        int fromBottom = rect.Bottom - y;

        bool nearLeft   = fromLeft   is >= 0 and < WindowsAPI.ResizeEdge;
        bool nearRight  = fromRight  is >= 0 and < WindowsAPI.ResizeEdge;
        bool nearTop    = fromTop    is >= 0 and < WindowsAPI.ResizeEdge;
        bool nearBottom = fromBottom is >= 0 and < WindowsAPI.ResizeEdge;

        bool cornerTL = fromLeft  is >= 0 and < WindowsAPI.ResizeCorner && fromTop    is >= 0 and < WindowsAPI.ResizeCorner;
        bool cornerTR = fromRight is >= 0 and < WindowsAPI.ResizeCorner && fromTop    is >= 0 and < WindowsAPI.ResizeCorner;
        bool cornerBL = fromLeft  is >= 0 and < WindowsAPI.ResizeCorner && fromBottom is >= 0 and < WindowsAPI.ResizeCorner;
        bool cornerBR = fromRight is >= 0 and < WindowsAPI.ResizeCorner && fromBottom is >= 0 and < WindowsAPI.ResizeCorner;

        if (cornerTL) return WindowsAPI.HTTOPLEFT;
        if (cornerTR) return WindowsAPI.HTTOPRIGHT;
        if (cornerBL) return WindowsAPI.HTBOTTOMLEFT;
        if (cornerBR) return WindowsAPI.HTBOTTOMRIGHT;
        if (nearLeft)   return WindowsAPI.HTLEFT;
        if (nearRight)  return WindowsAPI.HTRIGHT;
        if (nearTop)    return WindowsAPI.HTTOP;
        if (nearBottom) return WindowsAPI.HTBOTTOM;

        return WindowsAPI.HTCLIENT;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IntPtr GetResizeCursor(int hitTest) => hitTest switch
    {
        WindowsAPI.HTLEFT  or WindowsAPI.HTRIGHT       => WindowsAPI.LoadCursor(IntPtr.Zero, WindowsAPI.IDC_SIZEWE),
        WindowsAPI.HTTOP   or WindowsAPI.HTBOTTOM      => WindowsAPI.LoadCursor(IntPtr.Zero, WindowsAPI.IDC_SIZENS),
        WindowsAPI.HTTOPLEFT or WindowsAPI.HTBOTTOMRIGHT => WindowsAPI.LoadCursor(IntPtr.Zero, WindowsAPI.IDC_SIZENWSE),
        WindowsAPI.HTTOPRIGHT or WindowsAPI.HTBOTTOMLEFT => WindowsAPI.LoadCursor(IntPtr.Zero, WindowsAPI.IDC_SIZENESW),
        _ => IntPtr.Zero
    };
    #endregion
}
