using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using Elements;
using HarmonyLib;
using PriconneALLTLFixup;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class TextRegistryPatch
{
    #region 1. Internal Models & State
    private static readonly object _syncLock = new();

    internal static readonly Dictionary<eTextId, string> OriginalStrings = new();
    internal static readonly Dictionary<eTextId, string> TranslatedStrings = new();

    internal static readonly List<ProcessedItem> StoredSkillTexts = new();

    private static string GetOtherFilePath(string fileName) => Path.Combine(
        Paths.BepInExRootPath, "Translation",
        ConfigurationManager.Translation.Code.Value,
        "Other", fileName);

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

    #region 2. Module A: Global Text Registry (ConstTextData)
    [HarmonyPatch(typeof(ConstTextData), "CreateInstanceAndLoadInitialize")]
    [HarmonyPostfix]
    [HarmonyWrapSafe]
    public static void PostfixLoadConstText()
    {
        string path = GetOtherFilePath("text_id.txt");
        if (!File.Exists(path)) return;

        var instance = Singleton<ConstTextData>.Instance;
        if (!Util.IsSafe(instance) || instance.scriptableObject == null) return;

        var scriptableObject = instance.scriptableObject;

        try
        {
            lock (_syncLock)
            {
                foreach (var line in File.ReadLines(path))
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2 || string.IsNullOrEmpty(parts[1])) continue;

                    if (Enum.TryParse<eTextId>(parts[0], out var textId))
                    {
                        OriginalStrings[textId] = scriptableObject.DataDictionary[textId];

                        string sanitizedVal = parts[1].Sanitize();
                        TranslatedStrings[textId] = sanitizedVal;

                        scriptableObject.DataDictionary[textId] = sanitizedVal;
                    }
                }
            }
            Log.Info($"[Registry] Global Text ID Mapping complete. ({TranslatedStrings.Count} items)");
        }
        catch (Exception ex) { Log.Error("ConstText mapping failed", ex); }
    }
    #endregion

    #region 3. Module B: Contextual Skill Storage
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), "Initialize")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillInit(Il2CppSystem.Collections.Generic.List<ValueTuple<PartsUnitSkillDetailTextPlate.ePlateType, string>> _detailTextList)
    {
        if (!ConfigurationManager.Core.TranslatorIntegration.Value ||
            !ConfigurationManager.UI.SmartSkillLayout.Value) return;

        if (!Util.IsSafe(_detailTextList)) return;

        lock (_syncLock)
        {
            StoredSkillTexts.Clear();
            bool isEffectGroup = false;
            int sequenceCount = 0;

            for (int i = 0; i < _detailTextList.Count; i++)
            {
                sequenceCount++;
                var item = _detailTextList[i];
                string content = item.Item2;

                if (content == "スキル効果") { sequenceCount = 1; isEffectGroup = true; }

                if (sequenceCount > 2 && StoredSkillTexts.Count > 0)
                {
                    var lastIdx = StoredSkillTexts.Count - 1;
                    var mergedItem = StoredSkillTexts[lastIdx];
                    mergedItem.Text += content;
                    StoredSkillTexts[lastIdx] = mergedItem;

                    _detailTextList.RemoveAt(i);
                    i--;
                }
                else
                {
                    StoredSkillTexts.Add(new ProcessedItem(item.Item1, content, isEffectGroup ? 1 : 0));
                }
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
            Log.Debug("[Registry] All text registries purged.");
        }
    }
    #endregion
}