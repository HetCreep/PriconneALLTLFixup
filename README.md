# ⚔️ PriconneALLTLFixup

**PriconneALLTLFixup** is a high-performance core system mod for *Princess Connect! Re:Dive* (PC/IL2CPP). Designed with a **"Performance First & Clean Architecture"** philosophy, it serves as the master framework for multi-language localization support, window management, and engine integrity.

---

## 🏗️ Core Modules (Consolidated Phase 1 & 2)
We have refactored 12+ legacy patches into 4 robust modules to ensure a 0% crash rate and peak efficiency:

### ⚙️ Phase 1: Core Integrity
1.  **`WindowCorePatch`**: An intelligent display controller supporting Fullscreen, Borderless, and Maximized modes with native Win32 API styles. It also serves as the **Central Lifecycle Controller** for the mod.
2.  **`EngineBridgePatch`**: The critical bridge between Unity and XUnity.AutoTranslator (XUAT).
    - **Language Sync Policy**: Supports dual-mode synchronization. By default, it inherits settings from XUAT (`Language={Lang}`). If disabled, it enforces a manual override using the mod's configuration to ensure strict path consistency.
    - **Asset Integrity**: Manages AssetBundle unstripping to ensure font assets are protected across scene loads.

### 🧠 Phase 2: Translation Engine
3.  **`TranslationCorePatch` (The Brain)**: 
    - **SetText Pipeline**: A high-performance interceptor that silences corrupted tags and repairs broken color gradients using the integrated `Fastenshtein` algorithm.
    - **Safety & Flow Control**: Features an integrated **Kill-Switch** (Anti-Detection) that suppresses all translations if sensitive player data (e.g., specific party names) is detected.
    - **Smart Search Loader**: Preloads character name mappings, allowing the search filter to recognize Japanese names or names in your preferred target language.
4.  **`TextRegistryPatch` (The Vault)**: 
    - **Global Cache**: Manages in-memory mapping for Static Text IDs (`text_id.txt`) and skill metadata. Features a failsafe that aborts loading if critical data files are missing.
    - **Smart Skill Layout**: An optional UI enhancement that groups and merges redundant skill description lines for maximum readability.

---

## 📜 The 10 Project Commandments
Every line of code adheres to these strict standards:
1. **Strict Performance**: Optimized loops and O(1) complexity via `HashSet` and Compiled Regex.
2. **Clean Architecture**: Refactored 10+ legacy files into 4 focused modules.
3. **Advanced C# Features**: Utilizing `Span<T>`, `MethodInlining`, and `Expression Trees`.
4. **Static Registry**: Centralized management for all patches and settings.
5. **Thread Safety**: Robust `lock` mechanisms for safe cross-thread data access.
6. **Comprehensive Logging**: Detailed diagnostics with **Developer Context** support.
7. **Defensive Programming**: Proactive `Util.IsSafe()` and file integrity checks.
8. **Adaptive UI**: Dynamic logic to handle varying text lengths and layouts.
9. **Minimal Boilerplate**: Consolidated redundant hooks into streamlined modules.
10. **Professional Documentation**: Enterprise-grade structure and clear technical documentation.

---

## 🚀 Tech Stack
- **BepInEx 6 (IL2CPP)**: Modern modding framework.
- **HarmonyX**: High-efficiency runtime patching.
- **XUnity.AutoTranslator**: Core translation integration.
- **Fastenshtein**: High-performance Levenshtein algorithm (Integrated).
- **Win32 API Bridge**: Native window and style control.

---
### 👨‍💻 Developer Context
This project was born from the core ideas and foundations laid by **Dakari** and **Olegase**. Without their inspiration, this mod would not exist. 

A special acknowledgment to my **AI Collaborator (Gemini)**; this project has relied heavily on AI assistance to achieve this level of optimization and architectural cleanliness. 

**Join us!** We are always looking for skilled developers to help improve this mod. If you are experienced in C# and IL2CPP modding, your contributions are more than welcome to make this the best experience for the community.

*Developed with ❤️ and AI to bring the best Princess Connect experience to everyone.*
