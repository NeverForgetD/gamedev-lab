# 🗻 Marching Cubes Terrain System

Unity에서 Marching Cubes 알고리즘을 직접 구현한 실시간 지형 편집 및 렌더링 시스템입니다.


📺 **전체 플레이리스트** → [Marching Cubes Dev Log — Unity](https://www.youtube.com/playlist?list=PLMOHfwfnIA6YzE3734cBiDpQrf6P25GbO)

---

## 🛠️ Tech Stack

- **Engine:** Unity 6 (URP)
- **Language:** C#
- **Libraries:** Unity.Mathematics, Unity.Collections, Unity.Jobs, Unity.Burst
- **GPU:** ComputeBuffer, DrawMeshInstancedIndirect, Procedural Instancing (custom shader)

---

## ⚙️ 핵심 구현

### 1. 🔵 Density Field Visualization & Marching Cubes Mesh Generation

> ▶ [Density Field Visualization & Marching Cubes — Week 1](https://youtu.be/thkhvTRmsXE)

#### Density Field Visualization

메시를 생성하기 전에, 스칼라 필드 자체를 인스턴싱된 점들의 3D 격자로 시각화합니다. 샘플 포인트 하나당 점 하나를 그리며, 전 과정을 GPU로 처리하는 드로우 파이프라인을 사용합니다.

**📦 Data upload**  
모든 샘플 포인트는 `FieldData` struct(월드 위치 + 밀도 값)로 묶여, stride 16바이트의 `ComputeBuffer`로 GPU에 업로드됩니다. 버퍼는 필드가 수정될 때마다 갱신됩니다.

```csharp
public struct FieldData
{
    public float3 position;
    public float  density;   // 음수 = 표면 내부
}
```

**⚡ Instanced draw call**  
`Graphics.DrawMeshInstancedIndirect`는 점 개수와 무관하게 단 한 번의 드로우 콜만 제출합니다. indirect args 버퍼가 인덱스 개수·인스턴스 개수·base vertex를 담고 있어, CPU는 인스턴스별 루프에서 완전히 빠집니다.

**🎨 Procedural Instancing shader**  
각 인스턴스는 `unity_InstanceID`로 structured buffer에서 자신의 `FieldData` 항목을 읽어, GPU 상에서 TRS 행렬을 재구성하고 월드 공간에 스스로 배치됩니다. `MaterialPropertyBlock` 오버헤드가 전혀 없습니다.

**🔴🔵 Density-based color**  
셰이더는 `density`의 부호에 따라 분기합니다. 음수 값(등위면 내부)은 한 색으로, 양수 값(외부)은 다른 색으로 렌더링됩니다. 덕분에 필드 경계를 한눈에 읽을 수 있습니다.

#### Marching Cubes Mesh Generation

등위면은 매 프레임 밀도 격자의 모든 큐브를 순회(march)하며 추출됩니다. 표면이 어떤 엣지를 가로지르는 지점마다 삼각형을 생성합니다.

**🧮 CubeIndex bitmask**  
각 큐브마다 8개 꼭짓점의 밀도를 `isoLevel`과 비교합니다. 꼭짓점 하나가 `cubeIndex`(0–255)에 비트 하나씩을 기여해, 어떤 꼭짓점이 표면 내부에 있는지를 인코딩합니다.

```csharp
var cubeIndex = 0;
for (var i = 0; i < 8; i++)
    if (fieldBuffer[cubeCorners[i]].density < isoLevel)
        cubeIndex |= (1 << i);
```

**📋 Lookup table**  
`edgeTable[cubeIndex]`는 큐브의 12개 엣지 중 어떤 엣지가 교차하는지를 나타내는 12비트 마스크입니다. `triangleTable[cubeIndex]`는 해당 구성에서 삼각형을 이루는 엣지 인덱스를 나열합니다 — 큐브당 최대 5개 삼각형입니다. 두 테이블 모두 표준 256-entry Marching Cubes 테이블을 static 배열로 베이크해 둔 것입니다.

**🎚️ IsoLevel control**  
`isoLevel`은 런타임 파라미터로 노출됩니다. 슬라이더를 움직이면 밀도 데이터를 다시 생성하지 않고도 추출되는 표면이 스칼라 필드 안쪽/바깥쪽으로 이동합니다.

**〰️ Edge interpolation modes**  
교차하는 각 엣지 위 정점 위치는 두 꼭짓점 위치를 보간해 계산합니다. 네 가지 모드를 지원하며 Inspector에서 런타임에 전환할 수 있습니다:

| 모드 | 동작 |
|------|-----------|
| `Linear` | 등위면 교차점을 정확히 계산 — `t = (iso - v1) / (v2 - v1)` |
| `Smoothstep` | 같은 t를 쓰되 `3t² - 2t³`로 완만하게 처리 — 노멀이 더 부드러움 |
| `Half` | 항상 중점에 정점을 배치 (각진, Minecraft 스타일) |
| `Snapping` | 끝점 근처에서는 0 또는 1로 스냅, 그 외에는 선형 — 미세 삼각형을 줄임 |

**📐 LOD via step size**  
`lodStep` 파라미터는 march 시 격자 셀을 N개마다 건너뜁니다. 동일한 밀도 데이터를 재사용하면서 먼 청크의 삼각형 개수를 줄입니다.

---

### 2. 🌐 Density Field Types — SDF-based

밀도 필드는 Signed Distance Field 규약을 따라 해석적으로 생성됩니다. 음수 값은 표면 내부, 양수 값은 외부이며, 0을 지나는 지점(zero-crossing)이 메시를 정의합니다.

**🔮 Sphere SDF**  
각 점의 밀도는 `distance(point, center) - radius`입니다. 표면을 거칠게 만들기 위해 그 위에 가산형 fBm 노이즈를 얹을 수 있습니다.

**🏔️ Terrain2D**  
밀도는 `worldY - baseHeight - fBm2D(x, z)`입니다. 노이즈로 변위된 높이 표면보다 아래에 있는 점은 음수(고체), 위에 있는 점은 양수(공기)가 됩니다. 단 하나의 수식으로 무한 절차적 지형이 만들어집니다.

**🌊 Fractional Brownian Motion (fBm)**  
두 필드 타입 모두 Unity의 `noise.snoise` 기반 4-octave fBm을 지원합니다. lacunarity는 2.0, gain은 0.5로 고정되어 어떤 스케일에서도 자연스러운 지형을 만듭니다. frequency와 amplitude는 프로파일별로 조정할 수 있습니다.

```
value += snoise(p * freq) * amp   // 4회 반복, freq ×= 2, amp ×= 0.5
```

---

### 3. 🗺️ Chunk Streaming & Runtime Editing

> ▶ [Chunk Streaming, Object Pooling & Terrain Editor — Week 2](https://youtu.be/zwa8Fl17JaM)

**🧩 Chunk Manager**  
월드는 XZ 평면 위에서 고정 크기의 청크로 분할됩니다. 매 프레임 `ChunkManager`는 플레이어의 현재 청크 좌표를 계산하고, 모든 방향으로 `renderDistance`만큼의 정사각형 영역을 유지합니다. 영역을 벗어난 청크는 풀로 반환되고, 새로 필요한 청크는 풀에서 꺼내 생성 큐에 추가됩니다.

**♻️ Object Pooling**  
모든 청크 `GameObject`는 시작 시점에 미리 생성됩니다. 청크를 반환할 때는 `deltaField` 누산기를 리셋하고 메시를 비울 뿐, 런타임에 `Instantiate`/`Destroy`를 하지 않습니다. 풀 크기는 `(2 × renderDistance + 1)²`입니다.

**🚦 Throttled generation**  
생성 큐는 플레이어로부터의 맨해튼 거리 순(가까운 것부터)으로 정렬되며, 프레임당 최대 `chunksPerFrame`개만 처리합니다. 플레이어가 빠르게 이동할 때 발생하는 프레임 스파이크를 방지합니다.

**📷 Frustum Culling**  
매 프레임 `GeometryUtility.CalculateFrustumPlanes`로 카메라 프러스텀을 추출합니다. 활성 청크마다 AABB를 `TestPlanesAABB`로 검사해, 프러스텀 밖의 청크는 `MeshRenderer`를 비활성화합니다. CPU 스키닝과 GPU 드로우 제출을 모두 건너뜁니다.

**✏️ Real-time Terrain Editor**  
`TerrainEditor`는 매 프레임 카메라에서 지형 표면으로 레이캐스트를 쏩니다. 좌클릭(파기) 또는 우클릭(채우기) 시 `ChunkManager.GetChunksInRadius`로 브러시 반경 내 모든 청크를 찾아 각각에 대해 `ModifyDensity`를 호출합니다.

밀도 수정에는 2차(quadratic) falloff를 적용해, 브러시 경계에서 편집이 매끄럽게 느껴지도록 합니다:

```csharp
float t = 1f - sqrt(distSq) / radius;
deltaField[i] += delta * t * t;
```

delta 값은 별도의 `deltaField` 누산기에 저장되고, 필드가 갱신될 때마다 기본 SDF 위에 더해집니다. 덕분에 편집 아래에 있는 원래 표면 형태가 보존됩니다.

**🗂️ ScriptableObject Profile**  
모든 청크의 파라미터(resolution, world size, 필드 타입, 노이즈 설정, 구 반지름)는 `ChunkProfile` ScriptableObject에 저장됩니다. `UnitSize`는 `worldSize / resolution`으로 유도되어, 수동 관리 없이 두 값의 일관성을 유지합니다. 편집 시점에 프로파일을 교체하는 것만으로 월드 전체의 성격을 바꿀 수 있습니다.

---

### 4. 🚀 Optimization Pass — Pooling, Culling & LOD

> ▶ [Optimization Pass: Pooling, Culling & LOD — Week 3](https://youtu.be/pRhWY_OiF5Y)

이번 단계는 청크 개수가 늘어남에 따라 프레임당 CPU 비용을 줄이는 데 집중했습니다.

- ♻️ **Object Pooling**은 시작 시점에 고정 크기의 청크 GameObject 풀을 미리 채워 런타임 할당을 제거했습니다. 청크를 반환할 때는 메시를 비우기 전에 MeshCollider 참조를 안전하게 null로 만듭니다. 공유 메시 데이터가 해제됐는데도 collider가 여전히 그것을 참조하고 있을 때 발생하는 Unity 엔진 크래시를 막기 위함입니다.
- 📷 **Frustum Culling**은 매 프레임 화면 밖 청크의 MeshRenderer를 비활성화해, 청크를 파괴하지 않고도 GPU 제출을 줄입니다.
- 🗂️ **ScriptableObject refactor**로 월드 파라미터를 `ChunkProfile` 에셋으로 분리했습니다. resolution, world size, 노이즈 설정, 필드 타입을 이제 코드 수정 없이 편집 시점에 핫스왑할 수 있습니다.
- 📐 **LOD step**은 제너레이터에 `lodStep` 정수를 노출합니다. 먼 청크는 더 큰 step size로 march해, 같은 밀도 버퍼를 공유하면서도 더 성긴 메시를 만듭니다.

---

### 5. 🔧 Job System, Burst & Mesh Optimization

> ▶ [Job System, Burst & Mesh Optimization — Week 4](https://www.youtube.com/watch?v=zn2nTJvZuBg&list=PLMOHfwfnIA6YzE3734cBiDpQrf6P25GbO&index=4)

이번 단계는 생성 파이프라인 전체를 메인 스레드 밖으로 옮기고, 부드러운 셰이딩을 위한 정점 중복 제거(vertex deduplication), 거리 기반 LOD, 비동기 collider 베이킹을 도입했습니다.

---

#### 🧵 NativeArray & Job System

모든 밀도·메시 생성 로직을 Burst 컴파일이 적용된 Unity의 C# Job System으로 옮겼습니다.

**Data migration**  
`FieldData[]`(managed heap)를 `NativeArray<FieldData>`(`Allocator.Persistent`)로 교체했습니다. 이로써 GC 압력이 사라지고, Burst로 컴파일된 job에서 zero-copy로 접근할 수 있습니다. Marching Cubes 룩업 테이블(`edgeTable`, `triangleTable`)도 persistent `NativeArray<int>`로 옮겼습니다. Burst job은 managed static 배열을 읽을 수 없기 때문입니다.

**Parallel density generation**  
`CreateSphereJob`과 `CreateTerrain2DJob`은 `IJobParallelFor`를 구현해, 밀도 계산을 가용한 모든 워커 코어에 분산합니다. fBm 노이즈(`noise.snoise`)는 Burst와 완전히 호환되며 SIMD 벡터화로 실행됩니다.

**Parallel Marching Cubes**  
`MarchingCubesJob`은 모든 셀을 병렬로 march합니다. 각 워커는 독립적으로 cubeIndex를 계산하고, `FixedList512Bytes<float3>`(스택 할당, GC 없음)로 엣지 정점을 보간한 뒤, 결과 `Triangle` struct를 `NativeStream`에 씁니다. `NativeStream`은 각 job 인덱스에 고유한 메모리 버킷을 할당해 쓰기 경쟁(write contention)을 완전히 제거합니다 — atomic도, lock도 없습니다.

| | 이전 | 이후 |
|---|---|---|
| 실행 | 메인 스레드 (블로킹) | 워커 스레드 (비동기) |
| 처리 | 단일 스레드, managed C# | IJobParallelFor + Burst SIMD |
| 청크 빌드 시간 (resolution=16) | ~5–10 ms | ~0.3–0.8 ms |
| 처리량 향상 | — | **~10–20×** |

**Job dependency chain**  
메시 job이 밀도 필드를 읽는 도중에 지형 편집이 발생할 때 생기는 data race를 막기 위해, `SimpleDensityField`는 `RegisterReaderHandle(JobHandle)` API를 노출합니다. 제너레이터는 MC job을 스케줄링한 직후 그 핸들을 등록하고, 다음 밀도 쓰기 job은 `JobHandle.CombineDependencies`를 통해 이 핸들을 자동으로 의존성으로 상속받습니다.

```
MarchingCubesJob (DensityField 읽기)
      │  RegisterReaderHandle()
      ▼
CreateTerrain2DJob (DensityField 쓰기) — MC job이 먼저 끝나기를 대기
```

---

#### ✨ Vertex Caching & Smooth Shading

기존 파이프라인은 삼각형마다 독립적인 정점 3개를 생성했습니다. 모든 삼각형이 자기 꼭짓점을 따로 소유하고 공유하지 않았습니다. 그래서 `RecalculateNormals`가 평균 낼 대상이 없어, 평평한(faceted) 셰이딩이 나왔습니다.

**How vertex caching works**  
`NativeStream`에서 모든 삼각형을 수집한 뒤, 각 정점 위치를 `int3` 키(위치 × 10,000 후 반올림)로 양자화해 `NativeHashMap<int3, int>`에서 조회합니다. 일치하는 키가 있으면 기존 인덱스를 재사용하고, 없으면 새 정점을 삽입합니다. 이렇게 하면 이웃한 두 셀이 같은 엣지를 독립적으로 보간할 때 생기는 부동소수점 반올림 차이를 흡수할 수 있습니다.

| | 캐싱 없음 | 캐싱 적용 |
|---|---|---|
| 정점 개수 (resolution=16, ~50% 채움) | ~15,000 (삼각형 × 3) | ~5,000–6,000 (고유 정점) |
| 정점 버퍼 크기 | 기준 | **~65% 감소** |
| 노멀 셰이딩 | Flat (per-face 노멀, faceted) | **Smooth (평균 노멀)** |
| GPU 정점 처리량 | 기준 | 비례해서 감소 |

Inspector의 `Smooth Shading` 토글로 캐싱(smooth) 경로와 비캐싱(flat) 경로를 런타임에 전환할 수 있습니다. 스타일 비교나 의도적인 로우폴리 연출에 유용합니다.

---

#### 📐 Distance-Based LOD

기존부터 있던 `lodStep` 파라미터(march 시 셀을 N개마다 건너뜀)를 `ChunkManager`의 거리 인식 시스템에 연결했습니다. LOD 밴드는 Inspector에서 자유롭게 설정할 수 있고, 체크박스 하나로 켜고 끌 수 있습니다.

| 거리 (맨해튼) | LodStep | 삼각형 감소 | 설명 |
|---|---|---|---|
| ≤ 1 | 1 | — | 최고 해상도 (플레이어 바로 주변) |
| ≤ 3 | 2 | **75%** | 중거리 |
| > 3 | 4 | **94%** | 원거리 |

플레이어가 청크 경계를 넘으면 `UpdateChunkLOD`가 밴드가 바뀐 청크를 감지해 `LodStep`을 갱신하고 `TriggerRebuild()`를 호출합니다. 이때는 MC job만 다시 스케줄링할 뿐 밀도 재계산은 하지 않습니다. `NativeArray` 데이터가 이미 준비돼 있기 때문입니다.

| | LOD 없음 | LOD 적용 (renderDistance=3) |
|---|---|---|
| 전체 청크 | 49 | 49 |
| 전체 삼각형 | ~245,000 | ~57,000–95,000 |
| 삼각형 감소 | — | **~60–75%** |

---

#### 🧱 Asynchronous Collider Baking

이전에는 `meshCollider.sharedMesh = mesh` 할당이 메인 스레드에서 동기적 물리 베이크를 유발했습니다. 정점 개수에 따라 청크당 2–5 ms의 stall이 발생했습니다.

`BakeMeshJob`은 `Physics.BakeMesh(meshID, false)`를 감쌉니다. 이 함수는 Unity 2022+에서 워커 스레드 실행을 허용합니다. job은 `ApplyMesh`가 반환된 직후(메시 데이터는 이미 GPU에 있음) 스케줄링되고, collider 할당은 `bakeJobHandle.IsCompleted`가 true를 반환하는 프레임으로 미뤄집니다.

| | 이전 | 이후 |
|---|---|---|
| 베이크 실행 | 메인 스레드 (블로킹) | 워커 스레드 (비동기) |
| 메인 스레드 비용 | 2–5 ms / 청크 | **0 ms** |
| collider 적용 | 같은 프레임 (즉시) | 1–2 프레임 뒤 (조용히) |

먼 청크는(`Collider Max Lod Step`으로 설정) 베이킹을 아예 건너뜁니다. 플레이어가 도달할 수 없으므로 충돌 처리가 불필요하기 때문입니다.

---

#### ⚠️ Known Limitations & Future Work

**LOD seam artifacts**  
인접한 두 청크의 `LodStep` 값이 다르면, 공유하는 경계 엣지의 정점 밀도가 어긋납니다. 그 결과 경계를 따라 눈에 보이는 균열이나 겹친 삼각형이 생깁니다. 정석적인 해법은 **Transvoxel 알고리즘**(Eric Lengyel, 2010)입니다. 512-entry 룩업 테이블을 한 벌 더 두어, 해상도 불일치를 기하학적으로 연속된 삼각형으로 메우는 *transition cell*을 생성합니다. 구현하려면 청크마다 6개 이웃의 LOD 레벨을 모두 감지하고, 경계면별로 별도의 transition-cell 패스를 실행하며, 두 청크가 모두 완성된 뒤 봉합되도록 타이밍을 동기화해야 합니다 — 현재 파이프라인에 상당한 추가가 필요합니다.

현실적인 완화책으로, 씬 레벨의 거리 안개(distance fog)를 쓰면 LodStep이 가장 크고 아티팩트가 가장 작은 먼 청크에서 이음새를 가릴 수 있습니다.

**Chunk boundary normal discontinuities**  
부드러운 셰이딩은 청크별로 계산됩니다. 청크 경계 위 정점은 이웃 청크의 정점과 공유되지 않으므로, 노멀이 양쪽에서 독립적으로 계산됩니다. 그 결과 기하가 연속이어도 청크 모서리에 셰이딩 이음새가 보입니다. 이를 해결하려면 빌드 후 패스에서 각 청크가 경계 정점 노멀을 6개 이웃과 교환해 평균을 내야 합니다. 이는 교환 전에 두 청크 모두 MC job을 끝내야 함을 의미하며, `ChunkManager`에 조율 복잡도를 더합니다.

**Vertex deduplication on the main thread**  
`BuildSmooth`는 현재 MC job 완료 후 메인 스레드에서 `NativeHashMap` 조회 루프를 실행합니다. 고해상도 청크에서는 1–3 ms가 들 수 있습니다. 이를 전용 `IJob`(단일 스레드지만 메인 스레드 밖)으로 옮기거나, MC job이 미리 중복 제거된 데이터를 생성하도록 재구성하면 남은 메인 스레드 비용을 없앨 수 있습니다.

**Base density caching**  
지형을 편집할 때마다 `ScheduleRefreshField`가 모든 격자 점에 대해 전체 fBm 노이즈 수식을 다시 평가합니다. 노이즈는 월드 위치에만 의존하므로(고정된 청크에서는 결코 변하지 않음), 기본 밀도를 별도의 `NativeArray<float>`에 캐싱하고 편집 시에는 `deltaField` 덧셈만 다시 실행할 수 있습니다. 이렇게 하면 편집 시 job 비용을 fBm 평가 비중(밀도 job의 ~70–80%)만큼 줄일 수 있습니다.

**Mesh.AllocateWritableMeshData (Zero-Copy upload)**  
`mesh.SetVertices(NativeArray)`는 내부적으로 데이터를 managed 버퍼에 복사한 뒤 업로드합니다. `Mesh.AllocateWritableMeshData`는 GPU-ready 버퍼에 직접 써서 이 복사를 우회합니다. Burst로 컴파일된 노멀 계산 job과 결합하면, MC 이후의 전체 파이프라인(정점 중복 제거, 노멀 계산, 메시 업로드)을 managed heap을 건드리지 않고 실행할 수 있습니다.

---

## 📅 개발 일지

| 주차 | 핵심 내용 | 영상 |
|------|-------|-------|
| Week 1 | Marching Cubes 알고리즘 · Sphere SDF · 2D 지형 노이즈 · 밀도 필드 gizmo | [▶ Week 1](https://youtu.be/thkhvTRmsXE) |
| Week 2 | Chunk Manager · 지형 셰이더 · 플레이어 컨트롤러 · 실시간 지형 편집기 | [▶ Week 2](https://youtu.be/zwa8Fl17JaM) |
| Week 3 | 오브젝트 풀링 · 프러스텀 컬링 · ScriptableObject 리팩터링 · LOD step | [▶ Week 3](https://youtu.be/pRhWY_OiF5Y) |
| Week 4 | Job System · Burst 컴파일 · NativeStream · 정점 캐싱 · 부드러운 셰이딩 · 비동기 collider 베이킹 · 거리 LOD | [▶ Week 4](https://www.youtube.com/watch?v=zn2nTJvZuBg&list=PLMOHfwfnIA6YzE3734cBiDpQrf6P25GbO&index=4) |
