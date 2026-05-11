using BepInEx;
using Elements;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using PriconneALLTLFixup;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class TextRegistryPatch
{
    #region 1. Internal Models & State Management
    private static readonly object _syncLock = new();

    internal static readonly Dictionary<eTextId, string> OriginalStrings   = new();
    internal static readonly Dictionary<eTextId, string> TranslatedStrings = new();
    internal static readonly List<ProcessedItem> StoredSkillTexts           = new();

    private const eTextId SKILL_EFFECT_HEADER_ID = (eTextId)10101004;

    internal struct ProcessedItem
    {
        public PartsUnitSkillDetailTextPlate.ePlateType PlateType;
        public int    GroupId;
        public string Text;

        public ProcessedItem(PartsUnitSkillDetailTextPlate.ePlateType plateType, string text, int groupId)
        {
            PlateType = plateType;
            Text      = text;
            GroupId   = groupId;
        }
    }
    #endregion

    #region 2. Module A: Global Text Registry
    [HarmonyPatch(typeof(ConstTextData), nameof(ConstTextData.CreateInstanceAndLoadInitialize))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixLoadConstText()
    {
        string path = Path.Combine(Paths.BepInExRootPath, "Translation",
                                   ConfigManager.Translation.Code.Value, "Other", "text_id.txt");
        if (!File.Exists(path)) return;

        var instance = Singleton<ConstTextData>.Instance;
        if (!instance.IsSafe() || instance.scriptableObject == null) return;

        var dict = instance.scriptableObject.DataDictionary;

        try
        {
            lock (_syncLock)
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int splitIdx = line.IndexOf('=');
                    if (splitIdx <= 0) continue;

                    if (Enum.TryParse<eTextId>(line.Substring(0, splitIdx).Trim(), out var textId))
                    {
                        if (dict.ContainsKey(textId))
                        {
                            string val = line.Substring(splitIdx + 1).Sanitize();
                            OriginalStrings[textId]   = dict[textId];
                            TranslatedStrings[textId] = val;
                            dict[textId] = val;
                        }
                    }
                }
            }
            FLog.Info($"[Registry] {TranslatedStrings.Count} UI Strings injected into memory.");
        }
        catch (Exception ex) { FLog.Error($"[Registry] Injection failed: {ex.Message}"); }
    }
    #endregion

    #region 3. Module B: Smart Skill Layout
    // PrefixSkillInit DISABLED:
    // IL2CPP List<ValueTuple<enum,string>> — every access pattern crashes natively:
    //   foreach/ToArray → native crash  |  get_Item[i] → managed exception
    //   Clear/Add       → native crash  |  RemoveAt alone → managed exception (safe)
    // All skill merging is handled by PostfixSkillDisplay (pure Unity, safe).
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), nameof(PartsUnitSkillDetailTextController.Initialize))]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillInit(
        Il2CppSystem.Collections.Generic.List<ValueTuple<PartsUnitSkillDetailTextPlate.ePlateType, string>> _detailTextList)
        => _ = _detailTextList; // no-op: do not touch IL2CPP list

    /// <summary>
    /// Ported from PriconneTLFixup's SkillPopupPatch.
    /// Runs after PartsDialogUnitSkillDetail.display renders all plates.
    /// Reads UILabel.text from Unity GameObjects (no IL2CPP List access).
    /// Merges consecutive effect-section DetailLabel texts into the first plate,
    /// hides extra plates, then sets the merged JP text on the first label so
    /// XUAT re-translates it using the merged key in the translation files.
    /// </summary>
    [HarmonyPatch(typeof(PartsDialogUnitSkillDetail), "display")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSkillDisplay(PartsDialogUnitSkillDetail __instance)
    {
        if (!ConfigManager.Core.TranslatorIntegration.Value || !ConfigManager.UI.SmartSkillLayout.Value) return;
        if (!__instance.IsSafe()) return;

        var root = __instance.transform.Find("ScrollContent/ScrollView/WrapContent");
        if (!root.IsSafe()) return;

        bool    inEffectSection  = false;
        UILabel firstEffectLabel = null;
        var     sb               = new StringBuilder();

        for (int i = 0; i < root.childCount; i++)
        {
            var plate = root.GetChild(i);
            if (!plate.IsSafe()) continue;

            var titleT  = plate.Find("TitleLabel");
            var detailT = plate.Find("DetailLabel");

            bool hasTitle  = titleT.IsSafe()  && titleT.gameObject.activeSelf;
            bool hasDetail = detailT.IsSafe() && detailT.gameObject.activeSelf;

            if (!hasTitle && !hasDetail) continue;

            if (hasTitle)
            {
                // Flush previous effect group before entering new section
                if (inEffectSection && firstEffectLabel.IsSafe() && sb.Length > 0)
                {
                    firstEffectLabel.text = sb.ToString();
                    sb.Clear();
                    firstEffectLabel = null;
                }

                inEffectSection = false;

                // Detect "Skill effect" section by TitleLabel text
                var hdr = titleT.GetComponent<UILabel>();
                if (hdr.IsSafe())
                {
                    var txt = hdr.text ?? string.Empty;
                    if (txt == "スキル効果"
                        || txt.IndexOf("Skill effect", StringComparison.OrdinalIgnoreCase) >= 0)
                        inEffectSection = true;
                }
            }
            else if (hasDetail && inEffectSection)
            {
                var lbl = detailT.GetComponent<UILabel>();
                if (!lbl.IsSafe()) continue;

                if (firstEffectLabel == null)
                {
                    // First effect plate — accumulate here
                    firstEffectLabel = lbl;
                    sb.Append(lbl.text ?? string.Empty);
                }
                else
                {
                    // Subsequent effect plate — merge text and hide
                    sb.Append(lbl.text ?? string.Empty);
                    plate.gameObject.SetActive(false);
                }
            }
        }

        // Flush final group — setting text triggers XUAT to re-translate with merged key
        if (inEffectSection && firstEffectLabel.IsSafe() && sb.Length > 0)
            firstEffectLabel.text = sb.ToString();
    }
    #endregion

    #region 4. Registry Control API
    public static void ClearCache()
    {
        lock (_syncLock)
        {
            OriginalStrings.Clear();
            TranslatedStrings.Clear();
            StoredSkillTexts.Clear();
        }
    }
    #endregion
}