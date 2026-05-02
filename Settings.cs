using BepInEx.Configuration;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PriconneALLTLFixup;

#region Plugin Metadata
public static class MyPluginInfo
{
    public const string Guid = "PriconneALLTLFixup";
    public const string Name = "PriconneALLTLFixup by HetCreep";
    public const string Author = "HetCreep";
    public const string OriginalAuthor = "Dakari and Olegase";
    public const string Version = "12.2.0";
    public const string HarmonyGuid = "com.github.hetcreep.priconnealltlfixup";
    public const string RepoUrl = "https://github.com/HetCreep/PriconneALLTLFixup";
    public const string ProcessName = "PrincessConnectReDive.exe";
}
#endregion

#region Core Configuration Logic
public interface ISetting { void Bind(ConfigFile config); }

public class ConfigSetting<T> : ISetting
{
    public ConfigEntry<T> Entry { get; protected set; } = null!;

    public T Value
    {
        get => Entry != null ? Entry.Value : DefaultValue;
        set { if (Entry != null) Entry.Value = value; }
    }

    public string Section { get; }
    public string Key { get; }
    public T DefaultValue { get; }
    public string Description { get; }

    public ConfigSetting(string section, string key, T defaultValue, string desc)
    {
        Section = section; Key = key; DefaultValue = defaultValue; Description = desc;
        ConfigManager.InternalRegister(this);
    }

    public virtual void Bind(ConfigFile config)
    {
        Entry = config.Bind(Section, Key, DefaultValue, new ConfigDescription(Description));
        Entry.SettingChanged += (s, e) => ConfigManager.NotifyChanged();
    }
}

public class PatchToggleSetting : ConfigSetting<bool>
{
    public Type TargetPatch { get; }
    public PatchToggleSetting(string sec, string key, bool def, string desc, Type patch = null)
        : base(sec, key, def, desc) { TargetPatch = patch; }

    public void Link(HarmonyPatchController controller)
    {
        if (TargetPatch == null || Entry == null) return;
        Entry.SettingChanged += (s, e) =>
        {
            if (Value) controller.Patch(TargetPatch); else controller.Unpatch(TargetPatch);
        };
        if (!Value) controller.Unpatch(TargetPatch);
    }
}
#endregion

public static class ConfigManager
{
    private static readonly List<ISetting> _registry = new(16);
    public static event Action OnChanged;

    internal static void InternalRegister(ISetting s) => _registry.Add(s);
    internal static void NotifyChanged() => OnChanged?.Invoke();

    #region Phase 1: Translation Settings
    public static class Translation
    {
        private const string S = "1. Translation Engine";

        public static readonly ConfigSetting<string> Code = new(
            S, "LanguageCode", "en", "ISO 639-1 Code (รหัสภาษาหลักที่มอดจะอ้างอิง)");

        public static readonly PatchToggleSetting TranslationRepair = new(
            S, "EnableTranslationRepair", true,
            "เปิดใช้งานการซ่อมแซม Tag สีและ Gradient ที่เสียหายจากการแปลอัตโนมัติ",
            typeof(Patches.TranslationCorePatch));
    }
    #endregion

    #region Phase 2: Visual & UI Layout
    public static class UI
    {
        private const string S = "2. User Interface";

        public static readonly PatchToggleSetting SmartSkillLayout = new(
            S, "EnableSmartSkillLayout", true,
            "เปิดใช้งานการจัดกลุ่มข้อความสกิลให้อ่านง่ายขึ้น (ยุบบรรทัดที่ซ้ำซ้อน)",
            typeof(Patches.TextRegistryPatch));
    }

    public static class Visuals
    {
        private const string S = "3. Visuals & Font";

        public static readonly PatchToggleSetting UIFont = new(
            S, "EnableFontReplacement", true,
            "เปิดใช้งานการเปลี่ยนฟอนต์สากลตามกฎใน _01.font.txt (Failsafe: หากไม่พบไฟล์จะใช้ของเกมแทน)",
            typeof(Patches.UIComponentPatch));

        public static readonly PatchToggleSetting UIUniversal = new(
            S, "EnableUIResizer", true,
            "เปิดใช้งานระบบปรับขนาดและตัดคำอัตโนมัติตามกฎใน _02.resize.txt",
            typeof(Patches.UIComponentPatch));
    }
    #endregion

    #region Phase 3: Gameplay Settings
    public static class Gameplay
    {
        private const string S = "4. Gameplay Features";

    }
    #endregion

    #region Phase 4: Core System
    public static class Core
    {
        private const string S = "5. System Core";

        public static readonly PatchToggleSetting DebugMode = new(
            S, "DeveloperLogs", false, "เปิดการบันทึก Log เชิงลึกสำหรับนักพัฒนา (Verbose Logging)");

        public static readonly ConfigSetting<string> Version = new(
            S, "ModVersion", MyPluginInfo.Version, "ข้อมูลเวอร์ชันปัจจุบันของมอด");

        public static readonly PatchToggleSetting SystemIntegration = new(
            S, "EnableSystemEnvironment", true,
            "เปิดใช้งานการจัดการหน้าต่าง Windows และคีย์ลัด (F11/Alt+Enter)",
            typeof(Patches.WindowCorePatch));

        public static readonly ConfigSetting<FullScreenMode> DisplayMode = new(
            S, "DisplayMode", FullScreenMode.FullScreenWindow,
            "โหมดการแสดงผลที่ต้องการ: 0=FullScreen, 1=Borderless, 2=Maximized, 3=Windowed");

        public static readonly PatchToggleSetting TranslatorIntegration = new(
            S, "EnableTranslatorSync", true,
            "เปิดใช้งานการซิงค์รหัสภาษาและสถานะการแปลกับ XUnity.AutoTranslator",
            typeof(Patches.EngineBridgePatch));
    }
    #endregion

    #region 2. Flow Control
    public static void Initialize(ConfigFile config)
    {
        Log.Info("[Config] Syncing configuration schema...");

        var groups = typeof(ConfigManager).GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
        foreach (var group in groups) RuntimeHelpers.RunClassConstructor(group.TypeHandle);

        config.SaveOnConfigSet = true;
        foreach (var s in _registry) s.Bind(config);

        Log.Info($"[Config] Successfully loaded {_registry.Count} parameters.");
    }

    public static void SynchronizePatches(HarmonyPatchController controller)
    {
        foreach (var s in _registry)
        {
            if (s is PatchToggleSetting toggle) toggle.Link(controller);
        }
    }
    #endregion
}