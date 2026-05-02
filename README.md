# ⚔️ PriconneALLTLFixup

**PriconneALLTLFixup** is a high-performance, universal master framework for modding *Princess Connect! Re:Dive* (PC/IL2CPP). Designed with a focus on strict performance and absolute stability, this framework provides a robust foundation for global translation projects through an intelligent rule-based engine.

---

## 🏗️ System Architecture (Consolidated Phases)
The project is meticulously structured into logical phases to ensure clean separation of concerns and ease of maintenance:

### ⚙️ Phase 1: Core Integrity & Environment
* **Advanced Window Management**: Full control over Fullscreen, Borderless, and Maximized modes via Native Win32 API.
* **System Shortcuts**: Native support for `F11` (Toggle Fullscreen) and `Alt+Enter`.
* **Engine Bridge**: An intelligent synchronization layer with *XUnity.AutoTranslator* (XUAT) that automatically handles language codes and engine states.

### 🧠 Phase 2: Translation Engine & Safety
* **Rich Text Repair**: Automatic restoration of corrupted color tags, gradients, and font size parameters caused by machine translation using the optimized `Fastenshtein` algorithm.
* **Kill-Switch (Anti-Detection)**: A high-level safety protocol that suppresses translation immediately upon detecting sensitive player data to prevent administrative flags.

### 🎨 Phase 3: Visual & Layout Engine (Universal Master)
* **Universal Font Redirection**: Dynamically redirects game fonts to high-quality international fonts from the `Font` directory, governed by the `_01.font.txt` rule-set.
* **Universal UI Resizer**: An adaptive layout processor that handles overflow, line wrapping, and dynamic width adjustments (Shrink vs. ResizeHeight) via `_02.resize.txt`.
* **Adaptive Font Size**: Context-aware font scaling based on active language (e.g., specific optimizations for Thai/Vietnamese scripts).
* **Numbers & Bubbles (Beta)**: Ongoing development for precise positioning of speech bubbles and combat gauge telemetry.

---

## 💎 Development Philosophy (The 10 Commandments)
Every line of code follows a strict set of principles to ensure the ultimate user experience:
1.  **Strict Performance Focus**: O(1) Cache-first logic. Zero unnecessary memory allocations in frame updates.
2.  **Defensive Programming**: Proactive `Util.IsSafe()` verification and file integrity checks before execution.
3.  **Thread Safety**: Robust lock mechanisms for registries and shared caches.
4.  **Universal Support**: Dynamic pathing architecture that supports any ISO 639-1 language code without hardcoding.

---

## 🚀 Tech Stack
* **BepInEx 6 (IL2CPP)**: Next-generation modding framework.
* **HarmonyX**: High-efficiency runtime bytecode patching.
* **Fastenshtein**: Integrated high-speed Levenshtein distance algorithm.
* **Win32 API Bridge**: Direct communication with Windows OS for windowing operations.

---

## 👨‍💻 Developer Context
This project was built upon the core foundations laid by **Dakari** and **Olegase**. It has since evolved into a universal framework with the assistance of my **AI Collaborator (Gemini)** to reach a professional standard of optimization and architectural cleanliness.

*Developed with ❤️ to provide the definitive Princess Connect! Re:Dive experience for the global community.*
