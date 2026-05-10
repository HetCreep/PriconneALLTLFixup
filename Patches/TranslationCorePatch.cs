#nullable enable
using BepInEx;
using Elements;
using Fastenshtein;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core;
using XUnity.AutoTranslator.Plugin.Core.Endpoints;
using XUnity.AutoTranslator.Plugin.Core.Extensions;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class TranslationCorePatch
{
    #region 1. Internal Models & State
    private static readonly object _syncLock = new();
    internal static readonly Dictionary<string, string[]> NameDict = new(512);
    private static volatile bool _isTranslationSuppressed;

    private static readonly HashSet<string> PestStrings = new()
    {
        "WlFDRuOAgE5BR1Za", "WlFDRuOAgE5BR1ZD", "Q1FDRuOAgE5BR1Za", "Q1FDRuOAgE5BR1ZD"
    };

    private static readonly Regex ColorRegex = new(@"[\[\(]([0-9A-Fa-fsS]{6,10})[\]\)]", RegexOptions.Compiled);
    private static readonly Regex GradientRegex = new(@"[\[\(]([0-9A-Fa-f,sS\s]{13,20})[\]\)]", RegexOptions.Compiled);
    private static readonly Regex PlaceholderHallucinationRegex = new(@"[\[\(](\s*\d+\s*)[\]\)]", RegexOptions.Compiled);

    // Strips multi-segment NGUI bracket tags MT engines produce (e.g. [FF7C4E,D62,146]).
    // Valid NGUI color tags are exactly [RRGGBB] (6 hex) or [-]. Cross-class repair cannot
    // fix these because the original uses ColorRegex and the translation uses GradientRegex.
    private static readonly Regex MalformedNguiTagRegex = new(
        @"\[[0-9A-Fa-f]{3,6}(?:,[0-9A-Fa-f]{3,6})+\]", RegexOptions.Compiled);
    #endregion

    #region 2. Module A: Preprocessor & Repair (SetText)
    /// <summary>
    /// Harmony Prefix intercepting XUAT's internal SetText call.
    /// Actual XUAT signature: void SetText(object ui, string text, bool isTranslated,
    ///     string originalText, TextTranslationInfo info, string source)
    /// Harmony allows receiving only the named parameters we care about.
    /// </summary>
    [HarmonyPatch("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin, XUnity.AutoTranslator.Plugin.Core", "SetText")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixSetText(ref string text, string originalText)
    {
        if (_isTranslationSuppressed) return false;
        if (string.IsNullOrEmpty(originalText) || string.IsNullOrEmpty(text) || text == originalText) return true;

        text = text.Sanitize();

        if (!ConfigManager.Translation.TranslationRepair.Value) return true;

        try
        {
            var sb = new StringBuilder(text);

            RepairCorruptedTags(originalText, sb, ColorRegex, 3);
            RepairCorruptedTags(originalText, sb, GradientRegex, 5);

            text = sb.ToString();
            ApplyFinalPolish(ref text, originalText);
        }
        catch (Exception ex) { FLog.Debug($"[Repair] Failed: {ex.Message}"); }

        return true;
    }


    private static void RepairCorruptedTags(string original, StringBuilder currentSB, Regex regex, int threshold)
    {
        var originalMatches = regex.Matches(original);
        if (originalMatches.Count == 0) return;

        string currentText = currentSB.ToString();
        var currentMatches = regex.Matches(currentText);
        if (currentMatches.Count == 0) return;

        foreach (Match om in originalMatches)
        {
            string originVal = om.Value;
            var lev = new Levenshtein(originVal);

            foreach (Match cm in currentMatches)
            {
                string corruptedVal = cm.Value;
                if (corruptedVal == originVal) continue;

                if (Math.Abs(corruptedVal.Length - originVal.Length) > threshold) continue;

                if (originVal.Length == corruptedVal.Replace(" ", "").Length)
                {
                    currentSB.Replace(corruptedVal, originVal);
                    continue;
                }

                if (lev.DistanceFrom(corruptedVal) <= threshold)
                {
                    currentSB.Replace(corruptedVal, originVal);
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ApplyFinalPolish(ref string text, string original)
    {
        if (original.Contains('{'))
        {
            text = PlaceholderHallucinationRegex.Replace(text, m =>
            {
                string key = m.Groups[1].Value.Trim();
                return original.Contains("{" + key + "}") ? "{" + key + "}" : m.Value;
            });
        }

        text = text.Replace("[--]", "[-]").Replace(@"\ n", @"\n").Trim();

        // Strip multi-segment bracket color tags that MT engines produce from NGUI [RRGGBB] codes.
        // These appear as literal text (e.g. [FF7C4E,D62,146]) because they fail both
        // ColorRegex (6-10 chars) and the cross-class repair path.
        if (MalformedNguiTagRegex.IsMatch(text))
            text = MalformedNguiTagRegex.Replace(text, string.Empty);
    }
    #endregion

    #region 3. Module B: Dictionary Data Loader
    public static void InitializeNameDict()
    {
        string path = Path.Combine(Paths.BepInExRootPath, "Translation", ConfigManager.Translation.Code.Value, "Other", "unit_names.txt");
        if (!File.Exists(path))
        {
            FLog.Debug($"[Dict] 'unit_names.txt' not found at '{path}' — multi-language name aliases disabled.");
            return;
        }

        try
        {
            lock (_syncLock)
            {
                NameDict.Clear();
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int splitIdx = line.IndexOf('=');
                    if (splitIdx <= 0) continue;

                    string jpKey = line.Substring(0, splitIdx).Trim();
                    string valPart = line.Substring(splitIdx + 1);

                    var aliases = valPart.Split(';', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(v => v.Trim())
                                       .ToArray();

                    if (aliases.Length > 0) NameDict[jpKey] = aliases;
                }
            }
            FLog.Info($"[Dict] Universal Mapping: {NameDict.Count} keys loaded.");
        }
        catch (Exception ex) { FLog.Error($"[Dict] Load failed: {ex.Message}"); }
    }
    #endregion

    #region 4. Module C: Anti-Detection & Flow Control
    [HarmonyPatch(typeof(AutoTranslationPlugin), "ToggleTranslation")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixToggle()
    {
        if (!Util.IsXuatActive())
        {
            TextRegistryPatch.ClearCache();
            lock (_syncLock) NameDict.Clear();
        }
        else
        {
            InitializeNameDict();
        }
    }

    [HarmonyPatch(typeof(LoadIndexReceiveParam), "ParseLoadIndexReceiveParam")]
    [HarmonyPostfix]
    public static void PostfixPartyDetection(LoadIndexReceiveParam __instance)
    {
        if (!__instance.IsSafe() || __instance.UserMyParty == null || __instance.UserMyParty.Count == 0) return;

        foreach (var party in __instance.UserMyParty)
        {
            if (PestStrings.Contains(ApplyUniversalMask(party.PartyName)))
            {
                _isTranslationSuppressed = true;
                FLog.Warn("[Control] Anti-detection triggered. Translation paused for safety.");
                break;
            }
        }
    }

    private static string ApplyUniversalMask(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        try
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(input));
        }
        catch { return input; }
    }
    #endregion

    #region 5. Module D: Player Name Cache Repair
    // XUAT's TextCache internals are not publicly accessible; all access goes through reflection.
    // Two hooks are needed: one fires after login data arrives, the other after XUAT hot-reload.

    private static readonly BindingFlags _bf = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags _sf = BindingFlags.Static  | BindingFlags.Public | BindingFlags.NonPublic;

    [HarmonyPatch(typeof(LoadTask), "ParseLoadIndexImpl")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixPlayerNameOnLoad(LoadTask __instance)
        => ReplacePlayerNameInCache();

    [HarmonyPatch(typeof(AutoTranslationPlugin), "LoadTranslations")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixPlayerNameOnReload(AutoTranslationPlugin __instance, bool reload)
    {
        if (reload) ReplacePlayerNameInCache();
    }

    /// <summary>
    /// Walks XUAT's internal translation cache dictionaries via reflection and replaces
    /// <c>{playername}</c> tokens with the logged-in player's display name.
    /// </summary>
    internal static void ReplacePlayerNameInCache()
    {
        try
        {
            var userData = Singleton<UserData>.Instance;
            if (!userData.IsSafe() || userData.UserInfo == null) return;

            string playerName = userData.UserInfo.UserName?.ToString() ?? string.Empty;
            if (string.IsNullOrEmpty(playerName)) return;

            // Resolve AutoTranslationPlugin singleton via static property/field
            var pluginType = typeof(AutoTranslationPlugin);
            object? plugin = (pluginType.GetProperty("Current", _sf) ?? (object?)pluginType.GetField("Current", _sf)) switch
            {
                PropertyInfo p => p.GetValue(null),
                FieldInfo    f => f.GetValue(null),
                _              => null
            };
            if (plugin == null) return;

            // Resolve TextCache from plugin instance
            object? cache = (pluginType.GetProperty("TextCache", _bf) ?? (object?)pluginType.GetField("TextCache", _bf)) switch
            {
                PropertyInfo p => p.GetValue(plugin),
                FieldInfo    f => f.GetValue(plugin),
                _              => null
            };
            if (cache == null) return;

            ReplaceCacheDict(cache, "_translations",      playerName, isReverse: false);
            ReplaceCacheDict(cache, "_reverseTranslations", playerName, isReverse: true);

            FLog.Debug($"[Translation] Player name '{playerName}' applied to XUAT cache.");
        }
        catch (Exception ex) { FLog.Warn($"[Translation] ReplacePlayerNameInCache failed: {ex.Message}"); }
    }

    private static void ReplaceCacheDict(object cache, string fieldName, string playerName, bool isReverse)
    {
        const string token = "{playername}";
        var field = cache.GetType().GetField(fieldName, _bf);
        if (field == null) return;

        var dict = field.GetValue(cache) as System.Collections.IDictionary;
        if (dict == null) return;

        var keys = new object[dict.Keys.Count];
        dict.Keys.CopyTo(keys, 0);

        foreach (object rawKey in keys)
        {
            if (rawKey is not string key) continue;
            object? entry = dict[key];

            if (isReverse)
            {
                // Value is a plain string
                if (entry is string sval && sval.Contains(token))
                    dict[key] = sval.Replace(token, playerName);
            }
            else
            {
                // Value has a Translation property
                if (entry != null)
                {
                    var tp = entry.GetType().GetProperty("Translation", _bf);
                    if (tp?.GetValue(entry) is string tv && tv.Contains(token))
                        tp.SetValue(entry, tv.Replace(token, playerName));
                }
            }

            // Rename key if it contains the token
            if (key.Contains(token))
            {
                string newKey = key.Replace(token, playerName);
                var tmp = dict[key];
                dict.Remove(key);
                dict[newKey] = tmp;
            }
        }
    }
    #endregion

    #region 6. Module E: Skill Translation Queue
    /// <summary>
    /// Prefix on <c>AutoTranslationPlugin.TranslateOrQueueWebJobImmediate</c>:
    /// resolves empty or whitespace <paramref name="text"/> by reading the actual text
    /// from the UI component, then queues a single-line translation job (stripping embedded
    /// newlines) when no cached translation exists.
    /// This prevents skill descriptions with leading newlines from being silently skipped.
    /// </summary>
    [HarmonyPatch(typeof(AutoTranslationPlugin), "TranslateOrQueueWebJobImmediate")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillTranslation(
        AutoTranslationPlugin __instance,
        object                ui,
        ref string            text,
        int                   scope,
        object                info,
        bool                  allowStabilizationOnTextComponent,
        bool                  ignoreComponentState,
        bool                  allowStartTranslationImmediate,
        bool                  allowStartTranslationLater,
        object                tc,
        object                untranslatedTextContext,
        object                context)
    {
        // Guard: instance or text null (IL2CPP can pass null refs via Harmony)
        if (__instance == null || text == null) return;

        // Skip when text is already populated (non-empty, non-whitespace)
        if (!string.IsNullOrWhiteSpace(text)) return;

        // Check IsCurrentlySettingText via reflection (TextTranslationInfo is internal)
        if (info != null)
        {
            try
            {
                var prop = info.GetType().GetProperty("IsCurrentlySettingText", _bf);
                if (prop?.GetValue(info) is true) return;
            }
            catch { /* reflection on IL2CPP types can throw — skip safely */ }
        }

        // text is empty/whitespace here — safe to call Contains on original ref
        // (re-read to guard against concurrent modification)
        string safeText = text ?? string.Empty;

        // Skip content with special markers or already-translated non-Japanese text
        if (safeText.Contains('※') || safeText.Contains('[') || IsNonJapaneseScript(safeText)) return;

        // Queue a flat (no-newline) version — avoids newline-related cache misses in XUAT
        string flatText = safeText.Replace("\n", string.Empty);
        if (flatText == safeText) return; // no change → nothing to re-queue

        // Re-invoke with original parameters except text (now flat) and no-start flags
        try
        {
            var method = typeof(AutoTranslationPlugin)
                .GetMethod("TranslateOrQueueWebJobImmediate", _bf);
            method?.Invoke(__instance, new object?[] {
                ui, flatText, scope, info,
                allowStabilizationOnTextComponent, ignoreComponentState,
                false, false, tc, untranslatedTextContext, context
            });
        }
        catch (Exception ex) { FLog.Debug($"[SkillTL] Re-queue failed: {ex.Message}"); }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="text"/> consists entirely of characters
    /// outside the Japanese/CJK Unicode blocks — indicating the text is likely already
    /// translated and does not need to be queued again.
    /// Replaces the legacy <c>IsEnglish()</c> ASCII-only check to support every locale.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNonJapaneseScript(string text)
    {
        foreach (char c in text)
        {
            // Hiragana, Katakana, CJK Unified Ideographs → still Japanese
            if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF'))
                return false;
        }
        return true;
    }
    #endregion
}