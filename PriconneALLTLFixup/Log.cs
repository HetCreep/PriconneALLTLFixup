using BepInEx.Logging;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace PriconneALLTLFixup;

public static class Log
{
    #region 1. Internal Infrastructure
    private static ManualLogSource? _internalSource;

    public static bool IsActive => _internalSource != null;

    public static bool IsDeveloperContext => ConfigurationManager.Core.DebugMode.Value;
    #endregion

    #region 2. System Integration

    internal static void Initialize(ManualLogSource source)
    {
        if (_internalSource != null) return;
        _internalSource = source;
    }
    #endregion

    #region 3. General Logging API (User & General)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Info(object msg) => Dispatch(LogLevel.Info, msg);

    public static void Info(string template, params object[] args)
    {
        if (args == null || args.Length == 0) { Dispatch(LogLevel.Info, template); return; }
        try { Dispatch(LogLevel.Info, string.Format(template, args)); }
        catch { Dispatch(LogLevel.Info, $"[FormatErr] {template}"); }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Warn(object msg) => Dispatch(LogLevel.Warning, msg);

    public static void Error(object msg, Exception? ex = null)
    {
        if (ex != null) Dispatch(LogLevel.Error, $"{msg}\n[Trace]: {ex.Message}\n{ex.StackTrace}");
        else Dispatch(LogLevel.Error, msg);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fatal(object msg) => Dispatch(LogLevel.Fatal, msg);
    #endregion

    #region 4. Developer Diagnostics API
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Debug(object msg,
        [CallerFilePath] string path = "",
        [CallerMemberName] string method = "",
        [CallerLineNumber] int line = 0)
    {
        if (!IsDeveloperContext) return;

        string file = Path.GetFileNameWithoutExtension(path);
        Dispatch(LogLevel.Debug, $"[{file}@{line}] {method}: {msg}");
    }
    #endregion

    #region 5. Internal Core Logic
    private static void Dispatch(LogLevel level, object? message)
    {
        if (_internalSource == null) return;

        _internalSource.Log(level, message ?? "<NULL_MSG>");
    }
    #endregion
}