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
    private static readonly List<(Regex rx, string tpl)> _patterns = new List<(Regex, string)>();
    private static bool _indexDone;

    private static readonly List<string> _texts = new List<string>();
    private static readonly List<object> _uis   = new List<object>();
    private static readonly List<IntPtr> _ptrs  = new List<IntPtr>();
    private static long _lastTick;
    private static long WindowTicks => 200 * TimeSpan.TicksPerMillisecond;

    private static readonly Regex _nguiTag =
        new Regex(@"\[(?:[^\]]{0,20})\]", RegexOptions.Compiled);

    // Trigger index build after XUAT loads translations
    [HarmonyPatch(typeof(AutoTranslationPlugin), "LoadTranslations")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixLoadTranslations() => BuildIndex();

    private static void BuildIndex()
    {
        _patterns.Clear();
        try
        {
            string translationRoot = Path.Combine(Paths.BepInExRootPath, "Translation");
            if (!Directory.Exists(translationRoot))
            {
                FLog.Warn("[SkillMerge] Translation root not found: " + translationRoot);
                return;
            }

            // Scan ALL language subdirectories (en, th, etc.)
            foreach (string langDir in Directory.GetDirectories(translationRoot))
            {
                string textDir = Path.Combine(langDir, "Text");
                if (!Directory.Exists(textDir)) continue;

                foreach (string file in Directory.GetFiles(textDir, "*.txt", SearchOption.AllDirectories))
                {
                    try { IndexFile(file); }
                    catch { /* bad file — skip */ }
                }
            }
            FLog.Info($"[SkillMerge] Indexed {_patterns.Count} regex patterns from {translationRoot}");
        }
        catch (Exception ex) { FLog.Warn("[SkillMerge] Index error: " + ex.Message); }
        finally { _indexDone = true; }
    }

    private static void IndexFile(string file)
    {
        foreach (string rawLine in File.ReadLines(file, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (!line.StartsWith("r:", StringComparison.Ordinal)) continue;
            int eq = line.IndexOf('=');
            if (eq < 3) continue;
            string keyPart = line.Substring(2, eq - 2).Trim().Trim('"');
            string tpl     = line.Substring(eq + 1);
            try { _patterns.Add((new Regex(keyPart, RegexOptions.Compiled | RegexOptions.Singleline), tpl)); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(AutoTranslationPlugin), "TranslateOrQueueWebJobImmediate")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMergeEffects(object ui, string text)
    {
        // Lazy index on first call in case LoadTranslations fired before our patch registered
        if (!_indexDone) BuildIndex();
        if (_patterns.Count == 0) return;

        string effectiveText = string.IsNullOrWhiteSpace(text)
            ? (ui as Il2CppSystem.Object)?.TryCast<UILabel>()?.text
            : text;
        if (string.IsNullOrWhiteSpace(effectiveText)) return;
        if (!HasJP(effectiveText)) return;
        if (effectiveText.Contains('\u203b')) return;

        IntPtr ptr = (ui as Il2CppSystem.Object)?.Pointer ?? IntPtr.Zero;
        if (ptr == IntPtr.Zero) return;

        // Strip NGUI markup, collapse newlines, trim all Unicode whitespace incl U+3000
        string flat = _nguiTag.Replace(effectiveText.Replace("\n", string.Empty), string.Empty)
                               .Trim('\u3000', '\u00A0', '\u200B', ' ', '\t');
        if (string.IsNullOrWhiteSpace(flat) || !HasJP(flat)) return;

        // Single-text: try direct regex match first
        if (TryTranslate(flat, out string single))
        {
            FLog.Debug($"[SkillMerge] ✓ Single: {flat.Substring(0, Math.Min(30, flat.Length))}");
            var lbl = (ui as Il2CppSystem.Object)?.TryCast<UILabel>();
            if (lbl.IsSafe()) lbl.text = single;
            return;
        }

        // Buffer for combined multi-effect key
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastTick > WindowTicks) { _texts.Clear(); _uis.Clear(); _ptrs.Clear(); }
        _lastTick = now;

        if (_ptrs.Count > 0 && _ptrs[_ptrs.Count - 1] == ptr) return;
        _texts.Add(flat); _uis.Add(ui); _ptrs.Add(ptr);

        if (_texts.Count < 2) return;

        for (int start = 0; start <= _texts.Count - 2; start++)
        {
            string combined = string.Concat(_texts.GetRange(start, _texts.Count - start));
            if (!TryTranslate(combined, out string trans)) continue;

            FLog.Debug($"[SkillMerge] ✓ Combined[{start}]: {combined.Substring(0, Math.Min(40, combined.Length))}");
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
