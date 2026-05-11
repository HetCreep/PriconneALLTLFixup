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

            // IMPORTANT: IL2CPP List<ValueTuple<enum,string>> interop quirks:
            //   • foreach / GetEnumerator  → native crash (enumerator value-type marshal fails)
            //   • Clear() / Add()          → native crash (corrupts native list state)
            //   • get_Item[i]              → managed ArgumentOutOfRangeException (safe, catchable)
            //   • RemoveAt(i)              → managed exception (safe, catchable)
            // Strategy: indexed for loop with try-catch, deferred RemoveAt with try-catch.

            int count;
            try { count = _detailTextList.Count; }
            catch { return; }

            TranslatedStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string targetHeader);
            OriginalStrings.TryGetValue(SKILL_EFFECT_HEADER_ID, out string originalHeader);

            var toRemove = new System.Collections.Generic.List<int>();
            bool isEffectGroup = false;
            int sequenceCount = 0;

            for (int i = 0; i < count; i++)
            {
                PartsUnitSkillDetailTextPlate.ePlateType plateType;
                string content;
                try
                {
                    var item = _detailTextList[i];
                    plateType = item.Item1;
                    content   = item.Item2;
                }
                catch (Exception ex)
                {
                    FLog.Debug($"[Skill] get_Item({i}/{count}) failed: {ex.Message} — stopping early");
                    break; // abort cleanly; toRemove stays as-is
                }

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
                    toRemove.Add(i);
                }
                else
                {
                    StoredSkillTexts.Add(new ProcessedItem(plateType, content, isEffectGroup ? 1 : 0));
                }
            }

            // Deferred reverse removal — RemoveAt is a managed call (safe)
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