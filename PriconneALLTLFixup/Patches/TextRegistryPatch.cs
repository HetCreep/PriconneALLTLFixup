using BepInEx;
using Elements;
using HarmonyLib;

namespace PriconneALLTLFixup.Patches;

[HarmonyPatch]
public static class TextRegistryPatch
{
    #region 1. Internal Models & State Management
    private static readonly object _syncLock = new();

    internal static readonly Dictionary<eTextId, string> OriginalStrings = new();
    internal static readonly Dictionary<eTextId, string> TranslatedStrings = new();

    internal static readonly List<ProcessedItem> StoredSkillTexts = new();

    private static string GetOtherFilePath(string fileName) => Path.Combine(
        Paths.BepInExRootPath, "Translation",
        Util.GetXuatLanguage(),
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

        if (!File.Exists(path))
        {
            Log.Error($"[Registry] CRITICAL: 'text_id.txt' not found at {path}. Static text mapping is now ABORTED.");
            return;
        }

        var instance = Singleton<ConstTextData>.Instance;
        if (!Util.IsSafe(instance) || instance.scriptableObject == null) return;

        var scriptableObject = instance.scriptableObject;
        var dict = scriptableObject.DataDictionary;

        try
        {
            lock (_syncLock)
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;

                    string keyStr = parts[0].Trim();
                    string valStr = parts[1];

                    if (Enum.TryParse<eTextId>(keyStr, out var textId))
                    {
                        if (dict.ContainsKey(textId))
                        {
                            OriginalStrings[textId] = dict[textId];

                            string sanitizedVal = valStr.Sanitize();
                            TranslatedStrings[textId] = sanitizedVal;

                            dict[textId] = sanitizedVal;
                        }
                    }
                }
            }
            Log.Info($"[Registry] Static text mapping successfully loaded for: {Util.GetXuatLanguage()}");
        }
        catch (Exception ex)
        {
            Log.Error($"[Registry] Runtime error during text mapping: {ex.Message}");
        }
    }
    #endregion

    #region 3. Module B: Contextual Skill Storage
    [HarmonyPatch(typeof(PartsUnitSkillDetailTextController), "Initialize")]
    [HarmonyPrefix]
    [HarmonyWrapSafe]
    public static void PrefixSkillInit(Il2CppSystem.Collections.Generic.List<ValueTuple<PartsUnitSkillDetailTextPlate.ePlateType, string>> _detailTextList)
    {
        if (!ConfigManager.Core.TranslatorIntegration.Value ||
            !ConfigManager.UI.SmartSkillLayout.Value) return;

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