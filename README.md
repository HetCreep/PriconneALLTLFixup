# PriconneALLTLFixup (Master Framework Edition)

An advanced performance optimization and localization repair framework for **Princess Connect! Re:Dive**, designed to provide high-quality translation support for Global and Private Server environments.

> **Note**: This mod is an improved and highly optimized version based on the original foundations laid by **Dakari** and **Olegase**.

## 🚀 Tech Stack

- **Framework**: BepInEx 6 (IL2CPP) - Modern Unity modding environment.
- **Hooking Engine**: HarmonyX - High-efficiency runtime bytecode patching.
- **Core Dependency**: **XUnity.AutoTranslator (XUAT)** - **Mandatory**. This mod functions as an enhancement bridge for XUAT; it will not initialize without it.
- **Low-Level Bridge**: Win32 API Integration - For native window and OS-level control.
- **Optimization**: .NET Advanced Features (`ReadOnlySpan<char>`, `AggressiveInlining`).

---

## ✨ Key Features

### 1. Smart Localization Engine
- **Intelligent Path Redirection**: The mod determines the asset directory (`BepInEx\Translation\{ISO-639-1}`) using a priority system. It allows you to specify a fixed language for assets (like fonts/layout rules) while XUAT translates the text into a different language, ensuring zero internal conflicts.
- **Dynamic Fallback**: If no ISO 639-1 code is specified in the settings, the mod automatically detects and synchronizes with the active language configured in XUnity.AutoTranslator.

### 2. Visual & Font Mastery
- **Universal Font Redirection**: Globally overrides hardcoded game fonts using AssetBundles and external mapping rules.
- **Adaptive UI Resizer**: Real-time calculation of character width and word-wrapping based on the specific linguistic properties of the target language (e.g., Thai vowel stacking or Spanish text expansion).

### 3. Data Integrity & Search
- **Multi-Language Search Support**: Enabled via `text_id.txt`. This allows users to search for characters or items using their translated names (English/Thai/etc.), bypassing the game's hardcoded Japanese-only search logic.
- **High-Performance Number Formatting**: Adds standard thousands separators (`,`) across all UI elements, gauges, and Story text with zero allocation overhead.

---

## 🛠️ Critical File Structure

The mod utilizes resources from the `BepInEx\Translation\{ISO-639-1}\` directory:

| Path | Purpose |
| :--- | :--- |
| `Font\` | Stores `.unity3d` font bundles for global redirection. |
| `Other\_01.font.txt` | Mapping rules for assigning fonts to specific GameObjects. |
| `Other\_02.resize.txt` | Defines UI width boundaries and overflow methods (Resize/Shrink). |
| `Other\text_id.txt` | **Critical**: Maps `eTextId` to strings to enable multi-language search support. |

---

## ⚙️ Configuration (PriconneALLTLFixup.cfg)

### [1. Translation Engine]
- **LanguageCode**: Target **ISO 639-1 Code**. Leave empty for **Smart Fallback** (Sync with XUAT).
- **EnableTranslationRepair**: Proactively fixes corrupted Rich Text tags (colors/gradients).

### [2. User Interface]
- **EnableSmartSkillLayout**: Merges redundant skill description blocks for better readability.

### [3. Visual & Font]
- **EnableFontReplacement**: Toggles global font overriding based on `_01.font.txt`.
- **EnableUIResizer**: Toggles dynamic layout adjustment.
- **EnableNumberFormatting**: Toggles thousands separators (`,`) for HP, damage, and currency.

### [4. System Core]
- **EnableSystemEnvironment**: Manages window styles and OS shortcuts (F11 / Alt+Enter).
- **DisplayMode**: Sets the window mode: `0`=FullScreen, `1`=Borderless, `2`=**Maximized** (Required for compatibility with specific OS builds), `3`=Windowed.
- **DeveloperLogs**: Enables verbose profiling and bottleneck analysis for mod developers.

---

## 💎 The Master Framework 10 (Philosophies)

1.  **Strict Performance**: Zero-allocation logic; data managed via high-performance caches.
2.  **Clean Architecture**: Strict **Separation of Concerns** between modules.
3.  **Advanced C#**: Leveraging `Span`, `Inlining`, and `Generics` for peak efficiency.
4.  **Static Registry Pattern**: Centralized registration for O(1) runtime access.
5.  **Thread Safety**: Proper lock synchronization for asynchronous tasks.
6.  **Comprehensive Logging**: Multi-tier diagnostics and bottleneck identification.
7.  **Defensive Programming**: Pervasive `Util.IsSafe()` checks to prevent 100% of crashes.
8.  **Adaptive UI Logic**: UI adapts dynamically to the active language's properties.
9.  **Minimal Boilerplate**: Consolidated hooks for streamlined execution.
10. **Professional Documentation**: Enterprise-grade structure and transparency.

---

## 👨‍💻 Developed By

- **Project Foundation**: Based on concepts and code by **Dakari** and **Olegase**.
- **Core Maintenance**: **HetCreep** & **AI Collaborator (Gemini)**.

### 📢 Join Us!
We are actively looking for talented developers experienced in **C#**, **IL2CPP**, and **Reverse Engineering** to join our team and help improve this framework. If you're interested in building the ultimate localization experience, feel free to contribute!

---
*Developed with ❤️ to bring the ultimate Princess Connect experience to the global community.*
