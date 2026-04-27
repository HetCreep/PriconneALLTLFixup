# ⚔️ PriconneALLTLFixup

**PriconneALLTLFixup** is a high-performance core system mod for *Princess Connect! Re:Dive* (PC/IL2CPP). Designed with a **"Performance First & Clean Architecture"** philosophy, this mod serves as the foundational framework for window management, engine integrity, and advanced Thai translation support.

---

## 🛠️ Architecture: Phase 1 (Core Integrity)
In this phase, we have consolidated legacy patches into 4 robust modules to minimize boilerplate and maximize system stability:

1. **`EngineBridgePatch`**: A critical link between the Unity Engine and XUnity.AutoTranslator. It handles AssetBundle unstripping (font protection) and enforces language synchronization policies.
2. **`WindowCorePatch`**: An intelligent display management system. It supports Exclusive Fullscreen, Borderless, and Maximized modes, including native Win32 API integrations and standard hotkeys (F11 / Alt+Enter); The central lifecycle controller. It ensures all dependencies are loaded and the system is ready before initializing mod logic.
3. **`TextSafetyPatch`**: The "Gatekeeper" layer. It prevents game crashes by intercepting null or destroyed UI objects before they reach the translation engine, ensuring 100% runtime safety.

---

## 📜 The 10 Project Commandments
This project is developed under strict adherence to these 10 core rules:

1. **Strict Performance Focus**: Zero resource wastage in high-frequency loops or frame updates.
2. **Clean Code & Architecture**: Logical separation of concerns across all modules.
3. **Advanced C# Features**: Utilizing `Span<T>`, `MethodInlining`, and Generics for maximum efficiency.
4. **Static Registry Pattern**: Centralized, static registration for patches and configurations.
5. **Thread Safety**: Proper locking and synchronization for cache and text processing.
6. **Comprehensive Logging**: Multilevel logging (Info, Debug, Error) with developer context.
7. **Defensive Programming**: Proactive null-checks and integrity validation at every entry point.
8. **Adaptive UI Logic**: Dynamic calculation of UI scales and positions based on resolution.
9. **Minimal Boilerplate**: Consolidating redundant code into streamlined, reusable logic.
10. **Professional Documentation**: Enterprise-grade code structure with clear regions and comments.

---

## ⚙️ Configuration
Display and system settings can be adjusted via the `.cfg` file.

| Value | Mode | Description |
| :--- | :--- | :--- |
| **0** | **FullScreen** | Maximum performance for dedicated gaming. |
| **1** | **Window Borderless** | **Borderless Window** (Default) - Seamless Alt-Tab. |
| **2** | **MaximizedWindow (For some OS)** | Maximized window with Taskbar visibility. |
| **3** | **Windowed** | Standard windowed mode based on custom size. |

---

## 🚀 Tech Stack
- **BepInEx 6 (IL2CPP)**: The modding framework.
- **HarmonyX**: For high-efficiency runtime patching.
- **XUnity.AutoTranslator**: Base translation engine integration.
- **Win32 API Bridge**: For OS-level window control and styles.

---

### 👨‍💻 Developer Notes
This mod utilizes a **Background Deployment** system for patch registration, ensuring a non-blocking initialization process during game startup. For deep system diagnostics, please enable `DeveloperContext` in the configuration.

---
*Developed with ❤️ to bring the best Princess Connect experience.*
