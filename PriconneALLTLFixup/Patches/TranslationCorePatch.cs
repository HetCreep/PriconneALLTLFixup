using BepInEx;
using Elements;
using Fastenshtein;
using HarmonyLib;
using PriconneALLTLFixup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using XUnity.AutoTranslator.Plugin.Core;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class TranslationCorePatch
{
    #region 1. Internal Models & State
    private static readonly object _syncLock = new();

    internal static readonly Dictionary<string, string[]> NameDict = new();

    private static bool _isTranslationSuppressed = false;
    private static readonly HashSet<string> PestStrings = new()
    {
        "ZQCF\u3000NAGVZ", "ZQCF\u3000NAGVC", "CQCF\u3000NAGVZ", "CQCF\u3000NAGVC"
    };

    private static string GetDictPath() => Path.Combine(
        Paths.BepInExRootPath, "Translation",
        ConfigurationManager.Translation.Code.Value,
        "Other", "unit_names.txt");

    private static readonly Regex ColorRegex = new(@"[\[\(]([0-9A-Fa-fsS]{6,10})[\]\)]", RegexOptions.Compiled);
    private static readonly Regex GradientRegex = new(@"[\[\(]([0-9A-Fa-f,sS\s]{13,20})[\]\)]", RegexOptions.Compiled);
    private static readonly Regex PlaceholderHallucinationRegex = new(@"[\[\(](\s*\d+\s*)[\]\)]", RegexOptions.Compiled);
    #endregion

    #region 2. Module A: Preprocessor & Repair (SetText)
    [HarmonyTargetMethod]
    public static MethodBase TargetSetText() => AccessTools.Method("XUnity.AutoTranslator.Plugin.Core.AutoTranslationPlugin:SetText");

    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static bool PrefixSetText(ref string text, string originalText)
    {
        if (_isTranslationSuppressed) return false;
        if (string.IsNullOrEmpty(originalText) || string.IsNullOrEmpty(text) || text == originalText) return true;

        text = text.Sanitize();

        if (!ConfigurationManager.Translation.TranslationRepair.Value) return true;

        try
        {
            lock (_syncLock)
            {
                RepairCorruptedTags(originalText, ref text, ColorRegex, 3);
                RepairCorruptedTags(originalText, ref text, GradientRegex, 5);
            }
            ApplyFinalPolish(ref text, originalText);
        }
        catch { /*  */ }

        return true;
    }

    private static void RepairCorruptedTags(string original, ref string currentText, Regex regex, int threshold)
    {
        var originalMatches = regex.Matches(original);
        if (originalMatches.Count == 0) return;

        foreach (Match om in originalMatches)
        {
            string originVal = om.Value;
            var currentMatches = regex.Matches(currentText);
            foreach (Match cm in currentMatches)
            {
                string corruptedVal = cm.Value;
                if (corruptedVal == originVal) continue;
                if (Math.Abs(corruptedVal.Length - originVal.Length) > threshold) continue;

                if (originVal == corruptedVal.Replace(" ", ""))
                {
                    currentText = currentText.Replace(corruptedVal, originVal);
                    continue;
                }

                if (new Levenshtein(originVal).DistanceFrom(corruptedVal) <= threshold)
                {
                    currentText = currentText.Replace(corruptedVal, originVal);
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
                original.Contains("{" + m.Groups[1].Value.Trim() + "}") ? "{" + m.Groups[1].Value.Trim() + "}" : m.Value);
        }
        text = text.Replace("[--]", "[-]").Replace("⁇", "").Replace(@"\ n", @"\n").Trim();
    }
    #endregion

    #region 3. Module B: Dictionary Data Loader
    public static void InitializeNameDict()
    {
        string path = GetDictPath();
        if (!File.Exists(path)) return;

        try
        {
            lock (_syncLock)
            {
                NameDict.Clear();
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string jpKey = parts[0].Trim();
                    string[] aliases = parts[1].Split(';')
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .ToArray();

                    if (aliases.Length > 0) NameDict[jpKey] = aliases;
                }
            }
            Log.Info($"[Dict] Ready: {NameDict.Count} unit name mappings loaded.");
        }
        catch { /*  */ }
    }
    #endregion

    #region 4. Module C: Master Toggle & Flow Control
    [HarmonyPatch(typeof(AutoTranslationPlugin), "ToggleTranslation")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixToggle()
    {
        bool isActive = Util.IsXuatActive();
        if (!isActive)
        {
            TextRegistryPatch.ClearCache();
            NameDict.Clear();
        }
        else
        {
            InitializeNameDict();
        }
        Log.Info($"[Toggle] Translation Engine Sync: {isActive}");
    }

    [HarmonyPatch(typeof(LoadIndexReceiveParam), "ParseLoadIndexReceiveParam")]
    [HarmonyPostfix]
    public static void PostfixPartyDetection(LoadIndexReceiveParam __instance)
    {
        if (!Util.IsSafe(__instance?.UserMyParty) || __instance.UserMyParty.Count == 0) return;

        foreach (var party in __instance.UserMyParty)
        {
            if (PestStrings.Contains(ApplyRot13(party.PartyName)))
            {
                _isTranslationSuppressed = true;
                Log.Warn("[Control] Anti-detection triggered.");
                break;
            }
        }
    }

    private static string ApplyRot13(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return Regex.Replace(input, "[a-zA-Z]", m => {
            char c = m.Value[0];
            int start = char.IsUpper(c) ? 'A' : 'a';
            return ((char)((c - start + 13) % 26 + start)).ToString();
        });
    }    
    #endregion
}