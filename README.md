# ⚔️ PriconneALLTLFixup

**PriconneALLTLFixup** is a high-performance core system mod for *Princess Connect! Re:Dive* (PC/IL2CPP). Developed under the **"Performance First & Clean Architecture"** philosophy, this mod serves as the ultimate foundational framework for Thai translation support, window management, and engine integrity.

---

## 🏗️ Project Architecture (Phase 1 & 2 Completed)
We have optimized the system by consolidating over 12 legacy patches into 4 powerful modules to ensure a 0% crash rate and maximum efficiency:

### ⚙️ Phase 1: Core Integrity
1. **`WindowCorePatch`**: An intelligent display controller supporting Exclusive Fullscreen, Borderless, and Maximized modes with native Win32 API integration. It also serves as the **Central Lifecycle Controller** for the mod.
2. **`EngineBridgePatch`**: A critical link between Unity and XUnity.AutoTranslator. It handles AssetBundle unstripping (Font Protection) and enforces strict language synchronization policies.

### 🧠 Phase 2: Translation Engine
3. **`TranslationCorePatch` (The Brain)**: 
    - **SetText Interceptor**: Repairs corrupted tags and color gradients broken by machine translation.
    - **Silent Repair**: Operates quietly without log-spamming, utilizing high-performance `Fastenshtein` logic.
    - **Bilingual Search**: Enables character searching using Thai, English, or Japanese names.
    - **Kill-Switch (Anti-Detection)**: Automatically suppresses translation when specific strings (e.g., party names) are detected for player safety.
4. **`TextRegistryPatch` (The Vault)**: 
    - **Global Text Registry**: In-memory mapping for Static Text IDs (`text_id.txt`).
    - **Smart Skill Layout**: Intelligent grouping of skill descriptions to prevent UI overflow and improve readability.

---

## 📜 The 10 Project Commandments
Every line of code follows these strict rules:
1. **Strict Performance**: Zero resource wastage in high-frequency loops.
2. **Clean Architecture**: Logical separation of concerns (Refactored 10+ files into 4).
3. **Advanced C#**: Utilizing `Span<T>`, `MethodInlining`, and `HashSet<T>` for O(1) complexity.
4. **Static Registry**: Centralized patch and configuration management.
5. **Thread Safety**: Robust `lock` mechanisms for all cache and dictionary operations.
6. **Comprehensive Logging**: Detailed diagnostics with **Developer Context** support.
7. **Defensive Programming**: Proactive `Util.IsSafe()` checks on all Unity objects.
8. **Adaptive UI**: Dynamic logic to handle Thai text length and layout changes.
9. **Minimal Boilerplate**: Consolidated redundant hooks into unified modules.
10. **Professional Documentation**: Code and documentation built for enterprise standards.

---

## ⚙️ Configuration Highlights
| Group | Setting | Description |
| :--- | :--- | :--- |
| **Core** | `EnableTranslatorSync` | **Master Switch** for the entire translation engine. |
| **Translation** | `LanguageCode` | Standard ISO code (e.g., `th`, `en`). |
| **Translation** | `EnableTranslationRepair` | Fixes corrupted color/gradient tags automatically. |
| **UI** | `EnableSmartSkillLayout` | Toggles the intelligent skill description grouping. |
| **Core** | `DisplayMode` | 0: Fullscreen, 1: Borderless, 2: Maximized (For some OS), 3: Windowed. |

---

## 🚀 Tech Stack
- **BepInEx 6 (IL2CPP)**: The modern modding framework.
- **HarmonyX**: For high-efficiency runtime patching.
- **XUnity.AutoTranslator**: Base translation engine integration.
- **Win32 API Bridge**: Native window style control.

---
### 👨‍💻 Developer Notes
This project uses **Background Deployment** via `HarmonyPatchController` to ensure the game starts smoothly without blocking the main thread during patch registration.

*Developed with ❤️ to bring the best Princess Connect experience to the community.*
