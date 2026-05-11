using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

/// <summary>
/// Buffers consecutive JP skill effect UILabel texts (from re-translation pass) and
/// checks XUAT TextCache for a combined key. If found, queues combined translation on
/// first label and blanks subsequent labels — matching concatenated keys in translation files.
/// Lives in a separate class to isolate from the main TranslationCorePatch patching.
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
    private static long   WindowTicks => 500 * TimeSpan.TicksPerMillisecond;

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
        // Only process the re-translation pass (empty text = XUAT polling for component text)
        if (!string.IsNullOrWhiteSpace(text)) return;

        string componentText = (ui as Il2CppSystem.Object)?.TryCast<UILabel>()?.text;
        if (string.IsNullOrWhiteSpace(componentText)) return;

        // Skip already-translated or special-format text
        bool hasJP = false;
        foreach (char c in componentText)
            if ((c >= '\u3040' && c <= '\u30FF') || (c >= '\u4E00' && c <= '\u9FFF'))
            { hasJP = true; break; }
        if (!hasJP) return;
        if (componentText.Contains('\u203b') || componentText.Contains('[')) return;

        string flat = componentText.Replace("\n", string.Empty);

        // Reset buffer if too much time has passed
        long now = DateTime.UtcNow.Ticks;
        if (now - _lastTick > WindowTicks)
        {
            _texts.Clear();
            _uis.Clear();
        }
        _lastTick = now;

        // Don't add duplicates from repeated XUAT polls
        if (_texts.Count == 0 || _texts[_texts.Count - 1] != flat)
        {
            _texts.Add(flat);
            _uis.Add(ui);
        }

        if (_texts.Count < 2) return;

        string combined = string.Concat(_texts);
        if (!KeyExists(tc, combined)) return;

        // Combined key found — translate on first label, blank subsequent
        TranslateMethod?.Invoke(__instance, new object[]
        {
            _uis[0], combined, scope, info,
            allowStabilizationOnTextComponent, ignoreComponentState,
            false, false, tc, untranslatedTextContext, context
        });

        for (int i = 1; i < _uis.Count; i++)
        {
            var lbl = (_uis[i] as Il2CppSystem.Object)?.TryCast<UILabel>();
            if (lbl.IsSafe()) lbl.text = string.Empty;
        }

        _texts.Clear();
        _uis.Clear();
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

            var k2 = _utCtor.Invoke(new object[] { combined, false, false, true, true, true });
            return (bool)(_tryGet.Invoke(tc, new object[] { k2, false, true, -1, null }) ?? false);
        }
        catch { return false; }
    }
}
