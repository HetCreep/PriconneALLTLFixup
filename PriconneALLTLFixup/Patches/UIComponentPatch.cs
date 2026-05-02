using Elements;
using FLATOUT.Main;
using HarmonyLib;
using Il2CppInterop.Runtime;
using System.Collections;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class UIComponentPatch
{
    #region 1. Internal State & Failsafe Flags
    private static bool _initialized;
    private static bool _fontSystemReady;
    private static bool _resizeInProgress;
    private static readonly object _uiLock = new object();

    private static Font _baseFont;
    private static readonly Dictionary<string, Font> _fontLibrary = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<KeyValuePair<Regex, Font>> _fontRules = new List<KeyValuePair<Regex, Font>>();
    private static readonly List<(Regex regex, float width, UILabel.Overflow method)> _layoutRules = new List<(Regex regex, float width, UILabel.Overflow method)>();

    private static readonly Dictionary<string, Font> _matchedFontCache = new Dictionary<string, Font>(1024);
    private static readonly Dictionary<string, (float width, UILabel.Overflow method)?> _matchedLayoutCache = new Dictionary<string, (float width, UILabel.Overflow method)?>(1024);
    #endregion

    #region 2. System Loader & Integrity Check
    private static void Initialize()
    {
        if (_initialized) return;
        lock (_uiLock)
        {
            if (_initialized) return;
            try
            {
                string lang = Util.GetXuatLanguage();
                string root = Path.Combine(BepInEx.Paths.BepInExRootPath, "Translation", lang);
                string fontDir = Path.Combine(root, "Font");
                string otherDir = Path.Combine(root, "Other");

                _baseFont = LoadFontBundle(Path.Combine(fontDir, "font_base.unity3d"));
                _fontSystemReady = (_baseFont != null);

                if (!_fontSystemReady)
                    Log.Warn("[Visual] font_base.unity3d missing. Font redirection will be disabled.");

                if (_fontSystemReady && Directory.Exists(fontDir))
                {
                    foreach (var file in Directory.GetFiles(fontDir, "*.unity3d"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (name == "font_base") continue;
                        Font f = LoadFontBundle(file);
                        if (f != null) _fontLibrary[name] = f;
                    }
                }

                ParseFontConfig(Path.Combine(otherDir, "_01.font.txt"));
                ParseLayoutConfig(Path.Combine(otherDir, "_02.resize.txt"));

                _initialized = true;
                Log.Info($"[Visual] UI Engine Ready for '{lang}'. Rules: {_layoutRules.Count} resizer, {_fontRules.Count} font.");
            }
            catch (Exception ex) { Log.Error("[Visual] Engine init failed", ex); }
        }
    }

    private static void ParseFontConfig(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2) continue;

            string pattern = parts[0].Trim();
            string fontKey = parts[1].Trim();

            Font targetFont = null;
            if (_fontLibrary.TryGetValue(fontKey, out Font libFont)) targetFont = libFont;
            else if (fontKey == "font_base") targetFont = _baseFont;

            if (targetFont != null)
                _fontRules.Add(new KeyValuePair<Regex, Font>(ConvertToRegex(pattern), targetFont));
        }
    }

    private static void ParseLayoutConfig(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
            var parts = line.Split(new[] { '=' }, 2);
            if (parts.Length != 2) continue;

            var vals = parts[1].Split('|');
            if (vals.Length != 2 || !float.TryParse(vals[0].Trim(), out float w)) continue;

            UILabel.Overflow method = vals[1].Trim().Contains("ResizeHeight", StringComparison.OrdinalIgnoreCase)
                ? UILabel.Overflow.ResizeHeight : UILabel.Overflow.ShrinkContent;

            _layoutRules.Add((ConvertToRegex(parts[0].Trim()), w, method));
        }
    }

    private static Regex ConvertToRegex(string pattern)
    {
        string escaped = Regex.Escape(pattern).Replace(@"\*", ".*");
        return new Regex("^" + escaped + "$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    private static Font LoadFontBundle(string path)
    {
        if (!File.Exists(path)) return null;
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

    private static int GetAdaptiveSize(int originalSize)
    {
        if (originalSize != 24) return originalSize;

        string lang = Util.GetXuatLanguage();

        return lang switch
        {
            "th" or "vi" or "hi" => 19,

            "en" or "es" or "fr" or "de" or "it" => 20,

            _ => 21
        };
    }
    #endregion

    #region 3. Module A: Universal UI Handler
    [HarmonyPatch(typeof(CustomUILabel), nameof(CustomUILabel.Awake))]
    [HarmonyPrefix]
    public static void PrefixNGUIAwake(CustomUILabel __instance)
    {
        if (!ConfigManager.Visual.UIFont.Value && !ConfigManager.Visual.UIUniversal.Value) return;
        if (!_initialized) Initialize();
        if (__instance.IsSafe()) ApplyNGUIStyle(__instance);
    }

    [HarmonyPatch(typeof(UILabel), nameof(UILabel.ProcessText))]
    [HarmonyPostfix]
    public static void PostfixNGUIProcess(UILabel __instance)
    {
        if (__instance is CustomUILabel custom && custom.IsSafe())
            ApplyNGUIStyle(custom);
    }

    private static void ApplyNGUIStyle(CustomUILabel label)
    {
        string path = label.transform.GetHierarchyPath();

        if (ConfigManager.Visual.UIFont.Value && _fontSystemReady)
        {
            Font targetFont = GetMatchedFont(path) ?? _baseFont;
            if (targetFont != null && label.trueTypeFont != targetFont)
                label.trueTypeFont = targetFont;
        }

        if (ConfigManager.Visual.UIUniversal.Value)
        {
            var layout = GetMatchedLayout(path);
            if (layout.HasValue)
            {
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

    #region 4. Module B: Silent Integrity Fixes (Always On)
    [HarmonyPatch(typeof(PartsHeaderBackButton), "SetTitleText")]
    [HarmonyPrefix]
    public static bool PrefixHeaderTitle(PartsHeaderBackButton __instance, string _setTitleText)
    {
        if (string.IsNullOrEmpty(_setTitleText) || !__instance.IsSafe()) return true;

        if (__instance.titleLabel != null) __instance.titleLabel.text = "";
        string clean = _setTitleText.Sanitize().Replace("\n", " ");
        if (__instance.titleLabel2nd != null) __instance.titleLabel2nd.text = clean;

        float initialWidth = (clean.Length * __instance.titleLabel2nd.fontSize * 0.75f) + 60f;
        __instance.underLine.width = (int)initialWidth;
        __instance.underLine.gameObject.SetActive(true);

        CoroutineStarter.Run(AdjustTitleOnFly(__instance, clean));
        return false;
    }

    private static IEnumerator AdjustTitleOnFly(PartsHeaderBackButton instance, string text)
    {
        float timeout = Time.time + 3f;
        while (Time.time < timeout && instance.titleLabel2nd.text == text) yield return null;
        if (instance.IsSafe())
        {
            instance.titleLabel2nd.ProcessText();
            float finalWidth = instance.titleLabel2nd.mCalculatedSize.x + 40f;
            instance.underLine.width = (int)Math.Round(finalWidth);
            HeaderController header = SingletonMonoBehaviour<HeaderController>.Instance;
            if (header.IsSafe()) header.campaignIcons.SetIconPosition(header.viewManager.CurrentViewId, finalWidth);
        }
    }
    #endregion

    #region 5. Module C: Legacy TextMesh & Fallback
    [HarmonyPatch(typeof(TextMesh), nameof(TextMesh.text), MethodType.Setter)]
    [HarmonyPostfix]
    public static void PostfixTextMesh(TextMesh __instance, string value)
    {
        if (_resizeInProgress || !__instance.IsSafe() || string.IsNullOrEmpty(value)) return;

        bool doFont = ConfigManager.Visual.UIFont.Value;
        bool doResize = ConfigManager.Visual.UIUniversal.Value;
        if (!doFont && !doResize) return;

        try
        {
            _resizeInProgress = true;
            if (doFont && _fontSystemReady && _baseFont != null) __instance.font = _baseFont;

            if (doResize)
            {
                if (__instance.fontSize == 24) __instance.fontSize = GetAdaptiveSize(24);

                string path = __instance.transform.GetHierarchyPath();
                var cfg = GetMatchedLayout(path);

                float targetWidth = cfg.HasValue ? cfg.Value.width : 1.7f;

                TextSize textSize = new TextSize(__instance);
                if (textSize.Width > targetWidth) textSize.FitToWidth(targetWidth);
            }
        }
        finally { _resizeInProgress = false; }
    }

    [HarmonyPatch(typeof(FlTextParameter), "_ApplyData")]
    [HarmonyPostfix]
    public static void PostfixFlashSize(FlTextParameter __instance)
    {
        if (__instance.IsSafe() && __instance._fontSize == 24)
            __instance._fontSize = GetAdaptiveSize(24);
    }
    #endregion

    #region 6. Internal Cache Engine
    private static Font GetMatchedFont(string path)
    {
        lock (_uiLock) { if (_matchedFontCache.TryGetValue(path, out Font f)) return f; }
        Font match = null;
        foreach (var rule in _fontRules)
        {
            if (rule.Key.IsMatch(path)) { match = rule.Value; break; }
        }
        lock (_uiLock) { _matchedFontCache[path] = match; }
        return match;
    }

    private static (float width, UILabel.Overflow method)? GetMatchedLayout(string path)
    {
        lock (_uiLock) { if (_matchedLayoutCache.TryGetValue(path, out var l)) return l; }
        (float width, UILabel.Overflow method)? res = null;
        foreach (var rule in _layoutRules)
        {
            if (rule.regex.IsMatch(path)) { res = (rule.width, rule.method); break; }
        }
        lock (_uiLock) { _matchedLayoutCache[path] = res; }
        return res;
    }

    [HarmonyPatch(typeof(SceneManager), nameof(SceneManager.Internal_SceneLoaded))]
    [HarmonyPostfix]
    public static void ClearCaches()
    {
        lock (_uiLock)
        {
            _matchedFontCache.Clear();
            _matchedLayoutCache.Clear();
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
            Vector3 currentScale = _mesh.transform.localScale;
            currentScale.x *= ratio;
            _mesh.transform.localScale = currentScale;
        }
    }
    #endregion
}