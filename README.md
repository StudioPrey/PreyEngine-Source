# PreyEngine Source (Community Edition)

**Version: LTS 1.4 Eclipse**

**PreyEngine** is a custom 2D game engine built with **MonoGame** + **ImGui.NET**, engineered around a decoupled C# architecture and an intuitive development workflow.  
Created and maintained by **Arian Shahmohammadi**, founder of **Easyprey Studio**.

This repository is the official **Community / Source** release channel for PreyEngine. It is published under the MIT License with a mandatory attribution requirement.

---

## What's New in LTS 1.4 Eclipse

### ⚡ Massive Runtime & Physics Overhaul (Zero Breaking Changes)
- **Data-Oriented Physics Engine**: Transitioned core broadphase and collision detection routines away from an unoptimized $O(n^2)$ bottleneck to a high-performance **Data-Oriented design**. Drastically minimizes runtime GC allocations and accelerates collision queries.
- **Optimized Physics Serialization**: Runtime serialization and deserialization of physics components and colliders have been redesigned for minimal overhead during scene transitions.
- **Zero API Breaking Changes**: Despite deep under-the-hood refactoring of the physics, runtime loop, and 2D rendering pipeline, all public game scripting and engine APIs remain 100% backward-compatible.
- **Heavy Scene Loading Optimization**: Scene deserialization and asset streaming pipelines have been completely overhauled to handle heavy, asset-dense projects with significantly reduced load times and memory footprint.

### 🎮 Scripting Lifecycle & IDE Integration
- **Strict Execution Order Guarantees**: Refactored scripting lifecycle scheduler to strictly enforce that all `Awake()` calls across all game objects and scenes execute before any `Start()` callbacks are triggered.
- **First-Class IDE Autocomplete**: Automatic generation of `GameScripts.csproj` and `.sln` metadata, enabling seamless IntelliSense, code completion, and real-time error checking in Visual Studio and VS Code without modifying the engine's internal Roslyn compiler.

### 🎬 Animation & Audio Subsystem Bug Fixes
- **Animation Loop & Clipping Fixes**: Resolved two critical engine bugs affecting sprite frame sequences: fixed a clipping boundary calculation issue and eliminated the desynchronization bug during animation loop cycles.
- **Audio Improvements & WAV Support**: Fixed three critical audio management bugs and introduced native support for `.wav` audio playback with stable resource lifecycle handling.

### 🎨 Sleek Redesigned Launcher & Editor Polish
- **Completely Redesigned Launcher**: Modern, minimal, and responsive workspace hub rebuilt with Avalonia UI for effortless project management and engine version switching.
- **Optimized Viewport & Tooling Icons**: Viewport toolbar controls and utility icons now utilize an optimized SVG-based rendering pipeline with crisp rendering and lower draw overhead.
- **Typography & Localization**: Integrated the official **Vazirmatn** font alongside modern Latin typography, offering crisp UI rendering and native Persian text support in the editor.

### 📦 Standalone Production Pipeline
- **Game Packaging & Splash Screen**: Built-in, togglable PreyEngine splash sequence for standalone games, automated win-x64 compilation, auto-generated `.zip` archive creation, and structured **Build Reports**.
- **Diagnostics & State**: Built-in runtime `Crash Logger` and lightweight `Save / Load` state management.

---

## Core Architecture

- **Prey.Core (`MyEngine.Core`)**: Lightweight ECS (`GameObject`, `Component`, `Transform`), Data-Oriented 2D physics, Input handling, Scene/Prefab hierarchy, and audio/animation systems.
- **Prey.Editor (`MyEngine.Editor`)**: Integrated ImGui development environment (Hierarchy, Inspector, Viewport, Play Mode, Content Browser, Console, Build Settings).
- **Prey.Runtime (`MyEngine.Runtime`)**: Pure standalone player. Strictly depends on Core — completely free of editor assemblies in release binaries.
- **Prey.Launcher (`MyEngine.Launcher`)**: Sleek Avalonia-powered modern hub for managing projects and engine templates.

> **Note on Namespaces:** Internal codebase namespaces use the historic root `MyEngine`, while the official product name is **PreyEngine**.

---

## Project Structure
PreyEngine-Source/

├── src/

│ ├── MyEngine.Core/ # Core ECS, Data-Oriented Physics, Rendering Abstraction, Audio, Animation

│ ├── MyEngine.Editor/ # ImGui.NET Editor, Gizmos & Build Pipeline

│ ├── MyEngine.EditorFramework/ # Editor abstractions and UI helpers

│ ├── MyEngine.Runtime/ # Standalone Game Player

│ └── MyEngine.Launcher/ # Modern Avalonia-based Launcher

├── MyEngine.sln

├── LICENSE # MIT + Attribution

└── README.md

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer
- Windows 10/11 (win-x64 target for standalone player)

---

## Getting Started

### 1. Clone the repository
```bash
git clone https://github.com/StudioPrey/PreyEngine-Source.git
cd PreyEngine-Source
 Restore and Build
bash
dotnet restore
dotnet build --configuration Release
3. Run the Tools
bash
# Launch the redesigned modern Launcher
dotnet run --project src/MyEngine.Launcher

# Or run the Editor directly
dotnet run --project src/MyEngine.Editor
4. Exporting Your Game
Open your project in the Editor → Navigate to File > Build Settings… → Configure target options → Click Build.

The standalone game package will be generated inside the project’s Builds/ folder.

License & Attribution
PreyEngine is open-source under the MIT License with a Mandatory Attribution Clause.

You are free to use, modify, and distribute the engine for personal and commercial projects, provided that:

The original copyright notice is retained.
Prominent attribution is given to PreyEngine by Arian Shahmohammadi (Easyprey Studio) in documentation, credit screens, or README files of any published application.
See the LICENSE file for the full legal text.