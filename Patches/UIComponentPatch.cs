using Elements;
using FLATOUT.Main;
using HarmonyLib;
using Il2CppInterop.Runtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class UIComponentPatch
{
    #region 1. Internal State
    private static bool _initialized;
    private static bool _fontSystemReady;
    private static bool _fontRulesReady;
    private static bool _layoutRulesReady;
    private static volatile bool _resizeInProgress;
    private static readonly object _uiLock = new();

    private static Font _baseFont = null!;
    private static readonly Dictionary<string, Font> _fontLibrary = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<(Regex regex, Font font)> _fontRules = new();
    private static readonly List<(Regex regex, float width, UILabel.Overflow method)> _layoutRules = new();

    private static readonly Dictionary<string, Font> _matchedFontCache = new(2048);
    private static readonly Dictionary<string, (float width, UILabel.Overflow method)?> _matchedLayoutCache = new(2048);

    // --- Diagnostic: logs original game values before our rule is applied (DeveloperLogs only, NOT pushed) ---
    private static readonly HashSet<string> _diagLoggedPaths = new(2048);
    #endregion

    #region 2. System Loader (Traceable & Robust)
    private static void Initialize()
    {
        if (_initialized) return;
        lock (_uiLock)
        {
            if (_initialized) return;
            try
            {
                string xuatLang = Util.GetXuatLanguage();
                FLog.Info($"[Visual] XUAT Language: '{xuatLang}' | Our LanguageCode override: '{ConfigManager.Translation.Code.Value}'");

                // 2-tier: 1) User config  2) XUAT auto-detect — no directory scan fallback
                string finalLang = ConfigManager.Translation.Code.Value;
                if (string.IsNullOrWhiteSpace(finalLang)) finalLang = xuatLang;

                if (string.IsNullOrWhiteSpace(finalLang))
                {
                    FLog.Warn("[Visual] Language not configured and XUAT returned empty. " +
                              "Font/Layout patches are DISABLED. " +
                              "Set 'LanguageCode' in config or ensure XUAT is active.");
                    _initialized = true;
                    return;
                }

                FLog.Info($"[Visual] Master Framework initiating patch for: [{finalLang.ToUpper()}]");

                string root     = Path.Combine(BepInEx.Paths.BepInExRootPath, "Translation", finalLang);
                string fontPath = Path.Combine(root, "Font", "font_base.unity3d");

                if (File.Exists(fontPath))
                {
                    _baseFont = LoadFontBundle(fontPath)!;
                    _fontSystemReady = (_baseFont != null);
                }
                else
                {
                    FLog.Warn($"[Visual] 'font_base.unity3d' not found in '{root}\\Font'. " +
                               "Font patch DISABLED. Download from addon/Font/ on GitHub.");
                    _fontSystemReady = false;
                }

                if (_fontSystemReady)
                {
                    LoadSupplementaryFonts(Path.Combine(root, "Font"));

                    // charset.txt is consumed by XUAT — we only log its presence/absence
                    string charsetPath = Path.Combine(root, "Font", "charset.txt");
                    if (!File.Exists(charsetPath))
                        FLog.Debug("[Visual] 'charset.txt' not found — XUAT will render all Unicode ranges (no character pre-filter). " +
                                   "Download a per-language file from addon/Font/ on GitHub.");

                    string fontRulePath = Path.Combine(root, "Other", "_01.font.txt");
                    if (File.Exists(fontRulePath))
                    {
                        ParseFontConfig(fontRulePath);
                        _fontRulesReady = _fontRules.Count > 0;
                    }
                    else
                    {
                        FLog.Debug("[Visual] '_01.font.txt' not found — 'font_base' applied globally (no per-element font rules).");
                        _fontRulesReady = false;
                    }
                }

                string layoutRulePath = Path.Combine(root, "Other", "_02.resize.txt");
                if (File.Exists(layoutRulePath))
                {
                    ParseLayoutConfig(layoutRulePath);
                    _layoutRulesReady = _layoutRules.Count > 0;
                }
                else
                {
                    FLog.Debug("[Visual] '_02.resize.txt' not found — default game container widths used (layout patch disabled).");
                    _layoutRulesReady = false;
                }

                _initialized = true;
            }
            catch (Exception ex) { FLog.Error("[Visual] Engine init failed", ex); }
        }
    }

    private static void ParseFontConfig(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            string fontKey = parts[1].Trim();
            Font target = null;
            if (_fontLibrary.TryGetValue(fontKey, out Font libF)) target = libF;
            else if (fontKey == "font_base") target = _baseFont;

            if (target != null) _fontRules.Add((ConvertToRegex(parts[0].Trim()), target));
        }
    }

    private static void ParseLayoutConfig(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;

            var vals = parts[1].Split('|');
            if (vals.Length == 2 && float.TryParse(vals[0].Trim(), out float w))
            {
                var method = vals[1].Contains("ResizeHeight") ? UILabel.Overflow.ResizeHeight : UILabel.Overflow.ShrinkContent;
                _layoutRules.Add((ConvertToRegex(parts[0].Trim()), w, method));
            }
        }
    }

    private static Regex ConvertToRegex(string pattern)
    {
        string escaped = Regex.Escape(pattern).Replace(@"\*", ".*");
        return new Regex("^" + escaped + "$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private static Font LoadFontBundle(string path)
    {
        try
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null) return null;
            Font font = bundle.LoadAllAssets(Il2CppType.Of<Font>()).FirstOrDefault()?.Cast<Font>();
            if (font != null) font.Persistent();
            bundle.Unload(false);
            return font;
        }
        catch { return null; }
    }

    private static void LoadSupplementaryFonts(string fontDir)
    {
        if (!Directory.Exists(fontDir)) return;
        foreach (var file in Directory.GetFiles(fontDir, "*.unity3d"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name == "font_base") continue;
            Font f = LoadFontBundle(file);
            if (f != null) _fontLibrary[name] = f;
        }
    }
    #endregion

    #region 3. Module A: Universal UI Handler (NGUI)
    [HarmonyPatch(typeof(CustomUILabel), nameof(CustomUILabel.Awake))]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixNGUIAwake(CustomUILabel __instance)
    {
        if (!_initialized) Initialize();
        if (__instance.IsSafe()) ApplyNGUIStyle(__instance);
    }

    [HarmonyPatch(typeof(UILabel), nameof(UILabel.ProcessText), new[] { typeof(bool), typeof(bool) })]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixNGUIProcess(UILabel __instance)
    {
        if (__instance is CustomUILabel custom && custom.IsSafe())
            ApplyNGUIStyle(custom);
    }

    private static void ApplyNGUIStyle(CustomUILabel label)
    {
        if (!label.IsSafe() || !label.transform.IsSafe()) return;
        string path = label.transform.GetHierarchyPath();

        if (ConfigManager.Visual.UIFont.Value && _fontSystemReady)
        {
            Font targetFont = _fontRulesReady ? GetMatchedFont(path) : null;
            targetFont ??= _baseFont;
            if (label.trueTypeFont != targetFont) label.trueTypeFont = targetFont;
        }

        if (ConfigManager.Visual.UIUniversal.Value && _layoutRulesReady)
        {
            var layout = GetMatchedLayout(path);
            if (layout.HasValue)
            {
                // --- DIAGNOSTIC (DeveloperLogs=true only): log original game value once per unique path ---
                if (FLog.IsDeveloperContext && _diagLoggedPaths.Add(path))
                    FLog.Debug($"[ResizeDiag] {path}" +
                               $" | game={label.lineWidth}px {label.overflowMethod}" +
                               $" → rule={layout.Value.width}px {layout.Value.method}");

                label.lineWidth = (int)layout.Value.width;
                label.overflowMethod = layout.Value.method;

                if (layout.Value.method == UILabel.Overflow.ResizeHeight)
                {
                    label.multiLine = true;
                    label.maxLineCount = 0;
                }
            }
        }
    }
    #endregion

    #region 4. Module B: Header & Integrity (Always On)
    [HarmonyPatch(typeof(PartsHeaderBackButton), "SetTitleText")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixHeaderTitle(PartsHeaderBackButton __instance, string _setTitleText)
    {
        if (string.IsNullOrEmpty(_setTitleText) || !__instance.IsSafe()) return true;

        if (__instance.titleLabel != null) __instance.titleLabel.text = "";
        string clean = _setTitleText.Sanitize().Replace("\n", " ");
        if (__instance.titleLabel2nd != null) __instance.titleLabel2nd.text = clean;

        // Use NGUI's own layout engine for the initial width — handles all scripts correctly.
        // Fall back to the original English-era estimate only when ProcessText hasn't fired yet.
        __instance.titleLabel2nd.ProcessText();
        float initialWidth = __instance.titleLabel2nd.mCalculatedSize.x > 0f
            ? __instance.titleLabel2nd.mCalculatedSize.x + 40f
            : (clean.Length * __instance.titleLabel2nd.fontSize * 0.75f) + 60f;
        __instance.underLine.width = (int)initialWidth;
        __instance.underLine.gameObject.SetActive(true);

        CoroutineStarter.Run(AdjustTitleOnFly(__instance, clean));
        return false;
    }

    [HarmonyPatch(typeof(PartsHeaderBackButton), "SetSubTitleText")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixHeaderSubTitle(PartsHeaderBackButton __instance, string _setSubTitleText)
    {
        if (!__instance.IsSafe() || __instance.subTitleLabel == null
         || string.IsNullOrEmpty(_setSubTitleText)) return;

        // Immediate resize for already-translated (non-Japanese) text
        if (!ContainsJapanese(__instance.subTitleLabel.text))
        {
            __instance.subTitleLabel.ProcessText();
            int backPad = (__instance.backButton == null) ? 50 : 0;
            float w = __instance.subTitleLabel.mCalculatedSize.x + 20f + backPad;
            __instance.underLine.width = (int)Math.Round(w);

            HeaderController header = SingletonMonoBehaviour<HeaderController>.Instance;
            if (header.IsSafe())
                header.campaignIcons.SetIconPosition(header.viewManager.CurrentViewId, w);
        }

        // Deferred resize to catch XUAT translation finishing after this frame
        CoroutineStarter.Run(AdjustSubTitleOnFly(__instance, _setSubTitleText));
    }

    private static IEnumerator AdjustTitleOnFly(PartsHeaderBackButton instance, string text)
    {
        yield return new Util.WaitUntilOrTimeoutInstruction(3f, () => instance.titleLabel2nd.text != text);
        if (instance.IsSafe())
        {
            instance.titleLabel2nd.ProcessText();
            float finalWidth = instance.titleLabel2nd.mCalculatedSize.x + 40f;
            instance.underLine.width = (int)Math.Round(finalWidth);

            HeaderController header = SingletonMonoBehaviour<HeaderController>.Instance;
            if (header.IsSafe())
                header.campaignIcons.SetIconPosition(header.viewManager.CurrentViewId, finalWidth);
        }
    }

    private static IEnumerator AdjustSubTitleOnFly(PartsHeaderBackButton instance, string originalText)
    {
        int backPad = (instance.backButton == null) ? 50 : 0;
        yield return new Util.WaitUntilOrTimeoutInstruction(3f,
            () => !instance.IsSafe() || instance.subTitleLabel.text != originalText);
        if (instance.IsSafe())
        {
            instance.subTitleLabel.ProcessText();
            float finalWidth = instance.subTitleLabel.mCalculatedSize.x + 20f + backPad;
            instance.underLine.width = (int)Math.Round(finalWidth);

            HeaderController header = SingletonMonoBehaviour<HeaderController>.Instance;
            if (header.IsSafe())
                header.campaignIcons.SetIconPosition(header.viewManager.CurrentViewId, finalWidth);
        }
    }
    #endregion

    #region 5. Module C: Legacy & Flash Support
    [HarmonyPatch(typeof(TextMesh), nameof(TextMesh.text), MethodType.Setter)]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixTextMesh(TextMesh __instance)
    {
        if (_resizeInProgress || !__instance.IsSafe() || !_initialized) return;

        try
        {
            _resizeInProgress = true;

            if (ConfigManager.Visual.UIFont.Value && _fontSystemReady)
                __instance.font = _baseFont;

            if (ConfigManager.Visual.UIUniversal.Value)
            {
                if (__instance.fontSize == 24)
                    __instance.fontSize = GetAdaptiveSize(24);

                TextSize sizeTool = new TextSize(__instance);
                if (sizeTool.Width > 1.7f) sizeTool.FitToWidth(1.7f);
            }
        }
        finally { _resizeInProgress = false; }
    }

    [HarmonyPatch(typeof(FlTextParameter), "_ApplyData")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixFlashSize(FlTextParameter __instance)
    {
        if (ConfigManager.Visual.UIUniversal.Value && __instance.IsSafe() && __instance._fontSize == 24)
            __instance._fontSize = GetAdaptiveSize(24);
    }

    private static int GetAdaptiveSize(int originalSize)
    {
        string lang = ConfigManager.Translation.Code.Value;
        if (string.IsNullOrWhiteSpace(lang)) lang = Util.GetXuatLanguage();

        // Derive scale from the Unicode script of the active translation language.
        // This avoids maintaining a language-code whitelist and supports every locale.
        return (int)(originalSize * GetScriptScaleRatio(lang));
    }

    /// <summary>
    /// Returns a font-size scale ratio (0–1) based on the script family of the active
    /// translation language, derived from its ISO 639-1 code.
    /// <list type="bullet">
    ///   <item>0.80 — Wide diacritic scripts: Thai, Khmer, Lao, Myanmar, Tibetan, Sinhala, Burmese</item>
    ///   <item>0.85 — Bidirectional / complex scripts: Arabic, Hebrew, Syriac, Thaana</item>
    ///   <item>0.88 — Latin-extended / space-heavy: English, French, German, Spanish, etc.</item>
    ///   <item>0.93 — CJK / Indic / compact scripts: Chinese, Japanese, Korean, Hindi, Bengali</item>
    ///   <item>0.96 — Default fallback for all other scripts</item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float GetScriptScaleRatio(string isoCode) => isoCode switch
    {
        // Wide diacritic scripts (vertical stacking glyphs take significant vertical space)
        "th" or "km" or "lo" or "my" or "bo" or "si" => 0.80f,

        // Bidirectional / complex right-to-left scripts
        "ar" or "he" or "fa" or "ur" or "ps" or "syr" or "dv" => 0.85f,

        // Latin and Cyrillic with long words / wide character metrics
        "en" or "fr" or "de" or "es" or "pt" or "it" or "nl" or "pl"
        or "cs" or "ru" or "uk" or "bg" or "ro" or "el" or "hu" or "sv"
        or "no" or "da" or "fi" or "sk" or "hr" or "sr" or "sl" or "lt"
        or "lv" or "et" or "ca" or "eu" or "gl" or "tr" or "az" or "kk"
        or "uz" or "tk" or "ky" or "mn" or "id" or "ms" or "tl" or "sw"
        or "sq" or "mk" or "bs" or "is" or "ga" or "cy" or "mt" or "lb" => 0.88f,

        // Compact CJK and Indic scripts (dense character packing)
        "zh" or "ja" or "ko" or "hi" or "bn" or "mr" or "gu" or "ta"
        or "te" or "kn" or "ml" or "pa" or "ne" or "si" or "as" or "or"
        or "vi" or "ka" or "am" or "ti" => 0.93f,

        // Default: safe conservative scale for any unrecognised locale
        _ => 0.96f
    };
    #endregion

    #region 6. Helper Methods & Cache
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsJapanese(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (char c in text)
            if ((c >= '\u3040' && c <= '\u30FF') || // Hiragana + Katakana
                (c >= '\u4E00' && c <= '\u9FFF'))   // CJK Unified Ideographs (Kanji)
                return true;
        return false;
    }
    private static Font GetMatchedFont(string path)
    {
        if (_matchedFontCache.TryGetValue(path, out Font f)) return f;
        Font match = _fontRules.FirstOrDefault(r => r.regex.IsMatch(path)).font;
        return _matchedFontCache[path] = match;
    }

    private static (float width, UILabel.Overflow method)? GetMatchedLayout(string path)
    {
        if (_matchedLayoutCache.TryGetValue(path, out var l)) return l;
        var rule = _layoutRules.FirstOrDefault(r => r.regex.IsMatch(path));
        var res = rule.regex != null ? ((float width, UILabel.Overflow method)?)(rule.width, rule.method) : null;
        return _matchedLayoutCache[path] = res;
    }

    [HarmonyPatch(typeof(SceneManager), nameof(SceneManager.Internal_SceneLoaded))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void ClearCaches()
    {
        lock (_uiLock)
        {
            _matchedFontCache.Clear();
            _matchedLayoutCache.Clear();
            _diagLoggedPaths.Clear(); // reset so entering a new scene re-logs fresh values

            if (FLog.IsDeveloperContext)
                FLog.Debug("[Visual] UI Caches cleared (Scene change or Manual trigger).");
        }
    }
    #endregion

    #region 7. Private Helper Classes
    private class TextSize
    {
        private readonly TextMesh _mesh;
        private readonly MeshRenderer _renderer;
        public TextSize(TextMesh tm) { _mesh = tm; _renderer = tm.GetComponent<MeshRenderer>(); }
        public float Width => (_renderer != null) ? _renderer.bounds.size.x : 0f;
        public void FitToWidth(float maxWidth)
        {
            if (_renderer == null || Width <= 0 || Width <= maxWidth) return;
            float ratio = maxWidth / Width;
            Vector3 scale = _mesh.transform.localScale;
            scale.x *= ratio;
            _mesh.transform.localScale = scale;
        }
    }
    #endregion

    // =========================================================================
    // MODULE: UILabel Unclamp (UnclampPatch)
    // =========================================================================

    #region UILabel Unclamp
    // Pre-compiled regexes shared across all label evaluations.
    private static readonly Regex _whitespaceNewline = new(@"[\n ]*", RegexOptions.Compiled);
    private static readonly Regex _newlineOnly       = new(@"[\n]",   RegexOptions.Compiled);

    // Label names that must never be unclamped (fixed-size containers or icon badges).
    private static readonly HashSet<string> _unclampExcludes = new()
    {
        // Content panels — already have scroll/fixed containers
        "DetailLabel", "Label_item_name",
        // Lv requirement badges on item icons (e.g. Lv290 badge)
        "Label_lv", "LvLabel", "Label_level", "lv_label", "levelLabel",
        // Shop footer info labels (Reset Daily, stock count)
        "Label_update", "Label_info", "Label_info_text", "Label_reset",
        "Label_stock", "Label_count", "Label_appear",
    };

    /// <summary>
    /// Postfix on <c>UILabel.ProcessText(bool, bool)</c>:
    /// promotes a clamped label to <c>ResizeFreely</c> when the translated text has been
    /// truncated (processed text differs from the raw text after stripping whitespace) and
    /// the label meets all of the following conditions:
    /// <list type="bullet">
    ///   <item>label is valid, marked as changed, and non-empty</item>
    ///   <item>overflow mode is currently <c>ClampContent</c></item>
    ///   <item>max line count ≤ 3</item>
    ///   <item>height &lt; 50 px and lineWidth &lt; 300 px (small fixed-size label)</item>
    ///   <item>raw text contains no explicit newline characters</item>
    ///   <item>label name is not in the exclusion list</item>
    /// </list>
    /// Left-aligned labels have their pivot reset to <c>Left</c> so the text grows
    /// rightward rather than centring on the original anchor.
    /// </summary>
    [HarmonyPatch(typeof(UILabel), "ProcessText", new[] { typeof(bool), typeof(bool) })]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixUnclamp(UILabel __instance)
    {
        if (!__instance.IsSafe()) return;
        if (!__instance.isValid || !__instance.mChanged) return;

        string processed = _whitespaceNewline.Replace(__instance.mProcessedText ?? string.Empty, string.Empty);
        if (string.IsNullOrEmpty(processed)) return;

        string raw = _whitespaceNewline.Replace(__instance.mText ?? string.Empty, string.Empty);

        if (__instance.overflowMethod != UILabel.Overflow.ClampContent) return;
        if (raw == processed) return;
        if (__instance.maxLineCount > 3) return;
        if (_unclampExcludes.Contains(__instance.name)) return;
        if (__instance.height >= 50 || __instance.lineWidth >= 300) return;
        // Don't unclamp very short single-line labels — these are icon badges (Lv, count, etc.)
        // that live in fixed-size overlay containers and must not grow beyond their allocated space.
        if (__instance.height < 25 && __instance.maxLineCount <= 1) return;
        if (_newlineOnly.IsMatch(__instance.text ?? string.Empty)) return;

        // Don't unclamp Center/Right-aligned labels — they grow leftward when expanded,
        // displacing UI elements like the shop reset-daily footer label far from their
        // intended position. Only Left-aligned labels safely grow rightward.
        if (__instance.alignment != NGUIText.Alignment.Left) return;
        __instance.pivot = UIWidget.Pivot.Left;

        __instance.overflowMethod = UILabel.Overflow.ResizeFreely;
        __instance.ProcessText();
    }
    #endregion

    // =========================================================================
    // MODULE 8: Font Base Load Guard (FixFontLoadPatch)
    // =========================================================================

    #region 8. FixFontLoadPatch — Crash Prevention
    /// <summary>
    /// Postfix on <c>CustomUILabel.Awake</c>:
    /// if the label's <c>trueTypeFont</c> is <c>null</c> after <c>Awake</c> and our
    /// <c>_baseFont</c> is ready, assigns it as a safe fallback.
    /// This prevents the <c>NullReferenceException</c> that occurs in <c>ProcessText</c>
    /// when a font bundle's internal name does not match the expected name in the
    /// game's font reference table. Equivalent to legacy <c>FixFontLoadPatch</c>.
    /// </summary>
    [HarmonyPatch(typeof(CustomUILabel), nameof(CustomUILabel.Awake))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixFixFontLoad(CustomUILabel __instance)
    {
        if (!ConfigManager.Visual.UIFont.Value || !_fontSystemReady) return;
        if (!__instance.IsSafe()) return;

        // Guard: if the label already has a valid font, do not interfere
        if (__instance.trueTypeFont != null) return;

        // Fallback: assign base font to prevent NRE in ProcessText
        __instance.trueTypeFont = _baseFont;
        FLog.Debug($"[Visual] FixFontLoad: fallback font assigned to '{__instance.gameObject.name}' (trueTypeFont was null).");
    }
    #endregion
}