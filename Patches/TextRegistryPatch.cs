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

            // ── Step 1: Snapshot via foreach (stable for IL2CPP List<ValueTuple>) ─
            // get_Item indexed access is unstable; foreach/enumerator is reliable.
            var snapshot = new System.Collections.Generic.List<(PartsUnitSkillDetailTextPlate.ePlateType type, string text)>();
            foreach (var il2Item in _detailTextList)
                snapshot.Add((il2Item.Item1, il2Item.Item2));

            if (snapshot.Count == 0) return;

            // ── Step 2: Determine merge groups in managed memory ─────────────────
            TranslatedStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string targetHeader);
            OriginalStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string originalHeader);

            var toRemove = new System.Collections.Generic.List<int>(); // IL2CPP list indices
            bool isEffectGroup = false;
            int sequenceCount = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                var (plateType, content) = snapshot[i];
                sequenceCount++;

                if (content == "スキル効果" || content == targetHeader || content == originalHeader)
                {
                    sequenceCount = 1;
                    isEffectGroup = true;
                }

                if (sequenceCount > 2 && StoredSkillTexts.Count > 0)
                {
                    var lastStored = StoredSkillTexts[StoredSkillTexts.Count - 1];
                    lastStored.Text += content;
                    StoredSkillTexts[StoredSkillTexts.Count - 1] = lastStored;
                    toRemove.Add(i);
                }
                else
                {
                    StoredSkillTexts.Add(new ProcessedItem(plateType, content, isEffectGroup ? 1 : 0));
                }
            }

            if (toRemove.Count == 0) return;

            // ── Step 3: Deferred RemoveAt in reverse (never Clear/Add — causes native crash) ─
            for (int j = toRemove.Count - 1; j >= 0; j--)
            {
                try { _detailTextList.RemoveAt(toRemove[j]); }
                catch (Exception ex) { FLog.Debug($"[Skill] RemoveAt({toRemove[j]}) failed: {ex.Message}"); }
            }
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