# PreyEngine Source (Community Edition)

**Version: STS 11.3**

**PreyEngine** is a custom 2D game engine built with **MonoGame** + **ImGui.NET**.  
Created and maintained by **Arian Shahmohammadi**, founder of **Easyprey Studio**.

This repository is the official **Community / Source** release of PreyEngine.  
It is published under the MIT License with a mandatory attribution requirement.

---

## About

PreyEngine is an Iranian-made 2D game engine designed around a clean C# architecture and a Unity-like workflow. This STS 11.3 snapshot includes the editor, scene system, prefabs, input, scripting with hot reload, camera gizmos, and a modern Avalonia launcher.

---

## Open Source Policy

| Period | Policy |
|--------|--------|
| **Until Bahman 1405 (Jan/Feb 2027)** | Community source is released with approximately **3 version delay** behind the mainline |
| **From Bahman 1405 onward** | The 3-version delay policy is **retired**. Community releases follow a **2-month delay** instead |

**Critical and essential bug fixes** are **not** subject to the delay policy. Once identified and fixed, they are made available as soon as practical — without waiting for the normal delay window.

---

## What's New in STS 11.3

### Input System
- Unified input facade over MonoGame (`Keyboard`, `Mouse`, `TouchPanel`)
- Keyboard helpers: `IsKeyDown` / `IsKeyPressed` / `IsKeyReleased`
- Pointer abstraction (mouse + touch) for cross-platform code
- Mouse buttons, scroll delta, mouse delta
- Multi-touch accessors for future mobile runtimes

### Scripting System
- Unity-inspired `Script` component model
- Script compilation and registry
- Hot reload support
- Script serialization with scenes
- Starter templates

### Editor
- Camera gizmo / camera icon in the viewport
- External editor launcher integration
- Prior stability fixes retained (Transform save, ProjectSettings, drag-and-drop, dirty tracking)

---

## Features (STS 11.3)

### Core
- Entity-Component-System (ECS)
- `GameObject`, `Component`, `Transform`
- Scene system with JSON serialization (Transform save working)
- Prefab system (create, instantiate, Apply / Revert / Unlink)
- SpriteRenderer
- Camera2D with follow target, smoothing and offset
- Input System
- Scripting System + Hot Reload
- `Time` utilities for scripts

### Editor
- Hierarchy, Inspector, Viewport (Play Mode), Content Browser, Console
- Gizmo system (Move / Rotate / Scale) — World & Local
- Camera gizmo
- Full Undo/Redo
- Adaptive grid, scene tabs
- Live asset import (PNG, JPG, BMP)
- Drag & drop (File Explorer, Content Browser, Hierarchy)
- ProjectSettings (last opened scene)
- Dirty tracking with save confirmation

### Launcher
- Avalonia UI
- Splash screen
- Project management
- Editor version manager

---

## Project Structure

```
PreyEngine-Source/
├── src/
│   ├── MyEngine.Core/              # Engine core (ECS, Scene, Input, Scripting)
│   ├── MyEngine.Editor/            # Editor (ImGui + MonoGame)
│   ├── MyEngine.EditorFramework/   # Shared ImGui renderer
│   └── MyEngine.Launcher/          # Avalonia launcher
├── MyEngine.sln
├── LICENSE
└── README.md
```

> Internal code still uses the historical namespace `MyEngine`.  
> The public product name is **PreyEngine**.

---

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer
- Windows (primary platform)

---

## Getting Started

```bash
git clone https://github.com/StudioPrey/PreyEngine-Source.git
cd PreyEngine-Source

dotnet restore
dotnet build

# Run Editor
dotnet run --project src/MyEngine.Editor

# Run Launcher
dotnet run --project src/MyEngine.Launcher
```

---

## License & Attribution

This project is licensed under the **MIT License** with an additional **mandatory attribution** clause.

You are free to use, modify, and distribute this software, including for commercial purposes, provided that:

1. The original copyright notice is preserved.
2. Clear credit is given to **PreyEngine** by **Arian Shahmohammadi (Easyprey Studio)** in documentation, credits, about screens, or equivalent places.

See the [LICENSE](LICENSE) file for the full text.

---

## Author

**Arian Shahmohammadi**  
**Easyprey Studio** / StudioPrey

For updates, collaboration, or commercial inquiries, contact Easyprey Studio through official channels.

---

## Disclaimer

This is a delayed community snapshot (STS 11.3).  
It is provided as-is. Development continues on the mainline according to the delay policy described above.

Thank you for your interest in PreyEngine.
