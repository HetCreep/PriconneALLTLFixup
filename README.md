# ⚔️ PriconneALLTLFixup

**PriconneALLTLFixup** is a high-performance core system mod for *Princess Connect! Re:Dive* (PC/IL2CPP). Designed with a **"Performance First & Clean Architecture"** philosophy, it serves as the master framework for Thai localization, display management, and engine integrity.

---

## 🏗️ Core Modules (Consolidated Phase 1 & 2)
We have refactored 12+ legacy patches into 4 robust modules to ensure a 0% crash rate and peak efficiency:

### ⚙️ Phase 1: Core Integrity
1.  **`WindowCorePatch`**: An intelligent display controller supporting Fullscreen, Borderless, and Maximized modes with native Win32 API styles. It also serves as the **Central Lifecycle Controller** for the mod.
2.  **`EngineBridgePatch`**: The critical bridge between Unity and XUnity.AutoTranslator (XUAT).
    - **Language Sync Policy**: Supports dual-mode synchronization. By default, it inherits settings from XUAT (`Language=th`). If disabled, it enforces a manual override using the mod's configuration to ensure strict path consistency.
    - **Asset Integrity**: Manages AssetBundle unstripping to protect custom Thai fonts.

### 🧠 Phase 2: Translation Engine
3.  **`TranslationCorePatch` (The Brain)**: 
    - **SetText Pipeline**: A high-performance interceptor that silences corrupted tags and repairs broken color gradients using the `Fastenshtein` algorithm.
    - **Safety & Flow Control**: Features an integrated **Kill-Switch** (Anti-Detection) that suppresses all translations if sensitive player data (e.g., specific party names) is detected.
    - **Bilingual Search**: Unlocks the ability to search for characters using Thai, English, or Japanese names simultaneously.
4.  **`TextRegistryPatch` (The Vault)**: 
    - **Global Cache**: Manages in-memory mapping for Static Text IDs (`text_id.txt`) and skill metadata.
    - **Smart Skill Layout**: An optional UI enhancement that groups and merges redundant skill description lines for maximum readability.

---

## ⚙️ Key Configuration
| Setting | Default | Mode | Description |
| :--- | :--- | :--- | :--- |
| `EnableTranslatorSync` | `true` | **Auto** | Synchronizes language code directly from XUAT settings. |
| `LanguageCode` | `en` | **Manual** | Overrides XUAT settings if `EnableTranslatorSync` is `false`. |
| `EnableTranslationRepair` | `true` | Silent | Automatically fixes corrupted machine-translated tags. |
| `EnableSmartSkillLayout` | `true` | UI | Groups redundant skill text lines for a cleaner interface. |

---

## 🚀 Tech Stack
- **BepInEx 6 (IL2CPP)**: Modern modding framework.
- **HarmonyX**: High-efficiency runtime patching.
- **XUnity.AutoTranslator**: Core translation integration.
- **Win32 API Bridge**: Native window control.

---
### 👨‍💻 Developer Context
This mod utilizes a **Background Deployment** system to register patches without blocking the main game thread. Enable `DeveloperLogs` for deep system analysis.

*Developed with ❤️ to bring the best Princess Connect experience to the community.*
