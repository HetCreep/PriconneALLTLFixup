using BepInEx;
using Elements;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

// ── Shared state ──────────────────────────────────────────────────────────────
public static class SkillMergeIndex
{
    public static readonly List<(Regex rx, string tpl)> Patterns = new();
    public static bool Done;

    private static readonly Regex NgTag = new(@"\[(?:[^\]]{0,20})\]", RegexOptions.Compiled);

    public static void Build()
    {
        Patterns.Clear();
        try
        {
            string root = Path.Combine(Paths.BepInExRootPath, "Translation");
            if (!Directory.Exists(root)) { FLog.Warn("[SkillMerge] Translation dir missing"); return; }
            int total = 0;
            foreach (string lang in Directory.GetDirectories(root))
            {
                string txt = Path.Combine(lang, "Text");
                if (!Directory.Exists(txt)) continue;
                foreach (string f in Directory.GetFiles(txt, "*.txt", SearchOption.AllDirectories))
                    try { total += IndexFile(f); } catch { }
            }
            FLog.Info($"[SkillMerge] Indexed {total} regex patterns");
        }
        catch (Exception ex) { FLog.Warn("[SkillMerge] Build error: " + ex.Message); }
        finally { Done = true; }
    }

    private static int IndexFile(string file)
    {
        int n = 0;
        foreach (string raw in File.ReadLines(file, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (!line.StartsWith("r:", StringComparison.Ordinal)) continue;
            int eq = line.IndexOf('='); if (eq < 3) continue;
            string key = line.Substring(2, eq - 2).Trim().Trim('"');
            string tpl = line.Substring(eq + 1);
            try { Patterns.Add((new Regex(key, RegexOptions.Compiled | RegexOptions.Singleline), tpl)); n++; }
            catch { }
        }
        return n;
    }

    public static bool TryTranslate(string text, out string result)
    {
        foreach (var (rx, tpl) in Patterns)
        {
            var m = rx.Match(text);
            if (!m.Success) continue;
            var sb = new StringBuilder(tpl);
            for (int i = 1; i < m.Groups.Count; i++)
                sb.Replace("$" + i, m.Groups[i].Value);
            result = sb.ToString().Replace("\\n", "\n");
            return true;
        }
        result = null; return false;
    }

    public static bool HasJP(string s)
    {
        foreach (char c in s)
            if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF'))
                return true;
        return false;
    }

    public static string Flatten(string s) =>
        NgTag.Replace(s.Replace("\n", string.Empty), string.Empty)
             .Trim('\u3000', '\u00A0', '\u200B', ' ', '\t');
}

// ── Patch 1: build index at startup ──────────────────────────────────────────
[HarmonyPatch]
public static class SkillMergeIndexPatch
{
    internal static bool Ready;

    [HarmonyPatch(typeof(ConstTextData), nameof(ConstTextData.CreateInstanceAndLoadInitialize))]
    [HarmonyPostfix][HarmonyWrapSafe]
    public static void OnConstText()
    {
        FLog.Info("[SkillMerge] ConstText hook fired");
        SkillMergeIndex.Build();
        Ready = true; // IL2CPP objects now stable — safe for UILabel manipulation
    }

    [HarmonyPatch(typeof(AutoTranslationPlugin), "LoadTranslations")]
    [HarmonyPostfix][HarmonyWrapSafe]
    public static void OnLoadTranslations() => SkillMergeIndex.Build();
}

// ── Patch 2: hook UILabel.set_text ────────────────────────────────────────────
[HarmonyPatch]
public static class SkillMergeSetTextPatch
{
    private static readonly List<string>   _texts = new();
    private static readonly List<UILabel>  _uis   = new();
    private static long _lastTick;
    private static long Window => 200 * TimeSpan.TicksPerMillisecond;
    private static bool _applying;

    [HarmonyPatch(typeof(AutoTranslationPlugin), "TranslateOrQueueWebJobImmediate")]
    [HarmonyPostfix][HarmonyWrapSafe]
    public static void OnAfterXuat(
        object ui,
        string text,
        int scope,
        object info,
        bool allowStabilizationOnTextComponent,
        bool ignoreComponentState)
    {
        if (!SkillMergeIndexPatch.Ready) return;   // gate: only after ConstTextData init
        if (_applying) return;
        if (!SkillMergeIndex.Done || SkillMergeIndex.Patterns.Count == 0) return;
        if (!string.IsNullOrWhiteSpace(text)) return; // XUAT polls with empty text only
        try
        {
            string current = (ui as Il2CppSystem.Object)?.TryCast<UILabel>()?.text;
            if (string.IsNullOrWhiteSpace(current) || !SkillMergeIndex.HasJP(current)) return;

            string flat = SkillMergeIndex.Flatten(current);
            if (string.IsNullOrWhiteSpace(flat) || !SkillMergeIndex.HasJP(flat)) return;

            // Single-effect match
            if (SkillMergeIndex.TryTranslate(flat, out string single))
            {
                FLog.Debug($"[SkillMerge] ✓ {flat.Substring(0, Math.Min(40, flat.Length))}");
                var lbl1 = (ui as Il2CppSystem.Object)?.TryCast<UILabel>();
                if (!lbl1.IsSafe()) return;
                _applying = true;
                try { lbl1.text = single; } finally { _applying = false; }
                return;
            }

            // Buffer for combined multi-effect keys
            long now = DateTime.UtcNow.Ticks;
            if (now - _lastTick > Window) { _texts.Clear(); _uis.Clear(); }
            _lastTick = now;
            var lbl = (ui as Il2CppSystem.Object)?.TryCast<UILabel>();
            if (!lbl.IsSafe()) return;
            if (_uis.Count > 0 && ReferenceEquals(_uis[_uis.Count - 1], lbl)) return;
            _texts.Add(flat); _uis.Add(lbl);
            if (_texts.Count < 2) return;

            for (int s = 0; s <= _texts.Count - 2; s++)
            {
                string comb = string.Concat(_texts.GetRange(s, _texts.Count - s));
                if (!SkillMergeIndex.TryTranslate(comb, out string trans)) continue;
                FLog.Debug($"[SkillMerge] ✓ Combined[{s}]");
                _applying = true;
                try
                {
                    _uis[s].text = trans;
                    for (int i = s + 1; i < _uis.Count; i++)
                        if (_uis[i].IsSafe()) _uis[i].text = string.Empty;
                }
                finally { _applying = false; }
                _texts.Clear(); _uis.Clear();
                return;
            }
        }
        catch (Exception ex) { FLog.Debug($"[SkillMerge] OnAfterXuat err: {ex.Message}"); }
    }
}
