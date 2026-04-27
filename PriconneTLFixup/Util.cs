using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private static readonly Regex ColorTagRegex = new(@"\[([A-Fa-f0-9]{6})\]", RegexOptions.Compiled);
    private static readonly Regex ColorEndRegex = new(@"\[-\]", RegexOptions.Compiled);
    private static readonly Regex MarkCleanupRegex = new(@"(\p{Mn})\1+", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> _sanitizedPool = new(4000);
    private static readonly List<string> _poolHistory = new(4000);
    private static readonly Dictionary<int, string> _staticTranslationMap = new(1024);

    [ThreadStatic] private static StringBuilder? _processingBuffer;

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
        result = ColorTagRegex.Replace(result, "<color=#$1>");
        result = ColorEndRegex.Replace(result, "</color>");

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
                for (int i = 0; i < range; i++) { _sanitizedPool.Remove(_poolHistory[0]); _poolHistory.RemoveAt(0); }
                Log.Debug($"[Memory] Purged {range} cache entries to optimize heap.");
            }
            _sanitizedPool[key] = val;
            _poolHistory.Add(key);
        }
    }
    #endregion

    #region MODULE B: Universal UI & Duck Typing

    private static readonly Dictionary<(Type, string), PropertyInfo?> _memberCache = new(256);

    public static bool IsTextElement(this Component? c) => c != null &&
        (c is UnityEngine.UI.Text || c is TextMesh || c.GetType().Name.StartsWith("TextMeshPro"));

    public static void UpdateTextContent(this Component? c, string text)
    {
        if (c == null) return;
        if (c is UnityEngine.UI.Text t) t.text = text;
        else if (c is TextMesh tm) tm.text = text;
        else ReflectiveSet(c, "text", text);
    }

    private static void ReflectiveSet(Component c, string name, object value)
    {
        var key = (c.GetType(), name);
        PropertyInfo? prop;
        lock (_globalSync) { if (!_memberCache.TryGetValue(key, out prop)) _memberCache[key] = prop = c.GetType().GetProperty(name, UniversalFlags); }
        prop?.SetValue(c, value);
    }
    #endregion

    #region MODULE C: Integration & Sync Engine (XUAT)

    private static object? _xuatRef;
    private static Func<object, bool>? _isXuatActive;
    private static Func<object, float>? _xuatDelayGetter;
    private static bool _xuatInited;

    private static void EnsureXuatConnection()
    {
        if (_xuatInited) return;
        lock (_globalSync)
        {
            if (_xuatInited) return;
            try
            {
                var plugin = IL2CPPChainloader.Instance.Plugins.Values.FirstOrDefault(p => p.Metadata.GUID.Contains("autotranslator"));
                if (plugin?.Instance == null) return;

                _xuatRef = plugin.Instance;
                var type = _xuatRef.GetType();

                var fieldMode = type.GetField("_isInTranslatedMode", UniversalFlags);
                if (fieldMode != null) _isXuatActive = CreateAccessor<bool>(fieldMode);

                var manager = type.GetProperty("TranslationManager", UniversalFlags)?.GetValue(_xuatRef);
                var endpoint = manager?.GetType().GetProperty("CurrentEndpoint", UniversalFlags)?.GetValue(manager);
                var delayProp = endpoint?.GetType().GetProperty("TranslationDelay", UniversalFlags);
                if (delayProp != null) _xuatDelayGetter = CreateAccessor<float>(delayProp);

                Log.Info("[Integration] AutoTranslator synchronization established.");
            }
            catch (Exception ex) { Log.Debug($"[Integration] Reflection link failed: {ex.Message}"); }
            finally { _xuatInited = true; }
        }
    }

    public static bool IsExternalTranslationEnabled() { EnsureXuatConnection(); return _isXuatActive?.Invoke(_xuatRef!) ?? false; }
    public static float GetExternalDelay() { EnsureXuatConnection(); return _xuatDelayGetter?.Invoke(_xuatRef!) ?? 0.5f; }
    #endregion

    #region MODULE D: Asset Management & Font Fallback

    public static Font? SharedMainFont { get; private set; }
    private static readonly Dictionary<string, Font> _fontCache = new();

    public static void PreloadGlobalResources()
    {
        try
        {
            string lang = ConfigurationManager.Translation.Code.Value;

            string bepInExRoot = Path.GetDirectoryName(Paths.ConfigPath) ?? "";

            if (string.IsNullOrEmpty(bepInExRoot))
            {
                Log.Error("[Assets] Could not resolve BepInEx root path.");
                return;
            }

            string fontDir = Path.Combine(bepInExRoot, "Translation", lang, "Font");

            if (!Directory.Exists(fontDir))
            {
                Log.Warn($"[Assets] Translation directory missing: {fontDir}");
                return;
            }

            if (!Directory.Exists(fontDir)) return;

            string charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?:;()[]+-*/=%$#@&_\"'<>|\\" +
                             GetLanguageCharset(lang);

            foreach (var file in Directory.GetFiles(fontDir, "*.unity3d"))
            {
                var bundle = AssetBundle.LoadFromFile(file);
                var font = bundle.LoadAllAssets(Il2CppType.Of<Font>()).FirstOrDefault()?.Cast<Font>().Persistent();
                if (font != null) { font.RequestCharactersInTexture(charset, 24); SharedMainFont ??= font; }
                bundle.Unload(false);
            }
            Log.Info($"[Assets] Global preloading complete for: {lang}");
        }
        catch (Exception ex)
        {
            Log.Error("An error occurred during global resource preloading.", ex);
        }
    }

    private static string GetLanguageCharset(string lang) => lang switch
    {
        "th" => "\u0E01-\u0E7F",
        "ru" => "\u0400-\u04FF",
        "ja" => "\u3040-\u30FF\u4E00-\u9FFF",
        "vi" or "vn" => "\u00C0-\u1EF9",
        _ => ""
    };

    public static void RegisterFallback(UnityEngine.Object main, UnityEngine.Object fallback)
    {
        if (main == null || fallback == null) return;
        try
        {
            var table = main.GetType().GetProperty("fallbackFontAssetTable", UniversalFlags)?.GetValue(main);
            if (table == null) return;
            var contains = (bool)table.GetType().GetMethod("Contains")!.Invoke(table, new[] { fallback })!;
            if (!contains) table.GetType().GetMethod("Add")!.Invoke(table, new[] { fallback });
        }
        catch { }
    }
    #endregion

    #region MODULE E: Lifecycle & Object Security

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsSafe(this Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase? obj) => obj != null && obj.Pointer != IntPtr.Zero;

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

    [ThreadStatic] private static StringBuilder? _pathBuf;
    public static string GetHierarchyPath(this Transform? t)
    {
        if (t == null) return "";
        _pathBuf ??= new StringBuilder(128); _pathBuf.Clear();
        var stack = new Stack<string>();
        for (var curr = t; curr != null; curr = curr.parent) stack.Push(curr.name);
        while (stack.Count > 0) _pathBuf.Append('/').Append(stack.Pop());
        return _pathBuf.ToString();
    }

    public static Transform? FindDeep(this Transform p, string name)
    {
        var q = new Queue<Transform>(); q.Enqueue(p);
        while (q.Count > 0) { var c = q.Dequeue(); if (c.name == name) return c; foreach (Transform child in c) q.Enqueue(child); }
        return null;
    }

    public class WaitUntilOrTimeout(float timeout, Func<bool> predicate) : CustomYieldInstruction
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
}
