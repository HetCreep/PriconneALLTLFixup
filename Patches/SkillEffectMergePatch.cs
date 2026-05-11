using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Intercepts TranslateOrQueueWebJobImmediate for ALL calls (not just empty-text polls).
/// For each JP text: strips \n and re-queues (fixes auto-wrapped descriptions).
/// Also buffers consecutive short JP effect texts and tries a combined key lookup.
/// </summary>
[HarmonyPatch]
public static class SkillEffectMergePatch
{
    private static MethodInfo _translateMethod;
    private static MethodInfo TranslateMethod => _translateMethod ??=
        typeof(AutoTranslationPlugin)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "TranslateOrQueueWebJobImmediate"
                              && m.GetParameters().Length >= 9);

    private static readonly List<string> _texts = new List<string>();
    private static readonly List<object> _uis   = new List<object>();
    private static long   _lastTick;
    private static bool   _requeuing;

    // Effect window: effects set within this window are considered part of same skill
    private static long WindowTicks => 100 * TimeSpan.TicksPerMillisecond;

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
        if (_requeuing) return; // prevent recursion from our own re-queues

        // Determine actual text: from param if non-empty, else from UILabel component
        string effectiveText = string.IsNullOrWhiteSpace(text)
            ? (ui as Il2CppSystem.Object)?.TryCast<UILabel>()?.text
            : text;

        if (string.IsNullOrWhiteSpace(effectiveText)) return;
        if (!HasJapanese(effectiveText)) return;
        if (effectiveText.Contains('\u203b') || effectiveText.Contains('[')) return;

        string flat = effectiveText.Replace("\n", string.Empty);

        // If text had \n (auto-wrapped), re-queue flat version so XUAT finds the key
        if (flat != effectiveText && flat.Length > 0)
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
            return; // description handled — don't add to effect buffer
        }

        // No \n → likely a skill effect line. Buffer for combining.
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastTick > WindowTicks)
        {
            _texts.Clear();
            _uis.Clear();
        }
        _lastTick = now;

        // Dedup: avoid adding same text twice from repeated XUAT polls
        if (_texts.Count == 0 || _texts[_texts.Count - 1] != flat)
        {
            _texts.Add(flat);
            _uis.Add(ui);
        }

        if (_texts.Count < 2) return;

        string combined = string.Concat(_texts);
        if (!KeyExists(tc, combined)) return;

        // Combined key found — translate on first label, blank subsequent
        _requeuing = true;
        try
        {
            TranslateMethod?.Invoke(__instance, new object[]
            {
                _uis[0], combined, scope, info,
                allowStabilizationOnTextComponent, ignoreComponentState,
                false, false, tc, untranslatedTextContext, context
            });
        }
        finally { _requeuing = false; }

        for (int i = 1; i < _uis.Count; i++)
        {
            var lbl = (_uis[i] as Il2CppSystem.Object)?.TryCast<UILabel>();
            if (lbl.IsSafe()) lbl.text = string.Empty;
        }

        _texts.Clear();
        _uis.Clear();
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

            var k1 = _utCtor.Invoke(new object[] { combined, false, false, true, false, false });
            if ((bool)(_tryGet.Invoke(tc, new object[] { k1, false, false, -1, null }) ?? false)) return true;

            // Try with regex enabled (for patterns like (\d+))
            var k2 = _utCtor.Invoke(new object[] { combined, false, false, true, true, true });
            return (bool)(_tryGet.Invoke(tc, new object[] { k2, false, true, -1, null }) ?? false);
        }
        catch { return false; }
    }
}
