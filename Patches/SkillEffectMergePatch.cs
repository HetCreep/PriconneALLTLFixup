using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class SkillEffectMergePatch
{
    private static MethodInfo _translateMethod;
    private static MethodInfo TranslateMethod => _translateMethod ??=
        typeof(AutoTranslationPlugin)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "TranslateOrQueueWebJobImmediate"
                              && m.GetParameters().Length >= 9);

    private static readonly List<string> _texts  = new List<string>();
    private static readonly List<object> _uis    = new List<object>();
    private static readonly List<IntPtr> _ptrs   = new List<IntPtr>();
    private static long _lastTick;
    private static bool _requeuing;
    private static long WindowTicks => 200 * TimeSpan.TicksPerMillisecond;

    // Lazy-init via dynamic discovery (constructor param count varies across XUAT versions)
    private static MethodInfo      _tryGet;
    private static ConstructorInfo _utCtor;
    private static object[]        _tryGetArgs; // pre-built args template

    [HarmonyPatch(typeof(AutoTranslationPlugin), "TranslateOrQueueWebJobImmediate")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixMergeEffects(
        AutoTranslationPlugin __instance,
        object ui,
        string text,
        int scope,
        object info,
        bool allowStabilizationOnTextComponent,
        bool ignoreComponentState,
        object tc,
        object untranslatedTextContext,
        object context)
    {
        if (_requeuing) return;

        string effectiveText = string.IsNullOrWhiteSpace(text)
            ? (ui as Il2CppSystem.Object)?.TryCast<UILabel>()?.text
            : text;
        if (string.IsNullOrWhiteSpace(effectiveText)) return;
        if (!HasJapanese(effectiveText)) return;
        if (effectiveText.Contains('\u203b') || effectiveText.Contains('[')) return;

        IntPtr uiPtr = (ui as Il2CppSystem.Object)?.Pointer ?? IntPtr.Zero;
        if (uiPtr == IntPtr.Zero) return;

        string flat = effectiveText.Replace("\n", string.Empty);
        if (string.IsNullOrWhiteSpace(flat)) return;

        // Re-queue \n-stripped version (fixes auto-wrapped descriptions)
        if (flat != effectiveText)
        {
            _requeuing = true;
            try { TranslateMethod?.Invoke(__instance, new object[] { ui, flat, scope, info, allowStabilizationOnTextComponent, ignoreComponentState, false, false, tc, untranslatedTextContext, context }); }
            finally { _requeuing = false; }
        }

        long now = DateTime.UtcNow.Ticks;
        if (now - _lastTick > WindowTicks) { _texts.Clear(); _uis.Clear(); _ptrs.Clear(); }
        _lastTick = now;

        // Dedup by IL2CPP pointer — same UILabel polled again → skip
        if (_ptrs.Count > 0 && _ptrs[_ptrs.Count - 1] == uiPtr) return;
        _texts.Add(flat); _uis.Add(ui); _ptrs.Add(uiPtr);

        if (_texts.Count < 2) return;

        // Try all suffixes — handles buffer containing description before effects
        for (int start = 0; start <= _texts.Count - 2; start++)
        {
            string combined = string.Concat(_texts.Skip(start));
            if (!KeyExists(tc, combined)) continue;

            _requeuing = true;
            try { TranslateMethod?.Invoke(__instance, new object[] { _uis[start], combined, scope, info, allowStabilizationOnTextComponent, ignoreComponentState, false, false, tc, untranslatedTextContext, context }); }
            finally { _requeuing = false; }

            for (int i = start + 1; i < _uis.Count; i++)
            {
                var lbl = (_uis[i] as Il2CppSystem.Object)?.TryCast<UILabel>();
                if (lbl.IsSafe()) lbl.text = string.Empty;
            }
            _texts.Clear(); _uis.Clear(); _ptrs.Clear();
            return;
        }
    }

    private static bool HasJapanese(string s)
    {
        foreach (char c in s)
            if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF'))
                return true;
        return false;
    }

    private static bool KeyExists(object tc, string combined)
    {
        try
        {
            if (tc == null) return false;
            var type = tc.GetType();

            // Dynamic ctor discovery — find first ctor whose first param is string
            if (_utCtor == null)
            {
                var ut = type.Assembly.GetType("XUnity.AutoTranslator.Plugin.Core.UntranslatedText");
                if (ut == null) return false;
                foreach (var c in ut.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var p = c.GetParameters();
                    if (p.Length >= 1 && p[0].ParameterType == typeof(string)) { _utCtor = c; break; }
                }
            }
            if (_utCtor == null) return false;

            // Dynamic TryGetTranslation discovery
            if (_tryGet == null)
            {
                _tryGet = type.GetMethod("TryGetTranslation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (_tryGet == null) return false;

            // Build ctor args: first = combined text, rest = defaults
            var ctorPs = _utCtor.GetParameters();
            var ctorArgs = new object[ctorPs.Length];
            ctorArgs[0] = combined;
            for (int i = 1; i < ctorPs.Length; i++)
                ctorArgs[i] = ctorPs[i].ParameterType == typeof(bool) ? (object)false : (object)null;

            // Build TryGetTranslation args: first = UntranslatedText, rest = defaults
            var tryPs = _tryGet.GetParameters();
            if (_tryGetArgs == null || _tryGetArgs.Length != tryPs.Length)
                _tryGetArgs = new object[tryPs.Length];
            // arg0 = untranslatedText (set below), rest = defaults
            for (int i = 1; i < tryPs.Length; i++)
            {
                var pt = tryPs[i].ParameterType;
                _tryGetArgs[i] = pt == typeof(bool) ? (object)false
                               : pt == typeof(int)  ? (object)-1
                               : null;
            }

            // Try plain key
            _tryGetArgs[0] = _utCtor.Invoke(ctorArgs);
            if ((bool)(_tryGet.Invoke(tc, _tryGetArgs) ?? false)) return true;

            // Try with "allow regex" flags if ctor supports it
            if (ctorPs.Length >= 5)
            {
                ctorArgs[ctorArgs.Length - 2] = true; // allowTranslationOverride-like
                ctorArgs[ctorArgs.Length - 1] = true; // isForced-like
                _tryGetArgs[0] = _utCtor.Invoke(ctorArgs);
                // Set regex flag in TryGetTranslation if it has bool params
                if (_tryGetArgs.Length >= 3) _tryGetArgs[2] = true;
                if ((bool)(_tryGet.Invoke(tc, _tryGetArgs) ?? false)) return true;
            }

            return false;
        }
        catch { return false; }
    }
}
