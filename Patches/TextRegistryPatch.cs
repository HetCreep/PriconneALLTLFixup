using BepInEx;
using Elements;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

    #region 3. Module B: Skill Layout — Merge UILabel Texts for XUAT
    // PrefixSkillInit: intentional no-op (IL2CPP List<ValueTuple> cannot be safely accessed)
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), nameof(PartsUnitSkillDetailTextController.Initialize))]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillInit(
        Il2CppSystem.Collections.Generic.List<ValueTuple<PartsUnitSkillDetailTextPlate.ePlateType, string>> _detailTextList)
        => _ = _detailTextList;

    /// <summary>
    /// Postfix on PartsUnitSkillDetailTextController.Initialize — runs after all effect plates
    /// are created. Traverses the controller's child transforms, collects all "Skill effect"
    /// section DetailLabel texts, concatenates them (matching the translation file key format),
    /// and sets the merged JP text on the first effect label. XUAT then finds the combined
    /// translation key and applies the English text.
    /// Uses PartsUnitSkillDetailTextController (proven-safe __instance) NOT PartsDialogUnitSkillDetail.
    /// </summary>
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), nameof(PartsUnitSkillDetailTextController.Initialize))]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSkillControllerInit(PartsUnitSkillDetailTextController __instance)
    {
        if (!ConfigManager.Core.TranslatorIntegration.Value || !ConfigManager.UI.SmartSkillLayout.Value) return;
        if (!__instance.IsSafe()) return;

        TranslatedStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string effectHeaderTL);

        var root = __instance.transform;
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
                // Flush previous group before new section
                if (inEffectSection && firstEffectLabel.IsSafe() && sb.Length > 0)
                {
                    firstEffectLabel.text = sb.ToString();
                    sb.Clear(); firstEffectLabel = null;
                }
                inEffectSection = false;

                var hdr = titleT.GetComponent<UILabel>();
                if (hdr.IsSafe())
                {
                    var txt = hdr.text ?? string.Empty;
                    if (txt == "スキル効果"
                        || txt == effectHeaderTL
                        || txt.IndexOf("Skill effect", StringComparison.OrdinalIgnoreCase) >= 0)
                        inEffectSection = true;
                }
            }
            else if (hasDetail && inEffectSection)
            {
                var lbl = detailT.GetComponent<UILabel>();
                if (!lbl.IsSafe()) continue;

                string labelText = (lbl.text ?? string.Empty).Replace("\n", "");

                if (firstEffectLabel == null)
                {
                    firstEffectLabel = lbl;
                    sb.Append(labelText);
                }
                else
                {
                    sb.Append(labelText);
                    plate.gameObject.SetActive(false); // hide merged plate
                }
            }
        }

        // Final flush — triggers XUAT to translate with combined key
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