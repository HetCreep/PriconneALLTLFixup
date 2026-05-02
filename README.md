# PriconneALLTLFixup (Master Framework Edition)

An advanced performance optimization, visual enhancement, and localization repair framework for **Princess Connect! Re:Dive**. This project is engineered with enterprise-grade standards to provide a high-quality, stable, and seamless translation experience for Global and Private Server environments.

> **Heritage & Evolution**: This framework is a modern, highly optimized reconstruction based on the foundational work and original concepts created by **Dakari** and **Olegase**. It has been evolved into a "Master Framework" by **HetCreep** in collaboration with **AI (Gemini)**.

---

> [!CAUTION]
> **XUnity.AutoTranslator (XUAT) is REQUIRED.**
> This framework is designed to work as an intelligence and visual enhancement layer for XUAT. If XUAT is not detected, the mod will safely abort its initialization to ensure game stability.

---

![GitHub release (latest by date)](https://img.shields.io/github/v/release/HetCreep/PriconneALLTLFixup)
![GitHub License](https://img.shields.io/github/license/HetCreep/PriconneALLTLFixup)
![Platform](https://img.shields.io/badge/platform-PC%20%7C%20Unity-blue)

---

## 🚀 Tech Stack & Dependencies

### Core Infrastructure
- **BepInEx 6 (IL2CPP)**: The modern standard for Unity modding, providing the high-performance execution environment required for Princess Connect.
- **HarmonyX**: A powerful runtime bytecode manipulation engine used for non-destructive patching of the game's internal methods.
- **Win32 API Bridge**: Native C# P/Invoke integration for direct control over Windows OS-level window styles, transparency, and state management.

### Mandatory Dependency
- **XUnity.AutoTranslator (XUAT)**: **[REQUIRED]** This mod functions as an essential "Intelligence Bridge" for XUAT. It enhances XUAT's capabilities and fixes its limitations. The mod will safely abort initialization if XUAT is not detected in the environment.

### Optimization Algorithms
- **Fastenshtein.dll**: A high-speed, low-allocation implementation of the Levenshtein distance algorithm. It is utilized within the Tech Stack to perform near-instantaneous string similarity analysis for optimized text matching (Rule 1).
- **Advanced .NET Features**: Leveraging `ReadOnlySpan<char>`, `StringBuilder` reuse, `AggressiveInlining`, and `Generic Type Constraints` to minimize CPU cycles and memory pressure.

---

## ✨ Key Features & Technical Capabilities

### 1. Smart Localization Engine (Dual-Mode Sync)
- **Intelligent Path Redirection**: The mod determines its resource directory (`BepInEx\Translation\{ISO-639-1}`) using a sophisticated priority system.
    - **Manual Mode**: You can explicitly set a language code in the config to force the mod to load specific assets (e.g., forcing Thai fonts and layout rules).
    - **Smart Fallback**: If the config is left empty, the mod's bridge automatically queries the XUAT engine to detect the active translation target and synchronizes the path accordingly.
- **De-coupled Execution**: A unique architecture that allows the mod's visual assets (Fonts/Layouts) to run in one language while XUAT translates the text into another, ensuring zero internal conflicts or logic loops.

### 2. Visual & Typography Mastery
- **Universal Font Redirection**: Globally overrides hardcoded Japanese game fonts using custom AssetBundles. It applies a "font_base" by default and allows per-object overrides.
- **Adaptive UI Resizer**: A real-time layout engine that calculates character width and word-wrapping. It specifically handles linguistic complexities like Thai vowel stacking (Sara/Wannayuk) and the expansion of Spanish/English text relative to the original Japanese.

### 3. Data Integrity & Global Search
- **Multi-Language Search Support**: Powered by the critical `text_id.txt` mapping. This feature unlocks the ability to search for characters, items, or equipment using their translated names (English/Thai/etc.), successfully bypassing the game's default logic which is hardcoded to Japanese-only search keys.
- **High-Performance Number Formatting**: Dynamically injects standard thousands separators (`,`) across the entire game UI. This includes critical combat elements like HP gauges, damage numbers, and currency counts, all processed through an optimized cache.

---

## 🖼️ Visual Preview
*(Note: Replace these placeholders with your actual screenshot URLs)*
| Feature | Before | After |
| :--- | :---: | :---: |
| **Number Formatting** | 1234567 | 1,234,567 |
| **Universal Font** | Default Japanese Font | High-Quality Custom Font |
| **Adaptive UI** | Text Overflowing | Perfectly Scaled UI |

---

## 🛠️ Installation

1.  **Requirements**: Ensure you have [BepInEx 6 (IL2CPP)](https://github.com/BepInEx/BepInEx) and [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) installed and working.
2.  **Download**: Get the latest `PriconneALLTLFixup.dll` from the [Releases](https://github.com/HetCreep/PriconneALLTLFixup/releases) page.
3.  **Deployment**: Copy the `.dll` file into your game's plugin folder: `BepInEx\plugins\`.
4.  **First Run**: Launch the game once to generate the default configuration at `BepInEx\config\PriconneALLTLFixup.cfg`.
5.  **Assets Setup**: Place your custom fonts and mapping files in the corresponding language folder: `BepInEx\Translation\{LanguageCode}\Font\` and `Other\`.

---

## 🛠️ Critical File Structure

The mod organizes its intelligence and assets within the `BepInEx\Translation\{ISO-639-1}\` directory structure:

| Path | Detailed Purpose |
| :--- | :--- |
| `Font\font_base.unity3d` | **Global Default**: The primary high-quality font bundle applied to all text elements unless specified otherwise. |
| `Font\*.unity3d` | **Specialized Fonts**: Additional AssetBundles for specific artistic UI needs (e.g., Bold, Handwriting). |
| `Other\_01.font.txt` | **Mapping Rules**: Defines which GameObjects or Hierarchy Paths use specific font bundles (Supports Wildcards `*`). |
| `Other\_02.resize.txt` | **Layout Boundaries**: Defines maximum width limits and overflow methods (ResizeHeight/ShrinkContent) for dynamic UI. |
| `Other\text_id.txt` | **Core Registry**: Maps internal `eTextId` enums to localized strings. This is the key to enabling the **Multi-Language Search** feature. |

---

## ⚙️ Modular Configuration (PriconneALLTLFixup.cfg)

The configuration schema provides granular control over every optimization layer:

### [1. Translation Engine]
- **LanguageCode**: Specify the target **ISO 639-1 Code** (e.g., `th`, `en`, `vi`, `es`). Leave this field empty to enable the **Smart Fallback** system that automatically follows XUAT's target.
- **EnableTranslationRepair**: Toggles the high-performance regex engine that proactively repairs corrupted Rich Text tags (colors, gradients, and size tags) often damaged by translation engines.

### [2. User Interface]
- **EnableSmartSkillLayout**: Toggles the advanced contextual logic that merges repetitive or split skill description lines into a consolidated, easy-to-read format.

### [3. Visual & Font]
- **EnableFontReplacement**: Toggles the global font override system. When enabled, it prioritizes `font_base` and follows rules in `_01.font.txt`.
- **EnableUIResizer**: Toggles the dynamic layout engine that prevents text from overflowing UI boundaries based on `_02.resize.txt`.
- **EnableNumberFormatting**: Toggles standard thousands separators (`,`) for currency, damage values, and character HP gauges.

### [4. System Core]
- **EnableSystemEnvironment**: Manages deep Windows OS integration, controlling window styles, borderless states, and functional shortcuts (F11 / Alt+Enter).
- **DisplayMode**: Sets the preferred windowing behavior:
    - `0`: FullScreen
    - `1`: Borderless
    - `2`: **Maximized** (Crucial: This mode is required for specific Windows OS versions or builds to ensure correct display scaling).
    - `3`: Windowed
- **DeveloperLogs**: Enables verbose diagnostic logging, stack traces, and performance profiling for developer bottleneck analysis.
- **EnableTranslatorSync**: Toggles the internal synchronization bridge between this mod's state and the XUAT engine telemetry.

---

## 💎 The Master Framework 10 (Project Philosophies)

Every module in this project is built adhering to these 10 strict professional standards:

1.  **Strict Performance Focus**: All logic is zero-allocation in the main execution loop; heavy data is managed via high-performance static caches to ensure O(1) complexity.
2.  **Clean Code & Architecture**: Adheres to a strict **Separation of Concerns** between the translation bridge, visual engine, and core system modules across 4 phases.
3.  **Advanced C# Features**: Utilizes modern features such as `ReadOnlySpan<T>`, `Method Inlining`, and `Generic Constraints` to minimize CPU overhead.
4.  **Static Registry Pattern**: Centralized registration of patches and configuration parameters for instant, high-speed access during runtime.
5.  **Thread Safety**: Implements robust locking mechanisms (`lock`, `_syncRoot`) to ensure data integrity during multi-threaded localization tasks.
6.  **Comprehensive Logging**: A multi-tier diagnostic system that distinguishes between user information, warnings, and deep developer-only contexts.
7.  **Defensive Programming**: Pervasive use of `Util.IsSafe()` and integrity checks on Unity components to ensure 100% crash prevention.
8.  **Adaptive UI Logic**: Positions and scales UI elements dynamically by reacting to the linguistic properties and expansion ratios of the active language.
9.  **Minimal Boilerplate**: Consolidates redundant game hooks and boilerplate logic into unified, high-efficiency execution modules.
10. **Professional Documentation**: Provides enterprise-grade technical documentation and a transparent project structure for long-term maintainability.

---

## 👨‍💻 Development & Community

### Core Team
- **Original Foundations**: Concepts and legacy code by **Dakari** and **Olegase**.
- **Modern Maintenance**: Developed and re-engineered by **HetCreep** in collaboration with **AI Collaborator (Gemini)**.

### 📢 Join the Mission!
We are actively searching for talented developers with expertise in **C#**, **IL2CPP**, **Reverse Engineering**, or **UI/UX Design** to join our development efforts. If you are passionate about building the most advanced localization framework for the community, your contributions are more than welcome. Feel free to open a Pull Request or contact us via the repository!

---

## 🏗️ Building from Source

This project uses a specialized environment for building.
1. Clone the repository.
2. **Library Setup**: This project relies on local dependencies. Ensure you have the necessary DLLs in the `libs/` folder (organized into `core`, `interop`, and `plugins` subfolders) as defined in the project structure.
3. Open `PriconneALLTLFixup.sln` using **Visual Studio 2022**.
4. Set the build configuration to **Release**.
5. Build the solution. The automated **ILRepack** task will combine the main DLL with `Fastenshtein.dll` into a single high-performance package.

---

*Developed with ❤️ to bring the ultimate Princess Connect experience to the global community.*

---

## 📜 License
This project is licensed under the **MIT License** - feel free to use and improve it for the community!