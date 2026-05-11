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

    #region 3. Module B: Skill Layout — Merge effect UILabels via FindObjectsOfType
    // PrefixSkillInit: no-op — all IL2CPP List<ValueTuple> access crashes natively
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), nameof(PartsUnitSkillDetailTextController.Initialize))]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillInit(
        Il2CppSystem.Collections.Generic.List<ValueTuple<PartsUnitSkillDetailTextPlate.ePlateType, string>> _detailTextList)
        => _ = _detailTextList;

    /// <summary>
    /// Postfix on Initialize — uses FindObjectsOfType&lt;PartsUnitSkillDetailTextPlate&gt;()
    /// to avoid accessing controller.transform (which crashes natively).
    /// Plates ARE MonoBehaviours with safe .transform access.
    /// Merges consecutive effect-section DetailLabel texts, sets combined JP text on first
    /// label → XUAT translates using the concatenated key from the translation files.
    /// </summary>
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), nameof(PartsUnitSkillDetailTextController.Initialize))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSkillControllerInit(PartsUnitSkillDetailTextController __instance)
    {
        if (!ConfigManager.Core.TranslatorIntegration.Value || !ConfigManager.UI.SmartSkillLayout.Value) return;

        TranslatedStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string effectHeaderTL);

        var plates = Object.FindObjectsOfType<PartsUnitSkillDetailTextPlate>();
        if (plates == null || plates.Length == 0) return;

        bool    inEffect         = false;
        UILabel firstEffectLabel = null;
        var     sb               = new StringBuilder();

        foreach (var plate in plates)
        {
            if (!plate.IsSafe()) continue;

            var titleT  = plate.transform.Find("TitleLabel");
            var detailT = plate.transform.Find("DetailLabel");

            bool hasTitle  = titleT.IsSafe()  && titleT.gameObject.activeSelf;
            bool hasDetail = detailT.IsSafe() && detailT.gameObject.activeSelf;

            if (!hasTitle && !hasDetail) continue;

            if (hasTitle)
            {
                if (inEffect && firstEffectLabel.IsSafe() && sb.Length > 0)
                { firstEffectLabel.text = sb.ToString(); sb.Clear(); firstEffectLabel = null; }

                inEffect = false;
                var hdr  = titleT.GetComponent<UILabel>();
                if (hdr.IsSafe())
                {
                    var t = hdr.text ?? string.Empty;
                    if (t == "スキル効果" || t == effectHeaderTL
                        || t.IndexOf("Skill effect", StringComparison.OrdinalIgnoreCase) >= 0)
                        inEffect = true;
                }
            }
            else if (hasDetail && inEffect)
            {
                var lbl = detailT.GetComponent<UILabel>();
                if (!lbl.IsSafe()) continue;
                string txt = (lbl.text ?? string.Empty).Replace("\n", "");

                if (firstEffectLabel == null) { firstEffectLabel = lbl; sb.Append(txt); }
                else                          { sb.Append(txt); lbl.text = string.Empty; }
            }
        }

        if (inEffect && firstEffectLabel.IsSafe() && sb.Length > 0)
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