#nullable enable
using Elements;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core;
using SceneManagement = UnityEngine.SceneManagement.SceneManager;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class NumberComponentPatch
{
    #region 1. Internal State & Dynamic Culture Cache
    private static readonly object _syncLock = new object();

    private static CultureInfo?  _cachedCulture;
    private static volatile string? _lastLangCode;

    private static CultureInfo CurrentFormatCulture
    {
        get
        {
            string configLang = ConfigManager.Translation.Code.Value;

            if (_cachedCulture == null || _lastLangCode != configLang)
            {
                try
                {
                    _cachedCulture = !string.IsNullOrEmpty(configLang)
                        ? new CultureInfo(configLang)
                        : CultureInfo.CurrentCulture;
                }
                catch
                {
                    _cachedCulture = CultureInfo.CurrentCulture;
                }
                _lastLangCode = configLang;
                FLog.Debug($"[Number] Culture updated to '{_cachedCulture!.Name}' (config='{configLang}').");
            }
            return _cachedCulture!;
        }
    }

    private static readonly Dictionary<IntPtr, long> _labelValueRegistry = new(1024);

    private static readonly Regex _numberDetectionRegex = new Regex(@"[1-9]\d{3,}", RegexOptions.Compiled);
    private static readonly Regex _dateExclusionRegex = new Regex(@"\d{2,4}[/\.\-]\d{2}[/\.\-]\d{2,4}", RegexOptions.Compiled);
    private static readonly Regex _hpFractionRegex = new Regex(@"^(\d+)/(\d{4,})$", RegexOptions.Compiled);
    private static readonly Regex _gradientRegex = new Regex(@"^(\[[0-9a-fA-F,-]+\])[x+×]?(\d{4,})(\[[0-9a-fA-F,-]+\])$", RegexOptions.Compiled);
    private static readonly Regex _floatDetectionRegex = new Regex(@"(\d{4,})\.(\d+)", RegexOptions.Compiled);
    #endregion

    #region 2. Module A: Engine Number Tracking (via NGUI Layer)
    // NOTE: Patching Il2CppSystem.Number.FormatInt32 / FormatInt64 directly is not viable
    // in Unity 6 / IL2CPP — the DMD trampoline emits invalid IL (InvalidProgramException).
    // Number formatting is handled entirely at the NGUI and UILabel layers (Modules B & D).
    #endregion

    #region 3. Module B: NGUI CustomUILabel Integration
    [HarmonyPatch(typeof(CustomUILabel), nameof(CustomUILabel.SetText), typeof(string), typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]
    [HarmonyPrefix]
    public static void PrefixNGUISetText(CustomUILabel __instance, Il2CppReferenceArray<Il2CppSystem.Object> _args)
    {
        if (!ConfigManager.Visual.UIUniversal.Value || _args == null || _args.Length == 0) return;

        foreach (var obj in _args)
        {
            if (obj == null) continue;
            var type = obj.GetIl2CppType();
            if (type == null) continue;

            if (type.Equals(Il2CppType.Of<int>()))
                _labelValueRegistry[__instance.Pointer] = obj.Unbox<int>();
            else if (type.Equals(Il2CppType.Of<long>()))
                _labelValueRegistry[__instance.Pointer] = obj.Unbox<long>();
        }
    }

    [HarmonyPatch(typeof(CustomUILabel), nameof(CustomUILabel.SetText), typeof(string), typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]
    [HarmonyPostfix]
    public static void PostfixNGUISetText(CustomUILabel __instance)
    {
        if (!ConfigManager.Visual.UIUniversal.Value || !__instance.IsSafe()) return;

        if (_labelValueRegistry.TryGetValue(__instance.Pointer, out long val))
        {
            if (_dateExclusionRegex.IsMatch(__instance.text)) return;

            string formatted = val.ToString("#,0", CurrentFormatCulture);
            string original = val.ToString();

            if (formatted != original)
                __instance.text = __instance.text.Replace(original, formatted);
        }
    }
    #endregion

    #region 4. Module C: Auto-Translator (XUAT) Bridge
    [HarmonyPatch(typeof(AutoTranslationPlugin), "SetText")]
    [HarmonyPrefix]
    public static void PrefixXuatSetText(ref string text)
    {
        if (!ConfigManager.Visual.UIUniversal.Value || string.IsNullOrEmpty(text)) return;

        if (text.IndexOf("ID", StringComparison.OrdinalIgnoreCase) >= 0 || _dateExclusionRegex.IsMatch(text)) return;

        // Format any bare large numbers found in the translated string.
        // (FormatInt32 engine-level patch is not viable on IL2CPP Unity 6;
        //  we apply formatting here at the XUAT boundary instead.)
        var matches = _numberDetectionRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (long.TryParse(match.Value, out long num))
                text = text.Replace(match.Value, num.ToString("#,0", CurrentFormatCulture));
        }
    }
    #endregion

    #region 5. Module D: Solo Labels & Combat Bubbles
    [HarmonyPatch(typeof(UILabel), "text", MethodType.Setter)]
    [HarmonyPrefix]
    public static void PrefixLabelSetter(UILabel __instance, ref string value)
    {
        if (!ConfigManager.Visual.UIUniversal.Value || !__instance.IsSafe() || value == null) return;

        string name = __instance.name.ToLower();
        if (name.Contains("input") || name.Contains("condition")) return;

        var floatMatch = _floatDetectionRegex.Match(value);
        if (floatMatch.Success)
        {
            if (long.TryParse(floatMatch.Groups[1].Value, out long frontPart))
            {
                string formattedFront = frontPart.ToString("#,0", CurrentFormatCulture);
                value = value.Replace(floatMatch.Groups[1].Value, formattedFront);
                return;
            }
        }

        var hpMatch = _hpFractionRegex.Match(value);
        if (hpMatch.Success)
        {
            foreach (Group group in hpMatch.Groups)
            {
                if (long.TryParse(group.Value, out long n))
                    value = value.Replace(group.Value, n.ToString("#,0", CurrentFormatCulture));
            }
            return;
        }

        var gradMatch = _gradientRegex.Match(value);
        if (gradMatch.Success)
        {
            if (long.TryParse(gradMatch.Groups[2].Value, out long n))
                value = value.Replace(gradMatch.Groups[2].Value, n.ToString("#,0", CurrentFormatCulture));
            return;
        }

        if (long.TryParse(value, out long pureNum))
        {
            if (__instance.overflowMethod == UILabel.Overflow.ClampContent)
                __instance.overflowMethod = UILabel.Overflow.ShrinkContent;

            value = pureNum.ToString("#,0", CurrentFormatCulture);
        }
    }
    #endregion

    #region 6. Cache Cleanup
    [HarmonyPatch(typeof(SceneManagement), nameof(SceneManagement.Internal_SceneLoaded))]
    [HarmonyPostfix]
    public static void ClearCaches()
    {
        lock (_syncLock)
        {
            _labelValueRegistry.Clear();
        }
    }
    #endregion
}