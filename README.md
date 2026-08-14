# PreyEngine Source (Community Edition)

**Version: STS 11.2.1**

**PreyEngine** is a custom 2D game engine built with **MonoGame** + **ImGui.NET**.  
Created and maintained by **Arian Shahmohammadi**, founder of **Easyprey Studio**.

This repository is the official **Community / Source** release of PreyEngine.  
It is published under the MIT License with a mandatory attribution requirement.

> The latest development version of PreyEngine remains closed-source.  
> Community source releases follow a **3-version delay** policy.

---

## About

PreyEngine is an Iranian-made 2D game engine designed around a clean C# architecture and a Unity-like workflow. This STS 11.2.1 snapshot includes a usable editor, scene system, prefabs, a modern Avalonia launcher, and critical stability fixes over the previous community release.

---

## What's New in STS 11.2.1

### Critical Bug Fix
- **Transform serialization fixed**  
  Position, Rotation, and Scale were previously stored as fields instead of properties in `GameObjectData`. Combined with `IncludeFields = false` on the JSON serializer, this meant transforms were **never saved** to `.scene` files and always reloaded as defaults `(0, 0)` / scale `1`.  
  This has been fully corrected. Existing scenes saved before this version must have object positions re-placed once.

### Stability & Editor Improvements
- Removed the fake default GameObject that appeared on editor startup
- Last opened scene is now remembered across sessions via `ProjectSettings.json`
- Fixed Viewport going black after Save Scene / Save As Prefab
- Fixed incorrect scene path handling and duplicate scene folders
- Fixed drag-and-drop from File Explorer → Content Browser
- Fixed drag-and-drop from Content Browser → Hierarchy
- Shared drop logic extracted into `AssetDropHandler` for consistency

---

## Open Source Policy

| Item | Detail |
|------|--------|
| **Release Model** | Community source is released with approximately **3 versions delay** behind the closed mainline |
| **License** | MIT + mandatory attribution to PreyEngine |
| **Goal** | Let developers learn from and experiment with the engine while protecting ongoing commercial development |
| **Latest Features** | Available only in the closed-source version. Contact Easyprey Studio for production use or support |

---

## Features (STS 11.2.1)

### Core
- Entity-Component-System (ECS) architecture
- `GameObject`, `Component`, `Transform`
- Scene system with JSON serialization (**Transform save fully working**)
- Prefab system (create, instantiate, Apply / Revert / Unlink)
- SpriteRenderer
- Camera2D with follow target, smoothing and offset
- Basic rendering pipeline

### Editor
- Hierarchy panel
- Inspector panel
- Viewport with Play Mode
- Content Browser with folder navigation
- Console panel
- Gizmo system (Move / Rotate / Scale) — World & Local space
- Full Undo/Redo stack (Ctrl+Z / Ctrl+Y)
- Adaptive grid
- Scene tabs
- Live asset import (PNG, JPG, BMP) via FileSystemWatcher
- Drag & drop support (File Explorer + Content Browser + Hierarchy)
- ProjectSettings (remembers last opened scene)

### Launcher
- Modern Avalonia UI
- Splash screen
- Project management
- Editor version manager
- Real folder picker

### Not included in STS 11.2.1
- Scripting system
- Input System
- Later editor and runtime improvements present in newer closed versions

---

## Project Structure

```
PreyEngine-Source/
├── src/
│   ├── MyEngine.Core/              # Engine core
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

For the latest version, commercial inquiries, or collaboration, please contact Easyprey Studio through official channels.

---

## Disclaimer

This is a delayed community snapshot (STS 11.2.1).  
It is provided as-is. The closed-source mainline continues active development and contains features and fixes not present in this release.

Thank you for your interest in PreyEngine.
