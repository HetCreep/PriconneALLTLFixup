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

    internal static readonly Dictionary<eTextId, string> OriginalStrings = new();
    internal static readonly Dictionary<eTextId, string> TranslatedStrings = new();
    internal static readonly List<ProcessedItem> StoredSkillTexts = new();

    private const eTextId SKILL_EFFECT_HEADER_ID = (eTextId)10101004;

    internal struct ProcessedItem
    {
        public PartsUnitSkillDetailTextPlate.ePlateType PlateType;
        public int GroupId;
        public string Text;

        public ProcessedItem(PartsUnitSkillDetailTextPlate.ePlateType plateType, string text, int groupId)
        {
            PlateType = plateType;
            Text = text;
            GroupId = groupId;
        }
    }
    #endregion

    #region 2. Module A: Global Text Registry (The Memory Injector)
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

                            OriginalStrings[textId] = dict[textId];
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

    #region 3. Module B: Smart Skill Layout (Contextual Refactoring)
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), nameof(PartsUnitSkillDetailTextController.Initialize))]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillInit(Il2CppSystem.Collections.Generic.List<ValueTuple<PartsUnitSkillDetailTextPlate.ePlateType, string>> _detailTextList)
    {
        if (!ConfigManager.Core.TranslatorIntegration.Value || !ConfigManager.UI.SmartSkillLayout.Value) return;
        if (!_detailTextList.IsSafe()) return;

        lock (_syncLock)
        {
            StoredSkillTexts.Clear();
            if (_detailTextList.Count == 0) return;

            TranslatedStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string targetHeader);
            OriginalStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string originalHeader);

            bool isEffectGroup = false;
            int sequenceCount = 0;

            // KEY FIX: _detailTextList.ToArray()[i] converts IL2CPP List to managed array
            // on each iteration (same pattern as PriconneTLFixup). Direct get_Item[i] on
            // IL2CPP List<ValueTuple<enum,string>> causes ArgumentOutOfRangeException.
            // RemoveAt(i) + i-- is safe because we re-read via ToArray() next iteration.
            for (int i = 0; i < _detailTextList.Count; i++)
            {
                var item = _detailTextList.ToArray()[i];
                var plateType = item.Item1;
                var content   = item.Item2;

                sequenceCount++;

                if (content == "スキル効果" || content == targetHeader || content == originalHeader)
                {
                    sequenceCount = 1;
                    isEffectGroup = true;
                }

                if (sequenceCount > 2 && StoredSkillTexts.Count > 0)
                {
                    var last = StoredSkillTexts[StoredSkillTexts.Count - 1];
                    last.Text += content;
                    StoredSkillTexts[StoredSkillTexts.Count - 1] = last;

                    _detailTextList.RemoveAt(i);
                    i--;
                }
                else
                {
                    StoredSkillTexts.Add(new ProcessedItem(plateType, content, isEffectGroup ? 1 : 0));
                }
            }
        }
    }

    // Postfix on PartsDialogUnitSkillDetail.display — runs AFTER the popup is shown.
    // Finds the first effect DetailLabel and sets its text to the merged original JP text.
    // This triggers XUAT's UILabel hook → XUAT finds the merged translation key → translated.
    [HarmonyPatch(typeof(PartsDialogUnitSkillDetail), "display")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixSkillDisplay(PartsDialogUnitSkillDetail __instance)
    {
        if (!ConfigManager.Core.TranslatorIntegration.Value || !ConfigManager.UI.SmartSkillLayout.Value) return;
        if (!__instance.IsSafe()) return;

        lock (_syncLock)
        {
            if (StoredSkillTexts.Count == 0) return;

            var root = ((UnityEngine.Component)__instance).transform
                .Find("ScrollContent/ScrollView/WrapContent");
            if (!root.IsSafe()) return;

            var queue = new Queue<ProcessedItem>(StoredSkillTexts);
            var sb    = new StringBuilder();
            UILabel firstEffectLabel = null;

            for (int i = 0; i < root.childCount; i++)
            {
                var plate = root.GetChild(i);
                if (!plate.IsSafe() || !plate.gameObject.activeSelf) continue;

                var titleT  = plate.Find("TitleLabel");
                var detailT = plate.Find("DetailLabel");

                bool hasTitle  = titleT.IsSafe()  && titleT.gameObject.activeSelf;
                bool hasDetail = detailT.IsSafe() && detailT.gameObject.activeSelf;

                if (!hasTitle && !hasDetail) continue;
                if (queue.Count == 0) break;

                var stored = queue.Dequeue();

                if (hasTitle)
                {
                    // Header plate — flush pending effect text first
                    if (firstEffectLabel != null && sb.Length > 0)
                    {
                        firstEffectLabel.text = sb.ToString();
                        sb.Clear();
                        firstEffectLabel = null;
                    }
                }
                else if (hasDetail)
                {
                    var lbl = detailT.GetComponent<UILabel>();
                    if (!lbl.IsSafe()) continue;

                    if (stored.GroupId == 1) // effect group
                    {
                        if (firstEffectLabel == null)
                        {
                            firstEffectLabel = lbl;
                            sb.Append(stored.Text);
                        }
                        else
                        {
                            sb.Append(stored.Text);
                        }
                    }
                }
            }

            // Flush last group
            if (firstEffectLabel != null && sb.Length > 0)
                firstEffectLabel.text = sb.ToString();
        }
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