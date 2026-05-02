# PriconneALLTLFixup (Master Framework Edition)

An advanced performance optimization and localization repair framework for **Princess Connect! Re:Dive**, designed to work seamlessly with **XUnity.AutoTranslator** for Global and Private Server environments.

## 🚀 Key Features

### 1. Smart Localization Engine
- **Intelligent Language Fallback**: Automatically synchronizes with the XUAT engine. If no specific ISO 639-1 code is provided in the mod settings, it intelligently detects and follows the active translator's target language.
- **De-coupled Execution**: Supports running the mod in one language (e.g., `th` for Thai fonts) while the game text is translated into another (e.g., `en` for English text) without any internal conflicts.

### 2. Visual & Font Mastery
- **Universal Font Redirection**: Globally overrides game fonts using AssetBundles and external mapping rules. This allows for high-quality typography in languages not originally supported by the game.
- **Adaptive UI Resizer**: Real-time calculation of character sizing and word-wrapping based on the specific linguistic behavior of the target language (e.g., handling Thai vowel stacking or Spanish sentence length).

### 3. Data & Performance Optimization
- **High-Performance Number Formatting**: Implements localized number formatting (standard thousands separators) across all UI elements, including HP gauges and Story text, with zero frame-time impact.
- **Translation Repair & Sanitization**: Proactively fixes broken Rich Text tags (colors, gradients, and size tags) that are often damaged by auto-translation engines.
- **Smart Skill Layout**: Consolidates redundant skill description lines into a streamlined, readable format, preventing UI clutter in complex skill data.

---

## 🛠️ Critical File Structure

The mod organizes its resources within the `BepInEx\Translation\{ISO-639-1}\` directory. This structure allows the mod to be truly universal:

| Path | Purpose |
| :--- | :--- |
| `Font\` | Stores `.unity3d` AssetBundles for custom font redirection. |
| `Other\_01.font.txt` | Mapping rules defining which game objects use specific font assets. |
| `Other\_02.resize.txt` | Dynamic rules for UI width boundaries and overflow methods (Resize/Shrink). |
| `Other\text_id.txt` | **Critical**: Enables multi-language search functionality (e.g., searching characters by English/Thai names), bypassing the game's default hardcoded Japanese-only search logic. |

---

## ⚙️ Configuration (PriconneALLTLFixup.cfg)

The configuration schema is highly modular, allowing for precise control over every fix and optimization:

### [1. Translation Engine]
- **LanguageCode**: Specify the target **ISO 639-1 Code** (e.g., `th`, `en`, `vi`, `es`). Leave this empty to enable the **Smart Fallback** system which follows XUAT.
- **EnableTranslationRepair**: Toggles the high-performance regex engine that repairs corrupted color tags and gradients caused by auto-translators.

### [2. User Interface]
- **EnableSmartSkillLayout**: Toggles the logic that merges repetitive or split skill description blocks for better readability.

### [3. Visual & Font]
- **EnableFontReplacement**: Toggles global font overriding. When enabled, the mod will attempt to load custom fonts defined in `_01.font.txt`.
- **EnableUIResizer**: Toggles the dynamic layout adjustment engine. It reads boundaries from `_02.resize.txt` to prevent text overflowing.
- **EnableNumberFormatting**: Toggles standard thousands separators (`,`) for all numeric values, including combat damage and character HP.

### [4. System Core]
- **EnableSystemEnvironment**: Manages Windows OS level integration, including window styles and functional shortcuts (F11 for Fullscreen / Alt+Enter).
- **DisplayMode**: Sets the preferred window mode (0=FullScreen, 1=Borderless, 2=Maximized, 3=Windowed).
- **DeveloperLogs**: Enables verbose diagnostic logging and performance profiling (Bottleneck Analysis) for mod developers.
- **EnableTranslatorSync**: Toggles the internal bridge that links this mod's state with the XUnity.AutoTranslator engine.

---

## 💎 The Master Framework 10 (Project Philosophies)

Every module in this project is built adhering to our strict professional standards:

1.  **Strict Performance Focus**: All logic is zero-allocation in the main loop; heavy data is managed via high-performance static caches.
2.  **Clean Code & Architecture**: Adheres to a strict **Separation of Concerns** between the translation bridge, visual engine, and core system modules.
3.  **Advanced C# Features**: Utilizes `ReadOnlySpan<T>`, `AggressiveInlining`, and `Generics` to minimize CPU overhead and latency.
4.  **Static Registry Pattern**: Centralized registration of patches and configuration parameters for O(1) access speed during runtime.
5.  **Thread Safety**: Implements robust lock mechanisms (`_syncRoot`) to ensure data integrity during asynchronous localization tasks.
6.  **Comprehensive Logging**: A multi-tier logging system that distinguishes between user information and deep developer diagnostics.
7.  **Defensive Programming**: Extensive use of `Util.IsSafe()` and null-integrity checks to ensure 100% crash prevention.
8.  **Adaptive UI Logic**: Positions and scales UI elements dynamically based on the specific properties of the active language.
9.  **Minimal Boilerplate**: Consolidates redundant game hooks into unified, high-efficiency execution modules.
10. **Professional Documentation**: Provides enterprise-grade project documentation and a transparent file structure for other developers.

---
*Developed with ❤️ and AI-Enhanced Optimization to bring the ultimate Princess Connect experience to the global community.*
