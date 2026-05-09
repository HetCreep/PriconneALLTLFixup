#nullable enable
using BepInEx.Logging;
using System;
using System.Linq;

namespace PriconneALLTLFixup;

/// <summary>
/// Wraps the BepInEx DiskLogListener to suppress known-harmless HarmonyX warnings
/// that arise from IL2CPP assembly scanning at startup.
/// These are structural warnings produced by HarmonyLib.AccessTools when it encounters
/// IL2CPP interop assemblies (Il2Cppmscorlib, UnityEngine.*, etc.) whose types cannot
/// be fully reflected by the .NET runtime — this is expected behaviour in every
/// IL2CPP BepInEx environment and does not affect mod functionality.
/// </summary>
internal sealed class LogFilter : ILogListener
{
    #region 1. Filter Rules
    // Prefixes that identify harmless HarmonyX assembly-scan noise.
    // All messages from source "HarmonyX" matching any of these are suppressed.
    private static readonly string[] _suppressIfContains =
    {
        // IL2CPP interop assemblies HarmonyX cannot fully enumerate
        "AccessTools.GetTypesFromAssembly: assembly Il2Cppmscorlib",
        "AccessTools.GetTypesFromAssembly: assembly Il2CppSystem",
        "AccessTools.GetTypesFromAssembly: assembly __Generated",
        "AccessTools.GetTypesFromAssembly: assembly NativeFileBrowser",
        // UnityEngine module assemblies with unresolvable generic base types
        "AccessTools.GetTypesFromAssembly: assembly UnityEngine.",
        "AccessTools.GetTypesFromAssembly: assembly System.Windows.Forms",
        // Optional-patch method absence warnings (methods not in this game build)
        "AccessTools.DeclaredMethod: Could not find method for type Elements.MovieManager and name OnDestroy",
        "AccessTools.DeclaredMethod: Could not find method for type Elements.SubtitleManager and name SetSubTitleText",
        "AccessTools.DeclaredMethod: Could not find method for type Elements.ViewClanBattleRanking and name Initialize",
    };
    #endregion

    #region 2. Inner Listener (wrapped DiskLogListener)
    private readonly ILogListener _inner;

    private LogFilter(ILogListener inner) => _inner = inner;
    #endregion

    #region 3. Install / Uninstall
    /// <summary>
    /// Finds the BepInEx DiskLogListener, removes it from the global listener list,
    /// wraps it in a <see cref="LogFilter"/>, and registers the wrapper instead.
    /// Must be called early in <c>Plugin.Awake()</c> before the first log flush.
    /// </summary>
    public static void Install()
    {
        try
        {
            var disk = Logger.Listeners.FirstOrDefault(l => l.GetType().Name.Contains("DiskLog"));
            if (disk == null)
            {
                FLog.Debug("[LogFilter] DiskLogListener not found — filter not applied.");
                return;
            }

            Logger.Listeners.Remove(disk);
            Logger.Listeners.Add(new LogFilter(disk));
            FLog.Debug("[LogFilter] Installed — HarmonyX IL2CPP assembly-scan noise suppressed.");
        }
        catch (Exception ex)
        {
            FLog.Debug($"[LogFilter] Install failed (non-critical): {ex.Message}");
        }
    }

    /// <summary>Restores the original DiskLogListener (called on plugin unload).</summary>
    public static void Uninstall()
    {
        try
        {
            var filter = Logger.Listeners.OfType<LogFilter>().FirstOrDefault();
            if (filter == null) return;
            Logger.Listeners.Remove(filter);
            Logger.Listeners.Add(filter._inner);
        }
        catch { /* best-effort */ }
    }
    #endregion

    #region 4. ILogListener Implementation
    // Forward the filter level from the inner (wrapped) listener
    public LogLevel LogLevelFilter => _inner.LogLevelFilter;

    public void LogEvent(object sender, LogEventArgs eventArgs)
    {
        // Only suppress Warning-level messages from HarmonyX source
        if (eventArgs.Level == LogLevel.Warning &&
            eventArgs.Source?.SourceName?.Trim() == "HarmonyX")
        {
            string msg = eventArgs.Data?.ToString() ?? string.Empty;
            foreach (string pattern in _suppressIfContains)
            {
                if (msg.Contains(pattern, StringComparison.Ordinal))
                    return; // suppress — do not forward to disk
            }
        }

        _inner.LogEvent(sender, eventArgs);
    }

    public void Dispose()
    {
        try { _inner.Dispose(); } catch { }
    }
    #endregion
}
