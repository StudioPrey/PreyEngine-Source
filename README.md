# PreyEngine Source (Community Edition)

**LTS 1 — Eternal Rain**  
**Snapshot: LTS 1.1**

**PreyEngine** is a custom 2D game engine built with **MonoGame** + **ImGui.NET**.  
Created and maintained by **Arian Shahmohammadi**, founder of **Easyprey Studio**.

This repository is the official **Community / Source** release channel for PreyEngine.  
It is published under the MIT License with a mandatory attribution requirement.

---

## About

PreyEngine is an Iranian-made 2D game engine designed around a clean C# architecture and a Unity-like workflow. **LTS 1 (Eternal Rain)** is the first Long-Term Support generation and focuses on stability and refinement. This snapshot (**LTS 1.1**) adds a full custom 2D physics stack on top of the existing editor, input, and scripting foundations.

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

## What's New in LTS 1.1 (Eternal Rain)

### 2D Physics System
- Custom physics stack inspired by common engine patterns (abstract backend + engine integration)
- `IPhysicsBackend2D` with a managed backend implementation
- `PhysicsWorld2D`, `Rigidbody2D`, `Collider2D`
- `BoxCollider2D`, `CircleCollider2D`
- Collision detection/response helpers, raycast support
- Body types and core 2D math utilities (`AABB2D`, `Vec2`, etc.)
- Editor integration: component UI, collider gizmo overlay in the viewport, scene/prefab serialization

### Known v1 physics limits (honest scope)
- Discrete collision only (no continuous collision detection — fast bodies may tunnel thin colliders)
- No sleeping bodies yet
- Friction / restitution per collider (no shared physics material asset in v1)

### Also included (from prior Community line)
- Input System (keyboard / mouse / pointer, touch-ready)
- Scripting System with hot reload
- Camera gizmo, ProjectSettings, dirty tracking, drag-and-drop fixes
- Transform serialization fix and related editor stability work

---

## Features Overview

### Core
- ECS (`GameObject`, `Component`, `Transform`)
- Scene + prefab systems (JSON)
- SpriteRenderer, Camera2D
- Input System
- Scripting + Hot Reload
- **2D Physics** (Rigidbody / Colliders / World / Raycast)

### Editor
- Hierarchy, Inspector, Viewport (Play Mode), Content Browser, Console
- Gizmos (World / Local), camera gizmo, collider overlays
- Undo/Redo, adaptive grid, scene tabs
- Live asset import, drag & drop, ProjectSettings

### Launcher
- Avalonia UI, splash, project and editor version management

---

## Project Structure

```
PreyEngine-Source/
├── src/
│   ├── MyEngine.Core/         # Core, Input, Scripting, Physics
│   ├── MyEngine.Editor/       # ImGui editor
│   ├── MyEngine.EditorFramework/
│   └── MyEngine.Launcher/     # Avalonia launcher
├── MyEngine.sln
├── LICENSE
└── README.md
```

> Internal namespaces still use the historical name `MyEngine`.  
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

dotnet run --project src/MyEngine.Editor
dotnet run --project src/MyEngine.Launcher
```

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

This is a delayed Community snapshot of **LTS 1 — Eternal Rain** (LTS 1.1).  
It is provided as-is. Mainline development continues under the policies described above.

Thank you for your interest in PreyEngine.
