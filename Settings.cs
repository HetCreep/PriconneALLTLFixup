#nullable enable
using BepInEx.Configuration;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PriconneALLTLFixup;

#region Plugin Metadata
public static class MyPluginInfo
{
    public const string Guid           = "com.github.hetcreep.priconnealltlfixup";
    public const string Name           = "PriconneALLTLFixup";
    public const string Author         = "HetCreep";
    public const string OriginalAuthor = "Dakari and Olegase";
    public const string Version        = "12.3.0";
    public const string HarmonyGuid    = Guid;
    public const string RepoUrl        = "https://github.com/HetCreep/PriconneALLTLFixup";
    public const string ProcessName    = "PrincessConnectReDive.exe";
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

    public string Section      { get; }
    public string Key          { get; }
    public T      DefaultValue { get; }
    public string Description  { get; }

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
    public Type? TargetPatch { get; }
    public PatchToggleSetting(string sec, string key, bool def, string desc, Type? patch = null)
        : base(sec, key, def, desc) { TargetPatch = patch; }

    public void Link(HarmonyPatchController controller)
    {
        if (TargetPatch == null || Entry == null) return;

        Action syncAction = () =>
        {
            if (Value)
            {
                controller.Patch(TargetPatch);
                FLog.Debug($"[Config] Patch Sync -> Enabled: {TargetPatch.Name}");
            }
            else
            {
                if (!ConfigManager.IsPatchRequired(TargetPatch))
                {
                    controller.Unpatch(TargetPatch);
                    FLog.Debug($"[Config] Patch Sync -> Disabled (Full): {TargetPatch.Name}");
                }
                else
                {
                    FLog.Debug($"[Config] Patch Sync -> Logical Off (Class remains active): {TargetPatch.Name}");
                }
            }
        };

        syncAction();
        Entry.SettingChanged += (s, e) => syncAction();
    }
}
#endregion

public static class ConfigManager
{
    private static readonly List<ISetting> _registry = new(32);
    private static readonly object         _regLock  = new();
    public  static event    Action?        OnChanged;

    internal static void InternalRegister(ISetting s) { lock (_regLock) _registry.Add(s); }
    internal static void NotifyChanged()               => OnChanged?.Invoke();

    // =========================================================================
    // PHASE 1: Translation Engine
    // =========================================================================

    #region Phase 1: Translation Settings
    public static class Translation
    {
        private const string S = "1. Translation Engine";

        /// <summary>ISO 639-1 language code used to select the asset directory and culture.</summary>
        public static readonly ConfigSetting<string> Code = new(
            S, "LanguageCode", "",
            "ISO 639-1 language code (e.g. 'th', 'en', 'vi'). " +
            "Leave empty for automatic detection (3-tier fallback): " +
            "1) XUAT plugin reflection, " +
            "2) AutoTranslatorConfig.ini [General] Language=, " +
            "3) built-in default.");

        /// <summary>Enables the regex-based rich-text tag repair engine.</summary>
        public static readonly PatchToggleSetting TranslationRepair = new(
            S, "EnableTranslationRepair", true,
            "Repairs corrupted color/gradient/size Rich Text tags introduced by machine translation. " +
            "Uses Fastenshtein Levenshtein-distance fuzzy matching for tag recovery. " +
            "Also strips stray TMPro <color>/<size> tags that translation engines may inject into NGUI labels, " +
            "preventing literal '</color>' text from appearing in the game UI.",
            typeof(Patches.TranslationCorePatch));
    }
    #endregion

    // =========================================================================
    // PHASE 2: User Interface & Layout
    // =========================================================================

    #region Phase 2: UI Layout Settings
    public static class UI
    {
        private const string S = "2. User Interface";

        /// <summary>Enables the skill-description text consolidation system.</summary>
        public static readonly PatchToggleSetting SmartSkillLayout = new(
            S, "EnableSmartSkillLayout", true,
            "Merges split or redundant skill description lines into a single coherent paragraph " +
            "so XUAT receives and translates the full text as one unit.",
            typeof(Patches.TextRegistryPatch));

        /// <summary>Enables skill effect regex index building at startup.</summary>
        public static readonly PatchToggleSetting SkillEffectIndex = new(
            S, "EnableSkillEffectIndex", true,
            "Loads regex translation patterns from XUAT text files at startup for skill effect translation.",
            typeof(Patches.SkillMergeIndexPatch));

        /// <summary>Enables direct UILabel.set_text hook for skill effect translation.</summary>
        public static readonly PatchToggleSetting SkillEffectTranslate = new(
            S, "EnableSkillEffectTranslate", true,
            "Hooks UILabel.set_text to apply regex-based skill effect translations directly, bypassing XUAT cache.",
            typeof(Patches.SkillMergeSetTextPatch));

        /// <summary>Enables all general UI layout correction patches.</summary>
        public static readonly PatchToggleSetting UILayout = new(
            S, "EnableUILayout", true,
            "Enables UI layout patches that correct positional offsets, overflow methods, " +
            "label widths, and underline widths across menus, quest details, shop, profile cards, " +
            "and battle overlays for non-Japanese localisations.",
            typeof(Patches.UILayoutPatch));

        /// <summary>Enables dynamic speech-bubble and skill-bubble resizing.</summary>
        public static readonly PatchToggleSetting BubbleFixes = new(
            S, "EnableBubbleFixes", true,
            "Dynamically resizes battle skill bubbles, guildhouse speech balloons, " +
            "and SpineDrama text balloons to fit translated text that is longer than the Japanese original.",
            typeof(Patches.UIBubblePatch));

        /// <summary>Enables subtitle rendering fixes for movies and story scenes.</summary>
        public static readonly PatchToggleSetting SubtitleFixes = new(
            S, "EnableSubtitleFixes", true,
            "Fixes subtitle display for in-game movies and story scenes: " +
            "enables pre-translation pumping, adjusts overflow and font size for non-CJK scripts.",
            typeof(Patches.SubtitleSystemPatch));

        /// <summary>Enables multi-language search support in shop and item dialogs.</summary>
        public static readonly PatchToggleSetting MultiLangSearch = new(
            S, "EnableMultiLangSearch", true,
            "Replaces the Japanese-only item search logic with a culture-aware, " +
            "Unicode substring matcher that works correctly with translated item names " +
            "in any language (Thai, English, Vietnamese, etc.).",
            typeof(Patches.ShopSearchPatch));
    }
    #endregion

    // =========================================================================
    // PHASE 3: Visual & Font
    // =========================================================================

    #region Phase 3: Visual & Font Settings
    public static class Visual
    {
        private const string S = "3. Visual & Font";

        /// <summary>Enables the global font override system.</summary>
        public static readonly PatchToggleSetting UIFont = new(
            S, "EnableFontReplacement", true,
            "Overrides hardcoded Japanese game fonts globally using custom AssetBundles. " +
            "Applies 'font_base' by default; per-object rules are loaded from '_01.font.txt'. " +
            "Font path is resolved via 3-tier fallback: " +
            "configured LanguageCode → auto-scan all Translation subfolders → game default. " +
            "Falls back to the game's original font if no bundle is found.",
            typeof(Patches.UIComponentPatch));

        /// <summary>Enables the dynamic text overflow and word-wrap engine.</summary>
        public static readonly PatchToggleSetting UIUniversal = new(
            S, "EnableUIResizer", true,
            "Dynamically adjusts text overflow and word-wrapping based on rules in '_02.resize.txt'. " +
            "Handles Thai vowel stacking, CJK character width, and Latin expansion ratios.",
            typeof(Patches.UIComponentPatch));

        /// <summary>Enables thousands-separator formatting on all number displays.</summary>
        public static readonly PatchToggleSetting UINumbers = new(
            S, "EnableNumberFormatting", true,
            "Injects culture-aware thousands separators (e.g. '1,234,567') across the entire game UI: " +
            "HP gauges, damage numbers, currency, and rankings. LRU-cached for O(1) hot-path performance.",
            typeof(Patches.NumberComponentPatch));

        /// <summary>Enables story color-code stripping for translated scripts.</summary>
        public static readonly PatchToggleSetting StoryFixes = new(
            S, "EnableStoryFixes", true,
            "Enables story-engine patches: strips stray TMPro tags from NGUI story labels, " +
            "runs XUAT pre-translation pumps for both main and tutorial story managers, " +
            "and adjusts place-name label layout for non-Japanese text.",
            typeof(Patches.StorySystemPatch));

        /// <summary>Enables custom Atlas/Sprite Sheet redirect support.</summary>
        public static readonly PatchToggleSetting AtlasRedirect = new(
            S, "EnableAtlasRedirect", true,
            "Loads custom PNG+JSON atlas pairs from 'Translation/<lang>/Other/atlases/' " +
            "and redirects UISprite lookups to the fixup atlas when available.",
            typeof(Patches.SpriteAtlasPatch));

        /// <summary>Enables dumping of original game atlases for modding reference.</summary>
        public static readonly PatchToggleSetting EnableTextureDumping = new(
            S, "EnableTextureDumping", false,
            "Dumps original game atlas sprites to JSON files for use as replacement templates. " +
            "Disable in production — intended for mod development only.");

        /// <summary>Custom dump output path; empty uses the default translation folder.</summary>
        public static readonly ConfigSetting<string> TexturesDumpPath = new(
            S, "TexturesDumpPath", string.Empty,
            "Output folder for atlas JSON dumps. " +
            "Leave empty to use 'Translation/<lang>/Other/atlases/' (default).");
    }
    #endregion

    // =========================================================================
    // PHASE 4: Gameplay Features
    // =========================================================================

    #region Phase 4: Gameplay Settings
    public static class Gameplay
    {
        private const string S = "4. Gameplay Features";

        /// <summary>Placeholder for future story enhancement patches.</summary>
        public static readonly PatchToggleSetting StoryEnhancement = new(
            S, "EnableStoryEnhancement", false,
            "[Reserved] Story/Tutorial enhancement patches — not active in this build.");

        /// <summary>Enables the Birthday Voice every-day override.</summary>
        public static readonly PatchToggleSetting BirthdayEveryday = new(
            S, "EnableBirthdayEveryday", false,
            "Sets the player's birthday to today so Birthday Voice lines play every day. " +
            "Requires a game restart to take effect.",
            typeof(Patches.GameplayQoLPatch));

        /// <summary>Enables auto-focus of the search field when opening unit search.</summary>
        public static readonly PatchToggleSetting AutoFocusSearch = new(
            S, "EnableAutoFocusSearch", true,
            "Automatically focuses the search input field when opening the unit search dialog, " +
            "allowing immediate typing without a manual click.",
            typeof(Patches.GameplayQoLPatch));

        /// <summary>Enables the Sugoi process lifecycle manager.</summary>
        public static readonly PatchToggleSetting SugoiCleanup = new(
            S, "EnableSugoiCleanup", true,
            "Captures the Sugoi offline translator process handle at startup and terminates it " +
            "cleanly on game exit to prevent orphaned background processes.",
            typeof(Patches.SugoiExitPatch));
    }
    #endregion

    // =========================================================================
    // PHASE 5: System Core
    // =========================================================================

    #region Phase 5: Core System Settings
    public static class Core
    {
        private const string S = "5. System Core";

        /// <summary>Enables verbose FLog.Debug output for developers.</summary>
        public static readonly PatchToggleSetting DebugMode = new(
            S, "DeveloperLogs", false,
            "Enables verbose diagnostic logging (FLog.Debug) with call-site attribution " +
            "[File@Line], stack traces, and performance profiling. " +
            "Disable in production — generates significant log volume.");

        /// <summary>Read-only field recording the current mod version.</summary>
        public static readonly ConfigSetting<string> Version = new(
            S, "ModVersion", MyPluginInfo.Version,
            "Current mod version — read-only reference field written at startup.");

        /// <summary>Read-only field linking to the project repository.</summary>
        public static readonly ConfigSetting<string> RepoUrl = new(
            S, "RepositoryUrl", MyPluginInfo.RepoUrl,
            "Project repository URL — read-only reference field.");

        /// <summary>Enables Win32 OS integration: window styles, hotkeys, drag-and-drop.</summary>
        public static readonly PatchToggleSetting SystemIntegration = new(
            S, "EnableSystemEnvironment", true,
            "Enables Windows OS-level integration: " +
            "restores the maximize button and resize handles (Win32 WS_MAXIMIZEBOX / WS_THICKFRAME), " +
            "enables drag-and-drop (WS_EX_ACCEPTFILES), " +
            "and registers F11 / Alt+Enter fullscreen toggle hotkeys.",
            typeof(Patches.WindowSystemPatch));

        /// <summary>Preferred display/fullscreen mode applied at startup.</summary>
        public static readonly ConfigSetting<FullScreenMode> DisplayMode = new(
            S, "DisplayMode", FullScreenMode.FullScreenWindow,
            "Target window mode used when pressing F11 or Alt+Enter to toggle fullscreen. " +
            "0 = FullScreen (exclusive), 1 = Borderless Windowed, 2 = Maximized, 3 = Windowed. " +
            "Note: this setting does NOT change the window on startup. " +
            "The game boots with its own display settings and this mod never forces a resize at launch.");

        /// <summary>Enables the XUAT telemetry sync bridge.</summary>
        public static readonly PatchToggleSetting TranslatorIntegration = new(
            S, "EnableTranslatorSync", true,
            "Synchronises language code, delay timing, and endpoint state between this mod " +
            "and XUnity.AutoTranslator at startup. " +
            "Disable only if experiencing conflicts with a custom XUAT build.",
            typeof(Patches.EngineBridgePatch));

        /// <summary>Enables the XUAT resize-call safety guard.</summary>
        public static readonly PatchToggleSetting UIStabilityGuard = new(
            S, "EnableStabilityGuard", true,
            "Intercepts XUAT's ResizeUI calls and validates each target component " +
            "with IsSafe() and IsTextElement() before proceeding. " +
            "Prevents null-pointer crashes during scene transitions. " +
            "Strongly recommended — disable only for advanced debugging.",
            typeof(Patches.TextSafetyPatch));

        // Note: HarmonyX IL2CPP assembly-scan warnings (GetTypesFromAssembly) are suppressed
        // automatically by LogFilter, which wraps BepInEx's DiskLogListener at startup.
        // This is a known IL2CPP environment limitation and does not affect functionality.
    }
    #endregion

    // =========================================================================
    // Infrastructure
    // =========================================================================

    public static bool IsPatchRequired(Type patchType)
    {
        if (patchType == null) return false;
        foreach (var s in _registry)
        {
            if (s is PatchToggleSetting toggle &&
                toggle.TargetPatch == patchType &&
                toggle.Value)
                return true;
        }
        return false;
    }

    #region Flow Control
    public static void Initialize(ConfigFile config)
    {
        FLog.Info("[Config] Syncing configuration schema...");

        var groups = typeof(ConfigManager).GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
        foreach (var group in groups)
            RuntimeHelpers.RunClassConstructor(group.TypeHandle);

        config.SaveOnConfigSet = false;
        foreach (var s in _registry) s.Bind(config);
        config.Save();
        config.SaveOnConfigSet = true;

        FLog.Info($"[Config] {_registry.Count} parameters loaded and saved to disk.");
    }

    public static void SynchronizePatches(HarmonyPatchController controller)
    {
        FLog.Info("[Config] Synchronizing patch toggles...");
        int enabled = 0, disabled = 0;
        foreach (var s in _registry)
        {
            if (s is PatchToggleSetting toggle)
            {
                toggle.Link(controller);
                if (toggle.Value) enabled++; else disabled++;
            }
        }
        FLog.Info($"[Config] Patches synchronized: {enabled} enabled, {disabled} disabled.");
    }
    #endregion
}