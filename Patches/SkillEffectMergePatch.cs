using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class SkillEffectMergePatch
{
    // --- Translation index (loaded from files, bypasses XUAT cache) ---
    private static readonly List<(Regex rx, string tpl)> _patterns = new List<(Regex, string)>();
    private static bool _loaded;

    // --- Effect buffer ---
    private static readonly List<string> _texts = new List<string>();
    private static readonly List<object> _uis   = new List<object>();
    private static readonly List<IntPtr> _ptrs  = new List<IntPtr>();
    private static long _lastTick;
    private static long WindowTicks => 200 * TimeSpan.TicksPerMillisecond;

    // Called once after XUAT loads translations — index all regex patterns from text files
    [HarmonyPatch(typeof(AutoTranslationPlugin), "LoadTranslations")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixLoadTranslations()
    {
        _patterns.Clear();
        _loaded = false;
        try
        {
            string root = Path.Combine(Paths.BepInExRootPath, "Translation",
                ConfigManager.Translation.Code.Value, "Text");
            if (!Directory.Exists(root)) return;

            foreach (string file in Directory.GetFiles(root, "*.txt", SearchOption.AllDirectories))
            {
                foreach (string rawLine in File.ReadLines(file, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    // Regex key format: r:"^...$"=template  OR  r:^...$=template
                    if (!line.StartsWith("r:", StringComparison.Ordinal)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 3) continue;

                    string keyPart = line.Substring(2, eq - 2).Trim().Trim('"');
                    string tpl     = line.Substring(eq + 1);

                    try
                    {
                        var rx = new Regex(keyPart,
                            RegexOptions.Compiled | RegexOptions.Singleline);
                        _patterns.Add((rx, tpl));
                    }
                    catch { /* bad regex — skip */ }
                }
            }
            _loaded = true;
            FLog.Info($"[SkillMerge] Indexed {_patterns.Count} regex patterns.");
        }
        catch (Exception ex) { FLog.Warn($"[SkillMerge] Index failed: {ex.Message}"); }
    }

    // --- Main hook: buffer effect texts, try combined, apply directly ---
    [HarmonyPatch(typeof(AutoTranslationPlugin), "TranslateOrQueueWebJobImmediate")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMergeEffects(
        object ui, string text,
        bool allowStabilizationOnTextComponent,
        bool ignoreComponentState)
    {
        if (!_loaded) return;

        string effectiveText = string.IsNullOrWhiteSpace(text)
            ? (ui as Il2CppSystem.Object)?.TryCast<UILabel>()?.text
            : text;
        if (string.IsNullOrWhiteSpace(effectiveText)) return;
        if (!HasJP(effectiveText)) return;
        if (effectiveText.Contains('\u203b') || effectiveText.Contains('[')) return;

        IntPtr ptr = (ui as Il2CppSystem.Object)?.Pointer ?? IntPtr.Zero;
        if (ptr == IntPtr.Zero) return;

        string flat = effectiveText.Replace("\n", string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(flat)) return;

        // Single-text translation (descriptions + single-effect skills)
        if (TryTranslate(flat, out string single))
        {
            FLog.Debug($"[SkillMerge] Single match: {flat.Substring(0, Math.Min(30, flat.Length))}");
            var lbl = (ui as Il2CppSystem.Object)?.TryCast<UILabel>();
            if (lbl.IsSafe()) lbl.text = single;
            return;
        }

        // Buffer for multi-effect combining
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastTick > WindowTicks) { _texts.Clear(); _uis.Clear(); _ptrs.Clear(); }
        _lastTick = now;

        if (_ptrs.Count > 0 && _ptrs[_ptrs.Count - 1] == ptr) return; // same label polled again
        _texts.Add(flat); _uis.Add(ui); _ptrs.Add(ptr);

        if (_texts.Count < 2) return;

        // Try all suffixes (handles description-before-effects)
        for (int start = 0; start <= _texts.Count - 2; start++)
        {
            string combined = string.Concat(_texts.GetRange(start, _texts.Count - start));
            if (!TryTranslate(combined, out string trans)) continue;

            FLog.Debug($"[SkillMerge] Combined match start={start}: {combined.Substring(0, Math.Min(40, combined.Length))}");
            var first = (_uis[start] as Il2CppSystem.Object)?.TryCast<UILabel>();
            if (first.IsSafe()) first.text = trans;

            for (int i = start + 1; i < _uis.Count; i++)
            {
                var lbl = (_uis[i] as Il2CppSystem.Object)?.TryCast<UILabel>();
                if (lbl.IsSafe()) lbl.text = string.Empty;
            }
            _texts.Clear(); _uis.Clear(); _ptrs.Clear();
            return;
        }
    }

    private static bool TryTranslate(string text, out string result)
    {
        foreach (var (rx, tpl) in _patterns)
        {
            var m = rx.Match(text);
            if (!m.Success) continue;
            var sb = new StringBuilder(tpl);
            for (int i = 1; i <= m.Groups.Count - 1; i++)
                sb.Replace("$" + i, m.Groups[i].Value);
            result = sb.ToString().Replace("\\n", "\n");
            return true;
        }
        result = null;
        return false;
    }

    private static bool HasJP(string s)
    {
        foreach (char c in s)
            if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF'))
                return true;
        return false;
    }
}
