using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// 4c — Jobs + Burst + Spatial Grid 결합. 인지를 멀티코어 병렬 + 네이티브 컴파일로 처리하되,
/// 각 Boid는 NativeParallelMultiHashMap에서 주변 27셀의 후보만 꺼내 검사한다(O(N·k)).
///
/// 그리드 키는 셀 좌표 3개를 int 해시로 합친 값. 해시 충돌은 "엉뚱한 셀 후보가 섞임"으로만
/// 이어지는데, 거리 검사(sqr < perceptSqr)가 걸러내므로 결과는 항상 정확하다(성능만 미세 손해).
/// </summary>
[BurstCompile]
public struct PerceptionGridJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<BoidData> boids;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> grid;
    public NativeArray<PerceptionResult> results;

    public float perceptSqr;
    public float sepSqr;
    public float invCellSize;

    public void Execute(int i)
    {
        PerceptionResult p = default;
        Vector3 posI = boids[i].position;

        int cx = (int)math.floor(posI.x * invCellSize);
        int cy = (int)math.floor(posI.y * invCellSize);
        int cz = (int)math.floor(posI.z * invCellSize);

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int key = Hash(cx + dx, cy + dy, cz + dz);

            if (grid.TryGetFirstValue(key, out int j, out var it))
            {
                do
                {
                    if (j == i) continue;

                    Vector3 offset = posI - boids[j].position;
                    float sqr = offset.sqrMagnitude;
                    if (sqr <= 0f || sqr >= perceptSqr) continue;

                    p.alignmentSum += boids[j].direction;
                    p.cohesionSum  += boids[j].position;
                    p.flockCount++;

                    if (sqr < sepSqr)
                    {
                        p.separationSum += offset / sqr;
                        p.separationCount++;
                    }
                }
                while (grid.TryGetNextValue(out j, ref it));
            }
        }

        results[i] = p;
    }

    /// <summary>셀 좌표 3개 → int 해시 (큰 소수 곱 XOR, 표준 spatial hash).</summary>
    public static int Hash(int x, int y, int z)
        => (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
}
