using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Hooks UILabel.set_text directly (bypasses XUAT's TextManipulator limitation for UILabel)
/// to apply skill effect translations via regex patterns loaded from XUAT text files.
/// </summary>
[HarmonyPatch]
public static class SkillEffectMergePatch
{
    private static readonly List<(Regex rx, string tpl)> _patterns = new List<(Regex, string)>();
    private static bool _indexDone;
    private static bool _setting; // recursion guard for set_text

    private static readonly List<string> _texts = new List<string>();
    private static readonly List<UILabel> _uis   = new List<UILabel>();
    private static long _lastTick;
    private static long WindowTicks => 200 * TimeSpan.TicksPerMillisecond;

    private static readonly Regex _nguiTag =
        new Regex(@"\[(?:[^\]]{0,20})\]", RegexOptions.Compiled);

    // Fires at startup (confirmed working in TextRegistryPatch)
    [HarmonyPatch(typeof(ConstTextData), nameof(ConstTextData.CreateInstanceAndLoadInitialize))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixConstTextInit() => BuildIndex();

    // Also fires when XUAT reloads translations
    [HarmonyPatch(typeof(AutoTranslationPlugin), "LoadTranslations")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixLoadTranslations() => BuildIndex();

    public static void BuildIndex()
    {
        FLog.Info("[SkillMerge] BuildIndex called from: " + System.Environment.StackTrace.Substring(0, Math.Min(80, System.Environment.StackTrace.Length)));
        _patterns.Clear();
        try
        {
            string root = Path.Combine(Paths.BepInExRootPath, "Translation");
            if (!Directory.Exists(root)) { FLog.Warn("[SkillMerge] Translation dir not found"); return; }
            foreach (string lang in Directory.GetDirectories(root))
            {
                string txt = Path.Combine(lang, "Text");
                if (!Directory.Exists(txt)) continue;
                foreach (string f in Directory.GetFiles(txt, "*.txt", SearchOption.AllDirectories))
                    try { IndexFile(f); } catch { }
            }
        }
        catch (Exception ex) { FLog.Warn("[SkillMerge] " + ex.Message); }
        finally
        {
            _indexDone = true;
            FLog.Info($"[SkillMerge] Indexed {_patterns.Count} regex patterns");
        }
    }

    private static void IndexFile(string file)
    {
        foreach (string raw in File.ReadLines(file, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (!line.StartsWith("r:", StringComparison.Ordinal)) continue;
            int eq = line.IndexOf('='); if (eq < 3) continue;
            string key = line.Substring(2, eq - 2).Trim().Trim('"');
            string tpl = line.Substring(eq + 1);
            try { _patterns.Add((new Regex(key, RegexOptions.Compiled | RegexOptions.Singleline), tpl)); }
            catch { }
        }
    }

    // Hook UILabel.set_text — fires every time any UILabel's text is changed by game code
    [HarmonyPatch(typeof(UILabel), "set_text")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSetText(UILabel __instance, string value)
    {
        if (_setting) return;
        if (!_indexDone) BuildIndex();
        if (_patterns.Count == 0) return;
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!HasJP(value)) return;
        if (value.Contains('\u203b')) return;

        // Strip NGUI tags, collapse \n, trim all whitespace including U+3000
        string flat = _nguiTag.Replace(value.Replace("\n", string.Empty), string.Empty)
                               .Trim('\u3000', '\u00A0', '\u200B', ' ', '\t');
        if (string.IsNullOrWhiteSpace(flat) || !HasJP(flat)) return;

        // Single match
        if (TryTranslate(flat, out string single))
        {
            FLog.Debug($"[SkillMerge] ✓ Single: {flat.Substring(0, Math.Min(30, flat.Length))}");
            _setting = true;
            try { __instance.text = single; }
            finally { _setting = false; }
            return;
        }

        // Buffer for combined key
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastTick > WindowTicks) { _texts.Clear(); _uis.Clear(); }
        _lastTick = now;

        // Dedup by UILabel reference
        if (_uis.Count > 0 && ReferenceEquals(_uis[_uis.Count - 1], __instance)) return;
        _texts.Add(flat);
        _uis.Add(__instance);

        if (_texts.Count < 2) return;

        for (int start = 0; start <= _texts.Count - 2; start++)
        {
            string combined = string.Concat(_texts.GetRange(start, _texts.Count - start));
            if (!TryTranslate(combined, out string trans)) continue;

            FLog.Debug($"[SkillMerge] ✓ Combined[{start}]: {combined.Substring(0, Math.Min(40, combined.Length))}");
            _setting = true;
            try
            {
                _uis[start].text = trans;
                for (int i = start + 1; i < _uis.Count; i++)
                    _uis[i].text = string.Empty;
            }
            finally { _setting = false; }
            _texts.Clear(); _uis.Clear();
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
            for (int i = 1; i < m.Groups.Count; i++)
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
