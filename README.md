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

## How to open

1. Clone the repo
2. Open **Unity Hub** → **Add project from disk** → select the repo root folder
3. Open the scene from `Assets/Scenes/`
4. Press Play

No manual setup needed — the scene file already has all parameters and shader assignments saved.

## References

- [Sebastian Lague — Coding Adventure: Ray Tracing](https://www.youtube.com/watch?v=Qz0KTGYJjUs)
