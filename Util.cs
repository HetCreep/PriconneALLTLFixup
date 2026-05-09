using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PriconneALLTLFixup;

public static class Util
{
    #region 1. Infrastructure & Shared State
    public const BindingFlags UniversalFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly object _globalSync = new();
    #endregion

    #region MODULE A: Text & Translation Engine
    private static readonly Regex MarkCleanupRegex = new(@"(\p{Mn})\1+", RegexOptions.Compiled);
    // Strip dangling TMPro color tags that leaked into NGUI text via translation engine
    private static readonly Regex StrayTMProColorOpenRegex  = new(@"<color=#[0-9A-Fa-f]{3,8}>", RegexOptions.Compiled);
    private static readonly Regex StrayTMProColorCloseRegex = new(@"</color>", RegexOptions.Compiled);
    // Strip dangling Unity size/bold/italic tags that also leak
    private static readonly Regex StrayTMProTagRegex = new(@"</?(?:size|b|i|s|u)(?:=\d+)?>", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> _sanitizedPool = new(4000);
    private static readonly Queue<string> _poolHistory = new(4000);
    private static readonly Dictionary<int, string> _staticTranslationMap = new(1024);

    [ThreadStatic] private static StringBuilder _processingBuffer;

    public static string Sanitize(this string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        lock (_globalSync) { if (_sanitizedPool.TryGetValue(input, out var cached)) return cached; }

        _processingBuffer ??= new StringBuilder(1024);
        _processingBuffer.Clear();
        _processingBuffer.Append(input.Normalize(NormalizationForm.FormC));

        _processingBuffer.Replace("\r\n", "\n").Replace("/\\n", "\n").Replace("\\n", "\n")
                         .Replace("\u200B", "").Replace("\u3000", " ");

        string result = _processingBuffer.ToString();
        result = MarkCleanupRegex.Replace(result, "$1");
        // Strip stray TMPro tags that translation engines sometimes inject into NGUI labels
        result = StrayTMProColorOpenRegex.Replace(result, "");
        result = StrayTMProColorCloseRegex.Replace(result, "");
        result = StrayTMProTagRegex.Replace(result, "");

        UpdateSanitizedPool(input, result);
        return result;
    }

    private static void UpdateSanitizedPool(string key, string val)
    {
        lock (_globalSync)
        {
            if (_sanitizedPool.Count >= 4000)
            {
                int range = Math.Min(100, _poolHistory.Count);
                for (int i = 0; i < range; i++)
                {
                    if (_poolHistory.TryDequeue(out var oldestKey))
                    {
                        _sanitizedPool.Remove(oldestKey);
                    }
                }
                FLog.Debug($"[Memory] Purged {range} cache entries to optimize heap.");
            }
            if (!_sanitizedPool.ContainsKey(key))
                _poolHistory.Enqueue(key);
            _sanitizedPool[key] = val;
        }
    }
    #endregion

    #region MODULE B: Universal UI & Duck Typing
    private static readonly Dictionary<(Type, string), Action<Component, object>> _setterCache = new(256);

    private static readonly Dictionary<Type, bool> _textElementTypeCache = new(32);

    public static bool IsTextElement(this Component c)
    {
        if (c == null) return false;
        if (c is UnityEngine.UI.Text || c is TextMesh) return true;

        Type t = c.GetType();
        lock (_globalSync)
        {
            if (_textElementTypeCache.TryGetValue(t, out bool isText)) return isText;

            isText = t.Name.StartsWith("TextMeshPro");
            _textElementTypeCache[t] = isText;
            return isText;
        }
    }

    public static void UpdateTextContent(this Component c, string text)
    {
        if (c == null) return;
        if (c is UnityEngine.UI.Text t) t.text = text;
        else if (c is TextMesh tm) tm.text = text;
        else ReflectiveSet(c, "text", text);
    }

    private static void ReflectiveSet(Component c, string name, object value)
    {
        var key = (c.GetType(), name);
        Action<Component, object> setter;

        lock (_globalSync)
        {
            if (!_setterCache.TryGetValue(key, out setter))
            {
                var prop = c.GetType().GetProperty(name, UniversalFlags);
                if (prop != null && prop.CanWrite)
                {
                    setter = CreateSetter(prop);
                    _setterCache[key] = setter;
                }
                else
                {
                    _setterCache[key] = null;
                }
            }
        }

        setter?.Invoke(c, value);
    }

    private static Action<Component, object> CreateSetter(PropertyInfo prop)
    {
        var targetParam = Expression.Parameter(typeof(Component), "target");
        var valueParam = Expression.Parameter(typeof(object), "value");

        var castTarget = Expression.Convert(targetParam, prop.DeclaringType!);
        var castValue = Expression.Convert(valueParam, prop.PropertyType);

        var propertyAccess = Expression.Property(castTarget, prop);

        var assign = Expression.Assign(propertyAccess, castValue);

        return Expression.Lambda<Action<Component, object>>(assign, targetParam, valueParam).Compile();
    }
    #endregion

    #region MODULE C: Integration & Sync Engine (XUAT Bridge)
    private static object _xuatInstance;
    private static object _xuatSettings;

    private static Func<object, bool> _xuatModeGetter;
    private static Func<object, float> _xuatDelayGetter;
    private static Action<object, string> _xuatLanguageSetter;
    private static Func<object, string> _xuatLanguageGetter;

    private static bool _isXuatLinked;

    private static void LinkTranslationEngine()
    {
        if (_isXuatLinked) return;
        lock (_globalSync)
        {
            if (_isXuatLinked) return;
            try
            {
                var plugin = IL2CPPChainloader.Instance.Plugins.Values
                    .FirstOrDefault(p => p.Metadata.GUID.Contains("autotranslator"));

                if (plugin?.Instance == null) return;
                _xuatInstance = plugin.Instance;
                var type = _xuatInstance.GetType();

                var settingsProp = type.GetProperty("Settings", UniversalFlags);
                _xuatSettings = settingsProp?.GetValue(_xuatInstance);

                if (_xuatSettings != null)
                {
                    var langProp = _xuatSettings.GetType().GetProperty("Language", UniversalFlags);
                    if (langProp != null)
                    {
                        _xuatLanguageGetter = obj => langProp.GetValue(_xuatSettings)?.ToString();
                        _xuatLanguageSetter = (obj, val) => langProp.SetValue(_xuatSettings, val);
                    }
                }

                var fieldMode = type.GetField("_isInTranslatedMode", UniversalFlags);
                if (fieldMode != null) _xuatModeGetter = CreateAccessor<bool>(fieldMode);

                var manager = type.GetProperty("TranslationManager", UniversalFlags)?.GetValue(_xuatInstance);
                var endpoint = manager?.GetType().GetProperty("CurrentEndpoint", UniversalFlags)?.GetValue(manager);
                var delayProp = endpoint?.GetType().GetProperty("TranslationDelay", UniversalFlags);
                if (delayProp != null) _xuatDelayGetter = CreateAccessor<float>(delayProp);

                FLog.Info("[Bridge] XUAT High-performance link established.");
            }
            catch (Exception ex) { FLog.Debug($"[Bridge] Link failed: {ex.Message}"); }
            finally { _isXuatLinked = true; }
        }
    }

    public static bool IsXuatActive() { LinkTranslationEngine(); return _xuatModeGetter?.Invoke(_xuatInstance) ?? false; }
    public static float GetXuatDelay() { LinkTranslationEngine(); return _xuatDelayGetter?.Invoke(_xuatInstance) ?? 0.5f; }

    public static string GetXuatBridgeLanguage()
    {
        LinkTranslationEngine();

        // 1st: try reflect from live XUAT plugin instance (works if Settings property exists)
        string detected = _xuatLanguageGetter?.Invoke(_xuatInstance);
        if (!string.IsNullOrEmpty(detected)) return detected;

        // 2nd: read AutoTranslatorConfig.ini directly — most reliable fallback
        try
        {
            string iniPath = Path.Combine(BepInEx.Paths.ConfigPath, "AutoTranslatorConfig.ini");
            if (File.Exists(iniPath))
            {
                bool inGeneral = false;
                foreach (string line in File.ReadLines(iniPath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase)) { inGeneral = true; continue; }
                    if (trimmed.StartsWith("[") && inGeneral) break; // left [General]
                    if (inGeneral && trimmed.StartsWith("Language=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = trimmed.Substring("Language=".Length).Trim();
                        if (!string.IsNullOrEmpty(val))
                        {
                            FLog.Debug($"[Bridge] Language read from AutoTranslatorConfig.ini: '{val}'");
                            return val;
                        }
                    }
                }
            }
        }
        catch (Exception ex) { FLog.Debug($"[Bridge] AutoTranslatorConfig.ini read failed: {ex.Message}"); }

        // 3rd: fall back to our own config default
        return ConfigManager.Translation.Code.DefaultValue;
    }

    public static string GetXuatLanguage()
    {
        string modLanguage = ConfigManager.Translation.Code.Value;

        if (!string.IsNullOrWhiteSpace(modLanguage))
        {
            return modLanguage;
        }

        return GetXuatBridgeLanguage();
    }

    public static void SyncXuatLanguage(string targetLang)
    {
        LinkTranslationEngine();
        if (_xuatLanguageSetter == null) return;

        string current = _xuatLanguageGetter?.Invoke(_xuatInstance);
        if (!string.Equals(current, targetLang, StringComparison.OrdinalIgnoreCase))
        {
            _xuatLanguageSetter(_xuatInstance, targetLang);
            FLog.Info($"[Bridge] XUAT Language forced to: {targetLang}");
        }
    }
    #endregion

    #region MODULE D: Asset Management & Smart Font Fallback
    public static Font SharedMainFont { get; private set; }

    private static readonly List<UnityEngine.Object> _loadedFontAssets = new();

    public static void PreloadGlobalResources()
    {
        try
        {
            string lang = ConfigManager.Translation.Code.Value;
            string root = Path.GetDirectoryName(Paths.ConfigPath) ?? string.Empty;
            string fontDir = Path.Combine(root, "Translation", lang, "Font");

            if (!Directory.Exists(fontDir)) return;

            string charset = GetHybridCharset(fontDir);

            foreach (var file in Directory.GetFiles(fontDir, "*.unity3d"))
            {
                var bundle = AssetBundle.LoadFromFile(file);
                if (bundle == null) continue;

                var font = bundle.LoadAllAssets(Il2CppType.Of<Font>()).FirstOrDefault()?.Cast<Font>();
                if (font != null)
                {
                    font.Persistent();
                    font.RequestCharactersInTexture(charset, 32, FontStyle.Normal);
                    SharedMainFont ??= font;
                }

                var allAssets = bundle.LoadAllAssets();
                foreach (var asset in allAssets) { asset.Persistent(); _loadedFontAssets.Add(asset); }

                bundle.Unload(false);
            }
            FLog.Info($"[Assets] Global preloading complete. ({_loadedFontAssets.Count} assets)");
        }
        catch (Exception ex) { FLog.Error("Global font preloading failed.", ex); }
    }

    private static string GetHybridCharset(string fontDir)
    {
        string baseCharset = ExpandCharsetRange("A-Z a-z 0-9 .,!?:;()[]+-*/=%$#@&_\"'<>|\\ \u3040-\u30FF\u4E00-\u9FFF");

        StringBuilder combinedCharset = new StringBuilder(baseCharset);

        string customPath = Path.Combine(fontDir, "charset.txt");
        if (File.Exists(customPath))
        {
            try
            {
                string fileContent = File.ReadAllText(customPath, Encoding.UTF8);

                string cleanedContent = fileContent.Replace("\r", "").Replace("\n", "").Replace(" ", "");

                string unescapedContent = System.Text.RegularExpressions.Regex.Unescape(cleanedContent);

                combinedCharset.Append(ExpandCharsetRange(unescapedContent));

                FLog.Info($"[Assets] Successfully loaded additional characters from {customPath}");
            }
            catch (Exception ex)
            {
                FLog.Warn($"[Assets] Failed to read custom charset.txt: {ex.Message}");
            }
        }

        return combinedCharset.ToString();
    }

    private static string ExpandCharsetRange(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        StringBuilder sb = new();
        for (int i = 0; i < input.Length; i++)
        {
            if (i + 2 < input.Length && input[i + 1] == '-')
            {
                char startChar = input[i];
                char endChar = input[i + 2];

                if (startChar <= endChar)
                {
                    for (char c = startChar; c <= endChar; c++) sb.Append(c);
                }
                i += 2;
            }
            else
            {
                sb.Append(input[i]);
            }
        }
        return sb.ToString();
    }

    public static void RegisterFallback(UnityEngine.Object main, UnityEngine.Object fallback)
    {
        if (!main.IsSafe() || !fallback.IsSafe()) return;
        try
        {
            var tableProp = main.GetType().GetProperty("fallbackFontAssetTable", UniversalFlags);
            var table = tableProp?.GetValue(main);
            if (table == null) return;

            var containsMethod = table.GetType().GetMethod("Contains", UniversalFlags);
            bool alreadyHas = (bool)containsMethod.Invoke(table, new[] { fallback });

            if (!alreadyHas)
            {
                var addMethod = table.GetType().GetMethod("Add", UniversalFlags);
                addMethod?.Invoke(table, new[] { fallback });
                FLog.Debug($"[Assets] Linked fallback: {fallback.name} -> {main.name}");
            }
        }
        catch (Exception ex) { FLog.Debug($"[Assets] RegisterFallback failed: {ex.Message}"); }
    }
    #endregion

    #region MODULE E: Lifecycle & Object Security
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSafe(this Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj) => obj != null && obj.Pointer != IntPtr.Zero;

    public static T Persistent<T>(this T obj) where T : UnityEngine.Object
    {
        if (obj.IsSafe()) obj.hideFlags |= HideFlags.DontUnloadUnusedAsset | HideFlags.HideAndDontSave;
        return obj;
    }

    public static void DestroySafe(UnityEngine.Object obj)
    {
        if (!obj.IsSafe()) return;
        if (Application.isEditor) UnityEngine.Object.DestroyImmediate(obj); else UnityEngine.Object.Destroy(obj);
    }

    public static float ScreenAspectRatio => (float)Screen.width / Screen.height;
    public static float ResponsiveScale(float referenceWidth = 1920f) => Screen.width / referenceWidth;
    #endregion

    #region MODULE F: Hierarchy & Yield Instructions
    [ThreadStatic] private static StringBuilder _pathBuf;
    public static string GetHierarchyPath(this Transform t)
    {
        if (t == null) return "";
        _pathBuf ??= new StringBuilder(128); _pathBuf.Clear();
        var stack = new Stack<string>();
        for (var curr = t; curr != null; curr = curr.parent) stack.Push(curr.name);
        while (stack.Count > 0) _pathBuf.Append('/').Append(stack.Pop());
        return _pathBuf.ToString();
    }

    public static Transform FindDeep(this Transform p, string name)
    {
        if (p == null) return null;

        var q = new Queue<Transform>();
        q.Enqueue(p);

        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c.name == name) return c;

            int childCount = c.childCount;
            for (int i = 0; i < childCount; i++)
            {
                q.Enqueue(c.GetChild(i));
            }
        }
        return null;
    }

    public class WaitUntilOrTimeoutInstruction(float timeout, Func<bool> predicate) : CustomYieldInstruction
    {
        private readonly float _endTime = Time.time + timeout;
        public override bool keepWaiting => Time.time < _endTime && !predicate();
    }

    public static Func<object, TR> CreateAccessor<TR>(MemberInfo member)
    {
        var param = Expression.Parameter(typeof(object));
        Expression body = member switch
        {
            PropertyInfo p => Expression.Property(Expression.Convert(param, p.DeclaringType!), p),
            FieldInfo f => Expression.Field(Expression.Convert(param, f.DeclaringType!), f),
            _ => throw new NotSupportedException()
        };
        return Expression.Lambda<Func<object, TR>>(Expression.Convert(body, typeof(TR)), param).Compile();
    }
    #endregion

    #region MODULE G: Rank Display Helpers
    /// <summary>
    /// Returns the locale-appropriate ordinal rank suffix for <paramref name="rank"/>.
    /// <list type="bullet">
    ///   <item>English: 1st / 2nd / 3rd / Nth</item>
    ///   <item>CJK (ZH/JA/KO): 位</item>
    ///   <item>Thai, Arabic, and most other scripts: empty string (no grammatical suffix)</item>
    /// </list>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetRankSuffix(int rank)
    {
        string lang = ConfigManager.Translation.Code.Value;
        if (string.IsNullOrWhiteSpace(lang)) lang = GetXuatLanguage();

        return lang switch
        {
            "zh" or "ja" or "ko" => "位",

            "en" => (rank % 100) switch
            {
                11 or 12 or 13 => "th",
                _ => (rank % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
            },

            "fr" => rank == 1 ? "er" : "e",
            "de" or "ru" or "uk" or "bg" or "pl" => ".",
            "es" or "pt" => "º",
            "it"         => "°",
            "nl"         => "e",

            _ => string.Empty
        };
    }
    #endregion
}