# Ray Tracing Spheres and Triangles

An extended ray tracing renderer built in Unity using compute shaders, based on Sebastian Lague's ray tracing video tutorials. Adds triangle mesh support on top of sphere rendering.

## What it does

- Real-time ray tracing via a compute shader running on the GPU
- Supports spheres and triangle meshes
- Progressive rendering — accumulates samples over frames for anti-aliasing
- Procedural checkerboard material support

## Requirements

- Unity 2022.3 or later
- GPU with compute shader support (DirectX 11 / Metal)

## Rendering pipeline note

This project uses Unity's **Built-in renderer** — URP is intentionally disabled in `Project Settings → Graphics`. Compute shader ray tracing requires the camera image effect pipeline, which doesn't work with URP active. This is already committed in `ProjectSettings/`, so no manual change is needed after cloning.

## How to open

1. Clone the repo
2. Open **Unity Hub** → **Add project from disk** → select the repo root folder
3. In the Project panel, double-click `Assets/Scenes/SampleScene.unity` ← required on first open; Unity remembers it after that
4. Press Play

No manual setup needed — scene, parameters, and project settings are all committed.

## References

- [Sebastian Lague — Coding Adventure: Ray Tracing](https://www.youtube.com/watch?v=Qz0KTGYJjUs)
