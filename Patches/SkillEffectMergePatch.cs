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

    private static readonly List<string> _texts   = new List<string>();
    private static readonly List<object> _uis     = new List<object>();
    private static readonly List<IntPtr> _uiPtrs  = new List<IntPtr>();
    private static long _lastTick;
    private static bool _requeuing;
    private static long WindowTicks => 200 * TimeSpan.TicksPerMillisecond;

    private static MethodInfo      _tryGet;
    private static ConstructorInfo _utCtor;

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

        // Effective text: from param or from UILabel (for empty-text polls)
        string effectiveText = string.IsNullOrWhiteSpace(text)
            ? (ui as Il2CppSystem.Object)?.TryCast<UILabel>()?.text
            : text;

        if (string.IsNullOrWhiteSpace(effectiveText)) return;
        if (!HasJapanese(effectiveText)) return;
        if (effectiveText.Contains('\u203b') || effectiveText.Contains('[')) return;

        // Get IL2CPP pointer for dedup — same UILabel polled again → skip
        IntPtr uiPtr = (ui as Il2CppSystem.Object)?.Pointer ?? IntPtr.Zero;
        if (uiPtr == IntPtr.Zero) return;

        string flat = effectiveText.Replace("\n", string.Empty);
        if (string.IsNullOrWhiteSpace(flat)) return;

        // If text had \n (auto-wrapped UILabel), re-queue flat so XUAT finds the key.
        // Do NOT return — still add to buffer for combining.
        if (flat != effectiveText)
        {
            _requeuing = true;
            try
            {
                TranslateMethod?.Invoke(__instance, new object[]
                {
                    ui, flat, scope, info,
                    allowStabilizationOnTextComponent, ignoreComponentState,
                    false, false, tc, untranslatedTextContext, context
                });
            }
            finally { _requeuing = false; }
        }

        // Buffer management — reset on timeout
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastTick > WindowTicks)
        {
            _texts.Clear();
            _uis.Clear();
            _uiPtrs.Clear();
        }
        _lastTick = now;

        // Dedup by UI POINTER — same UILabel polled again → skip.
        // Allow same TEXT from different UILabels (e.g. identical duplicate effects).
        if (_uiPtrs.Count > 0 && _uiPtrs[_uiPtrs.Count - 1] == uiPtr) return;

        _texts.Add(flat);
        _uis.Add(ui);
        _uiPtrs.Add(uiPtr);

        // Need ≥ 2 entries before trying combined
        if (_texts.Count < 2) return;

        // Try all suffixes — handles description-before-effects:
        // buffer = [desc, eff1, eff1, eff2, eff2]
        // suffix from 1 = eff1+eff1+eff2+eff2 → matches combined key
        for (int start = 0; start <= _texts.Count - 2; start++)
        {
            string combined = string.Concat(_texts.Skip(start));
            if (!KeyExists(tc, combined)) continue;

            // Combined key found — translate first label, blank the rest
            _requeuing = true;
            try
            {
                TranslateMethod?.Invoke(__instance, new object[]
                {
                    _uis[start], combined, scope, info,
                    allowStabilizationOnTextComponent, ignoreComponentState,
                    false, false, tc, untranslatedTextContext, context
                });
            }
            finally { _requeuing = false; }

            for (int i = start + 1; i < _uis.Count; i++)
            {
                var lbl = (_uis[i] as Il2CppSystem.Object)?.TryCast<UILabel>();
                if (lbl.IsSafe()) lbl.text = string.Empty;
            }

            _texts.Clear();
            _uis.Clear();
            _uiPtrs.Clear();
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

            if (_utCtor == null)
            {
                var ut = type.Assembly.GetType("XUnity.AutoTranslator.Plugin.Core.UntranslatedText");
                if (ut == null) return false;
                _utCtor = ut.GetConstructor(new[]
                    { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            }
            if (_utCtor == null) return false;

            if (_tryGet == null)
                _tryGet = type.GetMethod("TryGetTranslation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_tryGet == null) return false;

            // Plain key
            var k1 = _utCtor.Invoke(new object[] { combined, false, false, true, false, false });
            if ((bool)(_tryGet.Invoke(tc, new object[] { k1, false, false, -1, null }) ?? false)) return true;

            // Regex-capable key (for (\d+) patterns)
            var k2 = _utCtor.Invoke(new object[] { combined, false, false, true, true, true });
            return (bool)(_tryGet.Invoke(tc, new object[] { k2, false, true, -1, null }) ?? false);
        }
        catch { return false; }
    }
}
