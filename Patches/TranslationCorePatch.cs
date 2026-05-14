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

    // Matches multi-segment NGUI bracket tags such as game-original gradients
    // (e.g. [f374ff,95289f]) and MT-mangled fragments (e.g. [FF7C4E,D62,146]).
    // Valid single-color NGUI tags ([RRGGBB], [F00], [-]) do not contain commas
    // and are intentionally not matched.
    // First segment kept at {3,6} to require a real-looking hex anchor; subsequent
    // segments allow {1,6} because MT engines often truncate or pad them.
    private static readonly Regex MalformedNguiTagRegex = new(
        @"\[[0-9A-Fa-f]{3,6}(?:,[0-9A-Fa-f]{1,6})+\]", RegexOptions.Compiled);

    /// <summary>
    /// Reduces a matched multi-segment bracket tag to a single-color tag using the
    /// first hex segment, which NGUI's parser handles correctly. Preserves the
    /// game's intended color (approximate, since gradient end-color is dropped).
    /// If the first segment isn't a valid 3- or 6-char hex (rare MT mangling like
    /// <c>[B74F,9D,269]</c> where the first is 4 chars), the entire tag is dropped.
    /// </summary>
    private static readonly MatchEvaluator MalformedTagEvaluator = m =>
    {
        string inner = m.Value.Substring(1, m.Value.Length - 2);
        int comma = inner.IndexOf(',');
        string firstSeg = comma < 0 ? inner : inner.Substring(0, comma);
        return (firstSeg.Length == 3 || firstSeg.Length == 6) ? "[" + firstSeg + "]" : string.Empty;
    };
    #endregion

    #region 2. Module A: Preprocessor & Repair (SetText)
    /// <summary>
    /// Harmony Prefix intercepting XUAT's internal SetText call.
    /// Actual XUAT signature: void SetText(object ui, string text, bool isTranslated,
    ///     string originalText, TextTranslationInfo info, string source)
    /// Harmony allows receiving only the named parameters we care about.
    /// </summary>
    [HarmonyPatch(typeof(AutoTranslationPlugin), "SetText")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixSetText(ref string text, string originalText)
    {
        if (_isTranslationSuppressed) return false;
        if (string.IsNullOrEmpty(originalText) || string.IsNullOrEmpty(text) || text == originalText) return true;

        text = text.Sanitize();

        // Collapse multi-segment bracket tags to a single-color tag (preserves the game's
        // intended hue when present, e.g. [f374ff,95289f]→[f374ff]) or drops entirely when
        // the first segment isn't a valid hex length. Runs unconditionally — game-original
        // gradients render fine in the JP font but show as literal brackets after XUAT swaps
        // in our font, so they must be normalised either way.
        // Not a repair feature — must run regardless of TranslationRepair setting.
        if (MalformedNguiTagRegex.IsMatch(text))
            text = MalformedNguiTagRegex.Replace(text, MalformedTagEvaluator);

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

        // Collapse multi-segment bracket tags to a single-color tag. See PrefixSetText.
        if (MalformedNguiTagRegex.IsMatch(text))
            text = MalformedNguiTagRegex.Replace(text, MalformedTagEvaluator);
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
        StripMalformedTagsFromCache();
    }

    /// <summary>
    /// Walks XUAT's translation cache and strips MT-mangled multi-segment NGUI color
    /// tags (e.g. <c>[FF7C4E,D62,146]</c>) from every cached translation value. This
    /// catches entries where the malformed tag was baked into the cache before
    /// <c>PrefixSetText</c> existed, or paths that bypass <c>SetText</c> entirely
    /// (cache hits served via fast path). Runs on every cache load — including the
    /// initial one — so the in-memory cache is always clean.
    /// </summary>
    internal static void StripMalformedTagsFromCache()
    {
        try
        {
            var pluginType = typeof(AutoTranslationPlugin);
            object? plugin = (pluginType.GetProperty("Current", _sf) ?? (object?)pluginType.GetField("Current", _sf)) switch
            {
                PropertyInfo p => p.GetValue(null),
                FieldInfo    f => f.GetValue(null),
                _              => null
            };
            if (plugin == null) return;

            object? cache = (pluginType.GetProperty("TextCache", _bf) ?? (object?)pluginType.GetField("TextCache", _bf)) switch
            {
                PropertyInfo p => p.GetValue(plugin),
                FieldInfo    f => f.GetValue(plugin),
                _              => null
            };
            if (cache == null) return;

            int stripped = 0;
            stripped += StripCacheDict(cache, "_translations",        isReverse: false);
            stripped += StripCacheDict(cache, "_reverseTranslations", isReverse: true);

            if (stripped > 0)
                FLog.Info($"[Repair] Stripped malformed NGUI tags from {stripped} cached translations.");
        }
        catch (Exception ex) { FLog.Warn($"[Repair] StripMalformedTagsFromCache failed: {ex.Message}"); }
    }

    private static int StripCacheDict(object cache, string fieldName, bool isReverse)
    {
        var field = cache.GetType().GetField(fieldName, _bf);
        if (field == null) return 0;

        var dict = field.GetValue(cache) as System.Collections.IDictionary;
        if (dict == null) return 0;

        int count = 0;
        var keys = new object[dict.Keys.Count];
        dict.Keys.CopyTo(keys, 0);

        foreach (object rawKey in keys)
        {
            if (rawKey is not string key) continue;
            object? entry = dict[key];

            if (isReverse)
            {
                if (entry is string sval && MalformedNguiTagRegex.IsMatch(sval))
                {
                    dict[key] = MalformedNguiTagRegex.Replace(sval, MalformedTagEvaluator);
                    count++;
                }
            }
            else
            {
                if (entry != null)
                {
                    var tp = entry.GetType().GetProperty("Translation", _bf);
                    if (tp?.GetValue(entry) is string tv && MalformedNguiTagRegex.IsMatch(tv))
                    {
                        tp.SetValue(entry, MalformedNguiTagRegex.Replace(tv, MalformedTagEvaluator));
                        count++;
                    }
                }
            }
        }
        return count;
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

    #region 6. Module E: Script Detection Helpers

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

    #region 5. Module D: Skill Effect Translation Fix (ported from PriconneSkillTLFixup by Olegase)
    private static MethodInfo _translateMethod;
    private static MethodInfo TranslateMethod => _translateMethod ??=
        typeof(AutoTranslationPlugin)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "TranslateOrQueueWebJobImmediate"
                              && m.GetParameters().Length >= 9);

    private static readonly List<string> _texts = new();
    private static readonly List<UILabel> _uis = new();
    private static long _lastTick;
    private static long Window => 200 * TimeSpan.TicksPerMillisecond;

    // NOTE: Previously hooked AutoTranslationPlugin.TranslateOrQueueWebJobImmediate
    // (Prefix). MonoMod failed to JIT-compile the patched method (Fatal CLR error
    // 0x80131506) because that target combines INTERNAL parameter types
    // (TextTranslationInfo, IReadOnlyTextTranslationCache, UntranslatedTextInfo,
    // ParserTranslationContext) with optional default values - a pattern that
    // breaks MonoMod's IL injection on .NET 6+/Unity 6 IL2CPP.
    // SkillMergeIndex still builds at startup; the active translation hook will
    // need to target a different, simpler method (e.g. UILabel.set_text Postfix).
    #endregion

}