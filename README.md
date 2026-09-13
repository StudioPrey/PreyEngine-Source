# PreyEngine Source (Community Edition)

**Version: STS 11.5**

**PreyEngine** is a custom 2D game engine built with **MonoGame** + **ImGui.NET**.  
Created and maintained by **Arian Shahmohammadi**, founder of **Easyprey Studio**.

This repository is the official **Community / Source** release channel for PreyEngine.  
It is published under the MIT License with a mandatory attribution requirement.

> **Note on versioning:** STS 11.4 was not published as a separate community snapshot.  
> Development moved directly to **STS 11.5**, which bundles a stronger set of improvements in one release.

---

## About

PreyEngine is an Iranian-made 2D game engine designed around a clean C# architecture and a Unity-like workflow. **STS 11.5** continues the short-term feature line with animation and audio foundations, a clearer 2D rendering abstraction, and everything already established in prior Community builds (physics, standalone runtime/build, input, scripting, and editor tooling).

---

## Release Channels: STS vs LTS

| Channel | Focus | Support window |
|---------|--------|----------------|
| **STS** (Short-Term Support) | Rapid feature delivery | **Minimum ~1 month**, **maximum ~7 months** per STS generation |
| **LTS** (Long-Term Support) | Stability and improvement | **Minimum ~3 months**, **maximum ~1.5 years** per LTS generation |

- **STS** moves fast: new capabilities land quickly; the support window is intentionally shorter.
- **LTS** moves carefully: fewer breaking swings, emphasis on reliability and sustainable improvement within the generation.

**Critical and essential bug fixes** (crashes, data loss, security issues) are prioritized on both channels and are not limited solely by the normal support windows above.

Community snapshots are delayed builds of the mainline. Small fixes are usually bundled into the next snapshot. Delay timing is a **release target**, not a guaranteed SLA.

---

## What's New in STS 11.5

### Animation (experimental)
- Sprite animation model: clips, frames, loop modes
- `FrameSequencePlayer` and `IAnimationSource`
- Sprite sheet slicing workflow in the editor (`SpriteSheetSlicerModal`)

> **Experimental:** Animation features are in an early trial phase. They may contain bugs, incomplete paths, or behaviour that changes in later releases. Do not treat them as production-stable yet.

### Audio (experimental)
- `AudioContext` and `AudioSource`
- Editor-side audio icon / feedback hooks

> **Experimental:** Audio is also in an early trial phase. Playback, resource handling, and editor integration may be incomplete or unstable. Expect fixes and API adjustments in future snapshots.

### Rendering abstraction — `IRenderer2D`
- New interface **`IRenderer2D`**: a thin abstraction over 2D draw calls so gameplay and systems (sprites, animation, future backends) are not hard-wired to a single MonoGame `SpriteBatch` usage site
- Default implementation: **`MonoGameRenderer2D`**
- `RenderContext.Renderer2D` is the shared entry point used by editor Play Mode and the standalone Runtime

This does not replace MonoGame; it isolates draw submission so a different 2D backend can be plugged in later without rewriting every renderer consumer.

### Also carried from recent Community / LTS line
- Standalone **Runtime** + **File > Build** pipeline (self-contained, ReadyToRun, win-x64)
- Full custom **2D physics**
- Input System, Scripting + Hot Reload
- Editor polish (layout, theme, toolbar, content browser icons, panel title fix)

---

## Features Overview

### Core
- ECS (`GameObject`, `Component`, `Transform`)
- Scene + prefab systems (JSON)
- SpriteRenderer, Camera2D
- Input System
- Scripting + Hot Reload
- 2D Physics (Rigidbody / Colliders / World / Raycast)
- GameManifest (Editor ↔ Runtime)
- **IRenderer2D** + MonoGameRenderer2D
- **Animation** (experimental)
- **Audio** (experimental)

### Editor
- Hierarchy, Inspector, Viewport (Play Mode), Content Browser, Console
- Toolbar, layout, theme, icons
- Gizmos, camera gizmo, collider overlays
- Build Settings + Build
- Sprite sheet slicer (experimental animation workflow)

### Runtime
- Standalone player (`MyEngine.Runtime`)
- Depends only on Core — no editor assemblies in the final binary

### Launcher
- Avalonia UI, splash, project and editor version management

---

## Project Structure

```
PreyEngine-Source/
├── src/
│   ├── MyEngine.Core/            # ECS, Physics, Input, Scripting, Animation, Audio, Rendering
│   ├── MyEngine.Editor/          # ImGui editor + Build
│   ├── MyEngine.EditorFramework/
│   ├── MyEngine.Runtime/         # Standalone player
│   └── MyEngine.Launcher/        # Avalonia launcher
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

Standalone build: open a project in the Editor → **File > Build Settings…** → **File > Build**.  
Output appears under the project’s `Builds/` folder.

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

This is a delayed Community snapshot (**STS 11.5**).  
It is provided as-is. Animation and audio subsystems are explicitly experimental.  
Mainline development continues under the support windows described above.

Thank you for your interest in PreyEngine.
