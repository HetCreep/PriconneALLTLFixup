using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace PriconneALLTLFixup;

internal static class WindowsAPI
{
    #region 1. Win32 Constants (Essential for UI Fixes)
    public const int GWL_STYLE     = -16;
    public const int GWL_EXSTYLE   = -20;

    public const long WS_MAXIMIZEBOX   = 0x00010000L;
    public const long WS_THICKFRAME    = 0x00040000L;
    public const long WS_EX_ACCEPTFILES = 0x00000010L;

    // WndProc message codes
    public const uint WM_SETCURSOR  = 0x0020;
    public const uint WM_NCHITTEST  = 0x0084;

    // Hit-test return values
    public const int HTCLIENT      = 1;
    public const int HTTOP         = 12;
    public const int HTBOTTOM      = 15;
    public const int HTLEFT        = 10;
    public const int HTRIGHT       = 11;
    public const int HTTOPLEFT     = 13;
    public const int HTTOPRIGHT    = 14;
    public const int HTBOTTOMLEFT  = 16;
    public const int HTBOTTOMRIGHT = 17;

    // System cursor IDs
    public const uint IDC_SIZENWSE = 32642;
    public const uint IDC_SIZENESW = 32643;
    public const uint IDC_SIZEWE   = 32644;
    public const uint IDC_SIZENS   = 32645;

    // Resize handle sizes (px)
    public const int ResizeEdge   = 8;
    public const int ResizeCorner = 12;
    #endregion

    #region 2. Native P/Invoke Imports
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadCursor(IntPtr hInstance, uint lpCursorName);

    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    #endregion

    #region 2b. SetWindowPos Flags
    public const uint SWP_NOSIZE     = 0x0001;
    public const uint SWP_NOMOVE     = 0x0002;
    public const uint SWP_NOZORDER   = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    /// <summary>Restore position only, keep size and z-order.</summary>
    public const uint SWP_MOVE_ONLY  = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;
    /// <summary>Resize only, keep position and z-order.</summary>
    public const uint SWP_SIZE_ONLY  = SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE;
    #endregion

    #region 3. Architecture-Aware Wrappers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetWindowLong(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8) return (long)GetWindowLongPtr64(hWnd, nIndex);
        return GetWindowLong32(hWnd, nIndex);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, nIndex, (IntPtr)dwNewLong);
        else
            SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
    }
    #endregion

    #region 4. Win32 Interop Structs
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
    #endregion
}