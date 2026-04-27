using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
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

public class Setting<T> : ISetting
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

    public Setting(string section, string key, T defaultValue, string desc)
    {
        Section = section; Key = key; DefaultValue = defaultValue; Description = desc;
        ConfigurationManager.InternalRegister(this);
    }

    public virtual void Bind(ConfigFile config)
    {
        Entry = config.Bind(Section, Key, DefaultValue, new ConfigDescription(Description));
        Entry.SettingChanged += (s, e) => ConfigurationManager.NotifyChanged();
    }
}

public class ToggleSetting : Setting<bool>
{
    public Type TargetPatch { get; }
    public ToggleSetting(string sec, string key, bool def, string desc, Type patch = null)
        : base(sec, key, def, desc) { TargetPatch = patch; }

    public void Link(HarmonyPatchController controller)
    {
        if (TargetPatch == null || Entry == null) return;
        Entry.SettingChanged += (s, e) => {
            if (Value) controller.Patch(TargetPatch); else controller.Unpatch(TargetPatch);
        };
        if (!Value) controller.Unpatch(TargetPatch);
    }
}
#endregion

public static class ConfigurationManager
{
    private static readonly List<ISetting> _registry = new(16);
    public static event Action OnChanged;

    internal static void InternalRegister(ISetting s) => _registry.Add(s);
    internal static void NotifyChanged() => OnChanged?.Invoke();

    #region 1. Official Config Groups
    public static class Translation
    {
        private const string S = "1. Translation Engine";

        public static readonly Setting<string> Code = new(
            S, "LanguageCode", "en", "ISO 639-1 Code");

        public static readonly ToggleSetting TranslationRepair = new(
            S, "EnableTranslationRepair", true,
            "เปิดใช้งานการซ่อมแซม Tag สีและ Gradient ที่เสียหายจากการแปลอัตโนมัติ",
            typeof(Patches.TranslationCorePatch));

    }

    public static class UI
    {
        private const string S = "2. User Interface";

        public static readonly ToggleSetting SmartSkillLayout = new(
            S, "EnableSmartSkillLayout", true,
            "เปิดใช้งานการจัดกลุ่มข้อความสกิลให้อ่านง่ายขึ้น (ยุบบรรทัดที่ซ้ำซ้อน)",
            typeof(Patches.TextRegistryPatch));
    }


    public static class Gameplay
    {
        private const string S = "3. Gameplay Features";


    }

    public static class Core
    {
        private const string S = "4. System Core";

        public static readonly ToggleSetting DebugMode = new(
            S,"DeveloperLogs", false, "เปิดการบันทึก Log เชิงลึกสำหรับนักพัฒนา");

        public static readonly Setting<string> Version = new(
            S, "ModVersion", MyPluginInfo.Version, "ข้อมูลเวอร์ชันปัจจุบัน");

        public static readonly ToggleSetting SystemIntegration = new(
            S, "EnableSystemEnvironment", false,
            "เปิดใช้งานการปรับแต่งหน้าต่างและคีย์ลัด F11 หรือ Alt+Enter",
            typeof(Patches.WindowCorePatch)
        );

        public static readonly Setting<FullScreenMode> DisplayMode = new(
            S, "DisplayMode", FullScreenMode.FullScreenWindow,
            "โหมดการแสดงผลที่ต้องการตามรูปภาพที่คุ้นตา: \n0 = FullScreen,\n1 = Window Borderless,\n2 = MaximizedWindow (For some OS)),\n3 = Windowed"
        );

        public static readonly ToggleSetting TranslatorIntegration = new(
            S, "EnableTranslatorSync", true,
        "เปิดใช้งานการซิงค์รหัสภาษากับ XUnity.AutoTranslator",
        typeof(Patches.EngineBridgePatch)
        );
    }
    #endregion

    #region 2. Flow Control
    public static void Initialize(ConfigFile config)
    {
        Log.Info("[Config] Syncing configuration schema...");

        var groups = typeof(ConfigurationManager).GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
        foreach (var group in groups) RuntimeHelpers.RunClassConstructor(group.TypeHandle);

        config.SaveOnConfigSet = true;
        foreach (var s in _registry) s.Bind(config);

        Log.Info($"[Config] Successfully loaded {_registry.Count} parameters.");
    }

    public static void SynchronizePatches(HarmonyPatchController controller)
    {
        foreach (var s in _registry)
        {
            if (s is ToggleSetting toggle) toggle.Link(controller);
        }
    }
    #endregion
}