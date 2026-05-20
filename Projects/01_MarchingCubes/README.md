# 🗻 Marching Cubes Terrain System

A real-time voxel terrain system built in Unity, implementing the Marching Cubes algorithm from scratch.  
Supports runtime terrain editing, chunk streaming, frustum culling, and object pooling.

---

## 🛠️ Tech Stack

- **Engine:** Unity 6 (URP)
- **Language:** C#
- **Libraries:** Unity.Mathematics, Unity Input System
- **GPU:** ComputeBuffer, DrawMeshInstancedIndirect, Procedural Instancing (custom shader)

---

## ⚙️ Core Implementation

### 1. 🔵 Density Field Visualization & Marching Cubes Mesh Generation

> `[VIDEO 1]`

#### Density Field Visualization

Before generating any mesh, the scalar field itself is visualized as a 3D grid of instanced dots — one per sample point — using a fully GPU-driven draw pipeline.

**📦 Data upload**  
All sample points are packed into a `FieldData` struct (world position + density value) and uploaded to the GPU as a `ComputeBuffer` with a 16-byte stride. The buffer is refreshed whenever the field is modified.

```csharp
public struct FieldData
{
    public float3 position;
    public float  density;   // negative = inside surface
}
```

**⚡ Instanced draw call**  
`Graphics.DrawMeshInstancedIndirect` submits a single draw call regardless of the point count. The indirect args buffer holds index count, instance count, and base vertex — keeping the CPU entirely out of the per-instance loop.

**🎨 Procedural Instancing shader**  
Each instance reads its own `FieldData` entry from the structured buffer using `unity_InstanceID`, reconstructs a TRS matrix on the GPU, and positions itself in world space without any `MaterialPropertyBlock` overhead.

**🔴🔵 Density-based color**  
The shader branches on the sign of `density`: negative values (inside the isosurface) render in one color, positive values (outside) in another. This makes the field boundary immediately readable at a glance.

#### Marching Cubes Mesh Generation

The isosurface is extracted per-frame by marching through every cube in the density grid and emitting triangles wherever the surface crosses an edge.

**🧮 CubeIndex bitmask**  
For each cube, the 8 corner densities are compared against `isoLevel`. Each corner contributes one bit to a `cubeIndex` (0–255), encoding which corners lie inside the surface.

```csharp
var cubeIndex = 0;
for (var i = 0; i < 8; i++)
    if (fieldBuffer[cubeCorners[i]].density < isoLevel)
        cubeIndex |= (1 << i);
```

**📋 Lookup tables**  
`edgeTable[cubeIndex]` is a 12-bit mask indicating which of the 12 cube edges are intersected. `triangleTable[cubeIndex]` lists the edge indices that form triangles for that configuration — up to 5 triangles per cube. Both tables are the standard 256-entry Marching Cubes tables, baked as static arrays.

**🎚️ IsoLevel control**  
`isoLevel` is exposed as a runtime parameter. Sliding it shifts the extracted surface inward or outward through the scalar field without regenerating density data.

**〰️ Edge interpolation modes**  
Vertex positions on each intersected edge are computed by interpolating between the two corner positions. Four modes are available and switchable in the Inspector at runtime:

| Mode | Behaviour |
|------|-----------|
| `Linear` | Exact isosurface crossing — `t = (iso - v1) / (v2 - v1)` |
| `Smoothstep` | Same t, but eased with `3t² - 2t³` for softer normals |
| `Half` | Always places the vertex at the midpoint (blocky, Minecraft-style) |
| `Snapping` | Snaps to 0 or 1 near the endpoints, otherwise linear — reduces micro-triangles |

**📐 LOD via step size**  
A `lodStep` parameter skips every N grid cells when marching, reducing triangle count for distant chunks while reusing the same density data.

---

### 2. 🌐 Density Field Types — SDF-based

The density field is generated analytically using a Signed Distance Field convention: negative values are inside the surface, positive values are outside, and the zero-crossing defines the mesh.

**🔮 Sphere SDF**  
Density at each point is `distance(point, center) - radius`. Additive fBm noise can be layered on top to roughen the surface.

**🏔️ Terrain2D**  
Density is `worldY - baseHeight - fBm2D(x, z)`. Points below the noise-displaced height surface are negative (solid); points above are positive (air). This produces infinite procedural terrain from a single formula.

**🌊 Fractional Brownian Motion (fBm)**  
Both field types support 4-octave fBm built on Unity's `noise.snoise`. Lacunarity is fixed at 2.0 and gain at 0.5, giving natural-looking terrain at any scale. Frequency and amplitude are tunable per-profile.

```
value += snoise(p * freq) * amp   // repeated 4×, freq ×= 2, amp ×= 0.5
```

---

### 3. 🗺️ Chunk Streaming & Runtime Editing

> `[VIDEO 2]`

**🧩 Chunk Manager**  
The world is divided into fixed-size chunks on the XZ plane. Each frame, `ChunkManager` computes the player's current chunk coordinate and maintains a square region of `renderDistance` chunks in every direction. Chunks that leave the region are returned to a pool; newly needed chunks are pulled from the pool and added to a generation queue.

**♻️ Object Pooling**  
All chunk `GameObject`s are pre-instantiated at startup. Returning a chunk resets its `deltaField` accumulator and clears the mesh — no `Instantiate`/`Destroy` at runtime. Pool size is `(2 × renderDistance + 1)²`.

**🚦 Throttled generation**  
The generation queue is sorted by Manhattan distance from the player (nearest first) and processes at most `chunksPerFrame` entries per frame, preventing frame spikes when the player moves quickly.

**📷 Frustum Culling**  
Every frame, `GeometryUtility.CalculateFrustumPlanes` extracts the camera frustum. Each active chunk's AABB is tested with `TestPlanesAABB`; chunks outside the frustum have their `MeshRenderer` disabled, skipping both CPU skinning and GPU draw submission.

**✏️ Real-time Terrain Editor**  
`TerrainEditor` raycasts from the camera to the terrain surface each frame. On left-click (dig) or right-click (fill), it queries `ChunkManager.GetChunksInRadius` to find all chunks within the brush radius and calls `ModifyDensity` on each one.

Density modification uses a quadratic falloff so edits feel smooth at the brush boundary:

```csharp
float t = 1f - sqrt(distSq) / radius;
deltaField[i] += delta * t * t;
```

The delta is stored in a separate `deltaField` accumulator and added on top of the base SDF every time the field is refreshed, preserving the original surface shape underneath edits.

**🗂️ ScriptableObject Profile**  
Every chunk's parameters (resolution, world size, field type, noise settings, sphere radius) are stored in a `ChunkProfile` ScriptableObject. `UnitSize` is derived as `worldSize / resolution`, keeping the two values consistent without manual bookkeeping. Swapping profiles at edit time is enough to change the entire world's character.

---

### 4. 🚀 Optimization Pass — Pooling, Culling & LOD

> `[VIDEO 3]`

This pass focused on reducing per-frame CPU cost as chunk count scaled up.

- ♻️ **Object Pooling** eliminated runtime allocation by pre-warming a fixed pool of chunk GameObjects at startup. Returning a chunk safely nulls the MeshCollider reference before clearing the mesh, avoiding a Unity engine crash when shared mesh data is freed while the collider still holds it.
- 📷 **Frustum Culling** disables MeshRenderer on out-of-view chunks each frame, cutting GPU submissions without destroying the chunk.
- 🗂️ **ScriptableObject refactor** decoupled world parameters into a `ChunkProfile` asset. Resolution, world size, noise settings, and field type are now hot-swappable at edit time without touching code.
- 📐 **LOD step** exposes a `lodStep` integer on the generator. Distant chunks march with a larger step size, producing sparser meshes while sharing the same density buffer.

---

### 5. 🔧 Job System & Mesh Threading *(In Progress)*

Currently, the Marching Cubes loop runs on the main thread inside `Update`. For a `resolution` of 32, a single chunk generates ~32,768 cube evaluations per frame — and multiple chunks loading simultaneously stall the frame.

**🧵 Planned approach**  
The mesh generation loop will be ported to Unity's Job System with Burst compilation. The marching loop body (`MarchCube`) is a pure function with no managed allocations, making it a natural fit for `IJobParallelFor`. Each job processes a slice of the XZ grid independently; results are written into a `NativeList<Triangle>` and flushed to the `Mesh` API via `Mesh.ApplyAndDisposeWritableMeshData` on the main thread.

Moving generation off the main thread means chunks can be built over several frames without a hitch, decoupled from the throttle-per-frame workaround currently used.

**✨ Smooth shading**  
The current pipeline emits unshared vertices — every triangle gets its own three vertices — so `RecalculateNormals` produces flat (faceted) shading. Smooth shading requires shared vertices: adjacent triangles must reference the same vertex index so Unity can average normals across the shared edge.

This means replacing the current append-only vertex list with a dictionary-based deduplication pass: for each computed edge vertex, look up whether a vertex at that world position was already emitted; if so, reuse its index; if not, insert it. The tradeoff is a more complex build step but significantly better visual quality and a smaller vertex buffer.

---

## 📅 Development Timeline

| Week | Focus | Video |
|------|-------|-------|
| Week 1 | Marching Cubes algorithm · Sphere SDF · 2D terrain noise · Density field gizmo | `[VIDEO 1]` |
| Week 2 | Chunk Manager · Terrain shader · Player controller · Real-time terrain editor | `[VIDEO 2]` |
| Week 3 | Object pooling · Frustum culling · ScriptableObject refactor · LOD step | `[VIDEO 3]` |
| Week 4 | Job System · Burst compilation · Threaded mesh build · Smooth shading *(in progress)* | — |
