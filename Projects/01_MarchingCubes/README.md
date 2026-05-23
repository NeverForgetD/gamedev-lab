# 🗻 Marching Cubes Terrain System

A real-time voxel terrain system built in Unity, implementing the Marching Cubes algorithm from scratch.  
Supports runtime terrain editing, chunk streaming, frustum culling, and object pooling.

📺 **Full Playlist** → [Marching Cubes Dev Log — Unity](https://www.youtube.com/playlist?list=PLMOHfwfnIA6YzE3734cBiDpQrf6P25GbO)

---

## 🛠️ Tech Stack

- **Engine:** Unity 6 (URP)
- **Language:** C#
- **Libraries:** Unity.Mathematics, Unity.Collections, Unity.Jobs, Unity.Burst, Unity Input System
- **GPU:** ComputeBuffer, DrawMeshInstancedIndirect, Procedural Instancing (custom shader)

---

## ⚙️ Core Implementation

### 1. 🔵 Density Field Visualization & Marching Cubes Mesh Generation

> ▶ [Density Field Visualization & Marching Cubes — Week 1](https://youtu.be/thkhvTRmsXE)

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

> ▶ [Chunk Streaming, Object Pooling & Terrain Editor — Week 2](https://youtu.be/zwa8Fl17JaM)

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

> ▶ [Optimization Pass: Pooling, Culling & LOD — Week 3](https://youtu.be/pRhWY_OiF5Y)

This pass focused on reducing per-frame CPU cost as chunk count scaled up.

- ♻️ **Object Pooling** eliminated runtime allocation by pre-warming a fixed pool of chunk GameObjects at startup. Returning a chunk safely nulls the MeshCollider reference before clearing the mesh, avoiding a Unity engine crash when shared mesh data is freed while the collider still holds it.
- 📷 **Frustum Culling** disables MeshRenderer on out-of-view chunks each frame, cutting GPU submissions without destroying the chunk.
- 🗂️ **ScriptableObject refactor** decoupled world parameters into a `ChunkProfile` asset. Resolution, world size, noise settings, and field type are now hot-swappable at edit time without touching code.
- 📐 **LOD step** exposes a `lodStep` integer on the generator. Distant chunks march with a larger step size, producing sparser meshes while sharing the same density buffer.

---

### 5. 🔧 Job System, Burst & Mesh Optimization

> ▶ [Job System, Burst & Mesh Optimization — Week 4](https://www.youtube.com/watch?v=zn2nTJvZuBg&list=PLMOHfwfnIA6YzE3734cBiDpQrf6P25GbO&index=4)

This pass ported the entire generation pipeline off the main thread and introduced vertex deduplication for smooth shading, distance-based LOD, and asynchronous collider baking.

---

#### 🧵 NativeArray & Job System

All density and mesh generation logic was moved to Unity's C# Job System with Burst compilation.

**Data migration**  
`FieldData[]` (managed heap) was replaced with `NativeArray<FieldData>` (`Allocator.Persistent`). This eliminates GC pressure and allows zero-copy access from Burst-compiled jobs. The Marching Cubes lookup tables (`edgeTable`, `triangleTable`) were also migrated to persistent `NativeArray<int>`, since Burst jobs cannot read managed static arrays.

**Parallel density generation**  
`CreateSphereJob` and `CreateTerrain2DJob` implement `IJobParallelFor`, distributing density evaluation across all available worker cores. fBm noise (`noise.snoise`) is fully Burst-compatible and executes with SIMD vectorization.

**Parallel Marching Cubes**  
`MarchingCubesJob` marches all cells in parallel. Each worker independently computes a cubeIndex, interpolates edge vertices using `FixedList512Bytes<float3>` (stack-allocated, no GC), and writes resulting `Triangle` structs to a `NativeStream`. `NativeStream` assigns each job index its own memory bucket, eliminating write contention entirely — no atomics, no locks.

| | Before | After |
|---|---|---|
| Execution | Main thread (blocking) | Worker threads (async) |
| Processing | Single-threaded, managed C# | IJobParallelFor + Burst SIMD |
| Chunk build time (resolution=16) | ~5–10 ms | ~0.3–0.8 ms |
| Throughput gain | — | **~10–20×** |

**Job dependency chain**  
To prevent data races when terrain editing fires while a mesh job is still reading the density field, `SimpleDensityField` exposes a `RegisterReaderHandle(JobHandle)` API. The generator registers its MC job handle immediately after scheduling; the next density write job automatically inherits it as a dependency via `JobHandle.CombineDependencies`.

```
MarchingCubesJob (reads DensityField)
      │  RegisterReaderHandle()
      ▼
CreateTerrain2DJob (writes DensityField) — waits for MC job to finish first
```

---

#### ✨ Vertex Caching & Smooth Shading

The original pipeline emitted three independent vertices per triangle — every triangle owned its own corners with no sharing. `RecalculateNormals` had nothing to average across, producing flat (faceted) shading.

**How vertex caching works**  
After collecting all triangles from the `NativeStream`, each vertex position is quantized to a `int3` key (position × 10,000, rounded) and looked up in a `NativeHashMap<int3, int>`. If a matching key exists, the existing index is reused; otherwise a new vertex is inserted. This absorbs floating-point rounding differences that arise when two neighboring cells independently interpolate the same edge.

| | Without Caching | With Caching |
|---|---|---|
| Vertex count (resolution=16, ~50% fill) | ~15,000 (triangles × 3) | ~5,000–6,000 (unique vertices) |
| Vertex buffer size | baseline | **~65% smaller** |
| Normal shading | Flat (per-face normals, faceted) | **Smooth (averaged normals)** |
| GPU vertex throughput | baseline | reduced proportionally |

A `Smooth Shading` toggle in the Inspector switches between the cached (smooth) and uncached (flat) path at runtime — useful for style comparisons or intentional low-poly aesthetics.

---

#### 📐 Distance-Based LOD

The pre-existing `lodStep` parameter (which skips every N cells when marching) was wired up to a distance-aware system in `ChunkManager`. LOD bands are fully configurable in the Inspector and can be toggled on/off with a single checkbox.

| Distance (Manhattan) | LodStep | Triangle reduction | Description |
|---|---|---|---|
| ≤ 1 | 1 | — | Full resolution (player's immediate surroundings) |
| ≤ 3 | 2 | **75%** | Mid-range |
| > 3 | 4 | **94%** | Far distance |

When the player crosses a chunk boundary, `UpdateChunkLOD` detects which chunks changed band, updates their `LodStep`, and calls `TriggerRebuild()`. This re-schedules only the MC job — no density recalculation — since the `NativeArray` data is already in place.

| | Without LOD | With LOD (renderDistance=3) |
|---|---|---|
| Total chunks | 49 | 49 |
| Total triangles | ~245,000 | ~57,000–95,000 |
| Triangle reduction | — | **~60–75%** |

---

#### 🧱 Asynchronous Collider Baking

Previously, assigning `meshCollider.sharedMesh = mesh` triggered a synchronous physics bake on the main thread — a 2–5 ms stall per chunk depending on vertex count.

`BakeMeshJob` wraps `Physics.BakeMesh(meshID, false)`, which Unity 2022+ allows to run on worker threads. The job is scheduled immediately after `ApplyMesh` returns (mesh data is already on the GPU), and the collider assignment is deferred to the frame `bakeJobHandle.IsCompleted` returns true.

| | Before | After |
|---|---|---|
| Bake execution | Main thread (blocking) | Worker thread (async) |
| Main thread cost | 2–5 ms / chunk | **0 ms** |
| Collider applied | Same frame (immediate) | 1–2 frames later (silent) |

For distant chunks (configurable via `Collider Max Lod Step`), baking is skipped entirely — the player cannot reach them, so collision is unnecessary.

---

#### ⚠️ Known Limitations & Future Work

**LOD seam artifacts**  
When two adjacent chunks have different `LodStep` values, their shared border edge has mismatched vertex densities. This produces visible cracks or overlapping triangles along the boundary. The canonical solution is the **Transvoxel algorithm** (Eric Lengyel, 2010): a second set of 512-entry lookup tables generates *transition cells* that bridge the resolution mismatch with geometrically continuous triangles. Implementation requires detecting all six neighbour LOD levels per chunk, running a separate transition-cell pass per boundary face, and synchronising timing so both chunks are complete before stitching — a significant addition to the current pipeline.

As a practical mitigation, scene-level distance fog can hide the seams on far chunks where LodStep is highest and the artifacts are smallest.

**Chunk boundary normal discontinuities**  
Smooth shading is computed per-chunk. Vertices on a chunk's border are not shared with the neighbouring chunk's vertices, so normals are computed independently on each side — producing a visible shading seam at chunk edges even when the geometry is continuous. Resolving this requires a post-build pass where each chunk exchanges its border vertex normals with its six neighbours and averages them. This demands that both chunks finish their MC jobs before the exchange can occur, adding coordination complexity to `ChunkManager`.

**Vertex deduplication on the main thread**  
`BuildSmooth` currently runs the `NativeHashMap` lookup loop on the main thread after the MC job completes. For high-resolution chunks this can cost 1–3 ms. Moving this into a dedicated `IJob` (single-threaded but off main thread) or restructuring the MC job to emit pre-deduplicated data would eliminate the remaining main-thread cost.

**Base density caching**  
Every terrain edit calls `ScheduleRefreshField`, which re-evaluates the full fBm noise formula for every grid point. Since noise only depends on world position (which never changes for a stationary chunk), the base density could be cached in a separate `NativeArray<float>` and only the `deltaField` addition re-run on edit — reducing edit-time job cost by roughly the fBm evaluation fraction (~70–80% of the density job).

**Mesh.AllocateWritableMeshData (Zero-Copy upload)**  
`mesh.SetVertices(NativeArray)` internally copies data into a managed buffer before uploading. `Mesh.AllocateWritableMeshData` bypasses this copy by writing directly into a GPU-ready buffer. Combined with a Burst-compiled normal calculation job, this would make the entire post-MC pipeline — vertex dedup, normal computation, mesh upload — run without touching the managed heap.

---

## 📅 Development Timeline

| Week | Focus | Video |
|------|-------|-------|
| Week 1 | Marching Cubes algorithm · Sphere SDF · 2D terrain noise · Density field gizmo | [▶ Week 1](https://youtu.be/thkhvTRmsXE) |
| Week 2 | Chunk Manager · Terrain shader · Player controller · Real-time terrain editor | [▶ Week 2](https://youtu.be/zwa8Fl17JaM) |
| Week 3 | Object pooling · Frustum culling · ScriptableObject refactor · LOD step | [▶ Week 3](https://youtu.be/pRhWY_OiF5Y) |
| Week 4 | Job System · Burst compilation · NativeStream · Vertex caching · Smooth shading · Async collider baking · Distance LOD | [▶ Week 4](https://www.youtube.com/watch?v=zn2nTJvZuBg&list=PLMOHfwfnIA6YzE3734cBiDpQrf6P25GbO&index=4) |
