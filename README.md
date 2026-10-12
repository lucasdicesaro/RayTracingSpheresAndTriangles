# Ray Tracing Spheres and Triangles

An extended ray tracing renderer built in Unity as a full-screen fragment shader, based on Sebastian Lague's ray tracing video tutorials. Adds triangle mesh support on top of sphere rendering.

## What it does

- Real-time ray tracing in a fragment shader, applied as a camera image effect (`OnRenderImage` + `Graphics.Blit`)
- Supports spheres and triangle meshes
- Progressive rendering — accumulates samples over frames for anti-aliasing
- Procedural checkerboard material support
- Two independent scenes: **CornellBox** and **Chess**

## Requirements

- Unity 2022.3 or later
- GPU with Shader Model 5 support (DirectX 11 / Metal)

## Rendering pipeline note

This project uses Unity's **Built-in renderer** — URP is intentionally disabled in `Project Settings → Graphics`. The renderer runs in `OnRenderImage`, the camera image-effect hook, which URP never calls. This is already committed in `ProjectSettings/`, so no manual change is needed after cloning.

To disable URP by hand (for example if it gets re-enabled):

1. **Edit → Project Settings → Graphics** → set **Default Render Pipeline** to **None**
2. **Edit → Project Settings → Quality** → for each quality level, set **Render Pipeline Asset** to **None**
3. Press Play; the Game view should show the ray traced image instead of the plain scene

## How to open

1. Clone the repo
2. Open **Unity Hub** → **Add project from disk** → select the repo root folder
3. In the Project panel, open `Assets/Scenes/CornellBox.unity` or `Assets/Scenes/Chess.unity`
4. Press Play

No manual setup needed — scene, parameters, and project settings are all committed.

## Controls (Play mode)

| Key | Action |
|-----|--------|
| **Space** | Pause / resume accumulation (the last image stays on screen) |
| **Right arrow** | Trace one more frame while paused |
| **R** | Restart accumulation from scratch |

## References

- [Sebastian Lague — Coding Adventure: Ray Tracing](https://www.youtube.com/watch?v=Qz0KTGYJjUs)
