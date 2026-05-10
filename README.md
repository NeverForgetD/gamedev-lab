# Marching Cubes

Marching Cubes 알고리즘 구현 프로젝트입니다.

---

## 주요 구현

### Density Field 시각화
GPU Instancing을 활용해 밀도장의 각 격자 점을 실시간으로 렌더링합니다.

- `ComputeBuffer`에 위치·밀도 데이터를 올려 GPU에 전달
- `DrawMeshInstancedIndirect`로 격자 점 수만큼 인스턴싱 드로우
- 커스텀 셰이더(Procedural Instancing)로 인스턴스별 위치를 버퍼에서 읽어 TRS 행렬 재구성
- density 부호에 따라 색상 분기 (내부 / 외부)

---

### Marching Cubes 메시 생성
밀도장의 등위면(isosurface)을 삼각형 메시로 실시간 변환합니다.

- 큐브 단위로 8개 코너 density를 읽어 `cubeIndex`(0~255) 비트마스크 계산
- `edgeTable` / `triangleTable` LookupTable로 삼각형 조합
- `isoLevel` 파라미터로 등위면 위치 조절
- 엣지 위 정점 위치를 보간으로 계산하며, Linear / Smoothstep / Snapping 등 다양한 보간 방식을 인스펙터에서 전환 가능

---

### Density Field 타입

**Sphere**
- SDF(Signed Distance Field) 방식
- `density = distance(pos, center) - radius`

**Terrain2D**
- 2D Simplex Noise 기반 fBm(Fractional Brownian Motion)으로 지형 표면 높이 생성
- `density = pos.y - surfaceHeight(x, z)`
- octaves / lacunarity / gain 파라미터로 지형 디테일 조절

---

## 사용 기술

- Unity 6 / URP
- Unity.Mathematics (Simplex Noise)
- GPU Instancing / ComputeBuffer
- Marching Cubes Algorithm
