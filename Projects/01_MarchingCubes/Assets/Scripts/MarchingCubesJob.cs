// MarchingCubesJob.cs
// unsafe 없이 FixedList512Bytes<float3> + switch 헬퍼로 구현.
// ▸ stackalloc 제거 → FixedList (Unity.Collections, Burst 호환)
// ▸ static readonly int2[] 제거 → GetEdgePair() switch 헬퍼

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MarchingCubesJob : IJobParallelFor
{
    // ── 보간 모드 상수 ────────────────────────────────────────────────
    public const int MODE_LINEAR     = 0;
    public const int MODE_HALF       = 1;
    public const int MODE_SMOOTHSTEP = 2;
    public const int MODE_SNAPPING   = 3;

    // ── 입력 ─────────────────────────────────────────────────────────
    [ReadOnly] public NativeArray<FieldData> DensityField;
    [ReadOnly] public NativeArray<int>       EdgeTable;
    [ReadOnly] public NativeArray<int>       TriangleTable; // [cubeIndex*16 + slot]

    public int   Resolution;
    public int   PointsPerAxis;
    public float IsoLevel;
    public int   LodStep;
    public int   InterpolateMode;

    // ── 출력 ─────────────────────────────────────────────────────────
    [WriteOnly] public NativeStream.Writer Writer;

    // ── 실행 ─────────────────────────────────────────────────────────
    public void Execute(int cellIndex)
    {
        // ① 1D → 3D 좌표
        int cellsPerAxis = (Resolution + LodStep - 1) / LodStep;
        int cx = cellIndex % cellsPerAxis;
        int cy = (cellIndex / cellsPerAxis) % cellsPerAxis;
        int cz = cellIndex / (cellsPerAxis * cellsPerAxis);

        int x = cx * LodStep;
        int y = cy * LodStep;
        int z = cz * LodStep;

        if (x >= Resolution || y >= Resolution || z >= Resolution)
        {
            // 빈 구간도 BeginForEachIndex / End 가 필요
            Writer.BeginForEachIndex(cellIndex);
            Writer.EndForEachIndex();
            return;
        }

        int s   = LodStep;
        int pts = PointsPerAxis;

        // ② 8 코너 인덱스 (로컬 변수 — 배열 없이)
        int c0 = Flatten(x,   y,   z,   pts);
        int c1 = Flatten(x+s, y,   z,   pts);
        int c2 = Flatten(x+s, y,   z+s, pts);
        int c3 = Flatten(x,   y,   z+s, pts);
        int c4 = Flatten(x,   y+s, z,   pts);
        int c5 = Flatten(x+s, y+s, z,   pts);
        int c6 = Flatten(x+s, y+s, z+s, pts);
        int c7 = Flatten(x,   y+s, z+s, pts);

        // ③ cubeIndex
        int cubeIndex = 0;
        if (DensityField[c0].density < IsoLevel) cubeIndex |= 1;
        if (DensityField[c1].density < IsoLevel) cubeIndex |= 2;
        if (DensityField[c2].density < IsoLevel) cubeIndex |= 4;
        if (DensityField[c3].density < IsoLevel) cubeIndex |= 8;
        if (DensityField[c4].density < IsoLevel) cubeIndex |= 16;
        if (DensityField[c5].density < IsoLevel) cubeIndex |= 32;
        if (DensityField[c6].density < IsoLevel) cubeIndex |= 64;
        if (DensityField[c7].density < IsoLevel) cubeIndex |= 128;

        int edgeMask = EdgeTable[cubeIndex];

        Writer.BeginForEachIndex(cellIndex);

        if (edgeMask == 0)
        {
            Writer.EndForEachIndex();
            return;
        }

        // ④ 엣지 보간 → vertList (FixedList : unsafe 불필요, Burst 호환)
        // float3 × 12 = 144 bytes → FixedList512Bytes 로 충분
        var vertList = new FixedList512Bytes<float3>();
        for (int i = 0; i < 12; i++) vertList.Add(float3.zero);

        for (int i = 0; i < 12; i++)
        {
            if ((edgeMask & (1 << i)) == 0) continue;

            var   pair = GetEdgePair(i);
            int   ci   = GetCorner(pair.x, c0, c1, c2, c3, c4, c5, c6, c7);
            int   cj   = GetCorner(pair.y, c0, c1, c2, c3, c4, c5, c6, c7);
            float3 p1  = DensityField[ci].position;
            float3 p2  = DensityField[cj].position;
            float  v1  = DensityField[ci].density;
            float  v2  = DensityField[cj].density;
            vertList[i] = Interpolate(p1, p2, v1, v2);
        }

        // ⑤ 트라이앵글 출력
        int baseIdx = cubeIndex * 16;
        for (int i = 0; i < 15; i += 3)
        {
            int e0 = TriangleTable[baseIdx + i];
            if (e0 == -1) break;

            int e1 = TriangleTable[baseIdx + i + 1];
            int e2 = TriangleTable[baseIdx + i + 2];

            Writer.Write(new Triangle
            {
                a = vertList[e0],
                b = vertList[e1],
                c = vertList[e2]
            });
        }

        Writer.EndForEachIndex();
    }

    // ── 유틸 ─────────────────────────────────────────────────────────

    private static int Flatten(int x, int y, int z, int pts)
        => x + y * pts + z * pts * pts;

    /// <summary>엣지 번호 → (first corner, second corner) 쌍 반환</summary>
    private static int2 GetEdgePair(int edge)
    {
        return edge switch
        {
            0  => new int2(0, 1),
            1  => new int2(1, 2),
            2  => new int2(2, 3),
            3  => new int2(3, 0),
            4  => new int2(4, 5),
            5  => new int2(5, 6),
            6  => new int2(6, 7),
            7  => new int2(7, 4),
            8  => new int2(0, 4),
            9  => new int2(1, 5),
            10 => new int2(2, 6),
            11 => new int2(3, 7),
            _  => new int2(0, 0)
        };
    }

    /// <summary>코너 인덱스 0~7 → 실제 densityField 플랫 인덱스</summary>
    private static int GetCorner(int idx,
        int c0, int c1, int c2, int c3,
        int c4, int c5, int c6, int c7)
    {
        return idx switch
        {
            0 => c0, 1 => c1, 2 => c2, 3 => c3,
            4 => c4, 5 => c5, 6 => c6, 7 => c7,
            _ => c0
        };
    }

    // ── 보간 ─────────────────────────────────────────────────────────

    private float3 Interpolate(float3 p1, float3 p2, float v1, float v2)
    {
        float t = InterpolateMode switch
        {
            MODE_HALF       => 0.5f,
            MODE_SMOOTHSTEP => Smoothstep(v1, v2),
            MODE_SNAPPING   => Snapping(v1, v2),
            _               => Linear(v1, v2)
        };
        return p1 + t * (p2 - p1);
    }

    private float Linear(float v1, float v2)
        => (IsoLevel - v1) / (v2 - v1);

    private float Smoothstep(float v1, float v2)
    {
        float t = Linear(v1, v2);
        return t * t * (3f - 2f * t);
    }

    private static float Snapping(float v1, float v2, float snap = 0.2f)
    {
        // snap 계산은 IsoLevel 을 쓰지 않으므로 static 가능
        // (v2 - v1 == 0 방어는 Linear 에서 처리)
        float range = v2 - v1;
        if (math.abs(range) < 1e-6f) return 0.5f;
        float t = math.clamp((0f - v1) / range, 0f, 1f); // isoLevel=0 기준
        if (t < snap)       return 0f;
        if (t > 1f - snap)  return 1f;
        return t;
    }
}
