# PreyEngine Source (Community Edition)

**LTS 1 — Eternal Rain**  
**Snapshot: LTS 1.3.41**

**PreyEngine** is a custom 2D game engine built with **MonoGame** + **ImGui.NET**.  
Created and maintained by **Arian Shahmohammadi**, founder of **Easyprey Studio**.

This repository is the official **Community / Source** release channel for PreyEngine.  
It is published under the MIT License with a mandatory attribution requirement.

---

## About

PreyEngine is an Iranian-made 2D game engine designed around a clean C# architecture and a Unity-like workflow. **LTS 1 (Eternal Rain)** is the first Long-Term Support generation and focuses on stability and refinement. This snapshot (**LTS 1.3.41**) adds a full **standalone Runtime / Build pipeline** so you can produce a real player executable without the editor, on top of the existing physics, editor, input, and scripting foundations.

---

## Release Channels: STS vs LTS

| Channel | Focus | Support window |
|---------|--------|----------------|
| **STS** (Short-Term Support) | Rapid feature delivery | Up to **two STS versions** of support (critical bug fixes excluded from this limit and may ship sooner) |
| **LTS** (Long-Term Support) | Stability and improvement | Each LTS **generation** (the leading version number, e.g. LTS **1**.x) is supported for up to **about three months**, including selected new features, bug fixes, and stability work within that generation |

- **STS** moves fast: new capabilities land quickly; support depth is intentionally limited.
- **LTS** moves carefully: fewer breaking swings, emphasis on reliability, polish, and sustainable improvement within the generation.

**Critical and essential bug fixes** (crashes, data loss, security issues) are prioritized on both channels and are not held back solely by normal support-window rules.

---

## Community Delay Policy

| Period | Policy |
|--------|--------|
| **Until Bahman 1405 (Jan–Feb 2027)** | Community source follows approximately a **3-version delay** behind mainline milestones |
| **From Bahman 1405 onward** | The 3-version rule is **retired**. Community snapshots target about a **6–8 week delay** after a milestone is considered stable on the mainline |

- Small fixes and polish are usually **bundled** into the next Community snapshot.
- **Critical / essential bug fixes** are exempt from delay targets and are published as soon as practical.
- Delay figures are **release targets**, not a guaranteed SLA.

---

## What's New in LTS 1.3.41 (Eternal Rain)

### Standalone Runtime & Build Pipeline
- New **`MyEngine.Runtime`** project that depends **only** on `MyEngine.Core` (no Editor, no EditorFramework, no ImGui.NET)
- Real standalone player executable: the game without any editor UI, gizmos, or ImGui
- **File > Build Settings…** — game name, version, window size, fullscreen, optional `.ico`, boot scene
- **File > Build** — background publish (UI stays responsive); progress and result in Console
- `dotnet publish` as **self-contained** + **ReadyToRun** (player does not need .NET installed; faster cold start on weak machines)
- Scripts compiled to a real on-disk DLL (`CompileToFile`); Assets copied without raw `.cs` sources
- `game.manifest.json` written (name / version / boot scene / window settings)
- Output folder: `<project>/Builds/<GameName>/` — zip-ready for distribution

### Architecture notes
- Runtime draws directly to the screen (no intermediate RenderTarget used for ImGui), so it is lighter than editor Play Mode
- Same update/draw loop as editor Play Mode, minus every editor-only system

### Known v1 Runtime limits (honest scope)
- **Single boot scene** (`GameManifest.BootScenePath`) — designed so a future multi-scene manager can treat it as the first scene without redesign
- **win-x64 only** for now (adding other RIDs is a small change in `ProjectBuilder`)
- Window/taskbar icon at runtime is a long-standing MonoGame limitation; the `.exe` icon itself is reliable

### Also included (from prior Community / LTS line)
- Full custom **2D Physics** (`IPhysicsBackend2D`, `PhysicsWorld2D`, `Rigidbody2D`, Box/Circle colliders, raycast, collision callbacks)
- Input System, Scripting + Hot Reload, Camera gizmo
- Editor polish: dedicated Toolbar, vector icons in Content Browser, panel title-bar fix, font, layout
- Content Browser folder tree + drag-and-drop move of assets/folders
- Transform serialization fix, dirty tracking, ProjectSettings, Avalonia launcher

---

## Features Overview

### Core
- ECS (`GameObject`, `Component`, `Transform`)
- Scene + prefab systems (JSON)
- SpriteRenderer, Camera2D
- Input System
- Scripting + Hot Reload
- **2D Physics** (Rigidbody / Colliders / World / Raycast)
- **GameManifest** (shared Editor ↔ Runtime DTO)

### Editor
- Hierarchy, Inspector, Viewport (Play Mode), Content Browser, Console
- Dedicated Toolbar (Play / Undo / Gizmos / Grid / Colliders)
- Gizmos (World / Local), camera gizmo, collider overlays
- Undo/Redo, adaptive grid, scene tabs
- Live asset import, drag & drop, ProjectSettings
- **Build Settings + Build** pipeline

### Runtime
- Standalone player (`MyEngine.Runtime`)
- Self-contained publish, ReadyToRun
- No editor assemblies in the final binary

### Launcher
- Avalonia UI, splash, project and editor version management

---

## Project Structure

```
PreyEngine-Source/
├── src/
│   ├── MyEngine.Core/           # Core, Input, Scripting, Physics, GameManifest
│   ├── MyEngine.Editor/         # ImGui editor + Build
│   ├── MyEngine.EditorFramework/
│   ├── MyEngine.Runtime/        # Standalone player (NEW)
│   └── MyEngine.Launcher/       # Avalonia launcher
├── MyEngine.sln
├── LICENSE
└── README.md
```

> Internal namespaces still use the historical name `MyEngine`.  
> The public product name is **PreyEngine**.

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer
- Windows (primary platform; Runtime currently targets win-x64)

---

## Getting Started

```bash
git clone https://github.com/StudioPrey/PreyEngine-Source.git
cd PreyEngine-Source

dotnet restore
dotnet build

dotnet run --project src/MyEngine.Editor
dotnet run --project src/MyEngine.Launcher
```

To produce a standalone build: open a project in the Editor → **File > Build Settings…** → **File > Build**. Output appears under the project’s `Builds/` folder.

---

## License & Attribution

Licensed under the **MIT License** with a **mandatory attribution** clause.

You may use, modify, and distribute this software, including commercially, provided that:

1. The original copyright notice is preserved.
2. Clear credit is given to **PreyEngine** by **Arian Shahmohammadi (Easyprey Studio)** in documentation, credits, about screens, or equivalent places.

See [LICENSE](LICENSE) for the full text.

---

## Author

**Arian Shahmohammadi**  
**Easyprey Studio** / StudioPrey

---

## Disclaimer

This is a delayed Community snapshot of **LTS 1 — Eternal Rain** (**LTS 1.3.41**).  
It is provided as-is. Mainline development continues under the policies described above.

Thank you for your interest in PreyEngine.
