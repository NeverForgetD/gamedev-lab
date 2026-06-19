using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 4a — 인지(SAC 누산)를 멀티코어로 병렬 처리하는 Job (Burst 미적용).
/// IJobParallelFor가 Boid 인덱스를 코어별로 쪼개 동시에 Execute한다.
/// 각 워커는 입력(boids)을 읽기만 하고 자기 인덱스의 results에만 쓴다 → 경쟁 없음.
/// </summary>
public struct PerceptionJobBruteForce : IJobParallelFor
{
    [ReadOnly] public NativeArray<BoidData> boids;
    public NativeArray<PerceptionResult> results;
    public float perceptSqr;
    public float sepSqr;

    public void Execute(int i)
        => results[i] = PerceptionMath.ComputeBruteForce(i, boids, perceptSqr, sepSqr);
}

/// <summary>
/// 4b — 4a와 동일하지만 [BurstCompile]로 C#을 네이티브 머신코드로 컴파일한다.
/// 같은 병렬 처리에 SIMD·최적화가 더해져 추가 가속.
/// </summary>
[BurstCompile]
public struct PerceptionJobBurst : IJobParallelFor
{
    [ReadOnly] public NativeArray<BoidData> boids;
    public NativeArray<PerceptionResult> results;
    public float perceptSqr;
    public float sepSqr;

    public void Execute(int i)
        => results[i] = PerceptionMath.ComputeBruteForce(i, boids, perceptSqr, sepSqr);
}

/// <summary>
/// Job들이 공유하는 인지 누산 로직. Burst·비Burst 양쪽에서 같은 결과를 보장한다.
/// 누산 규칙은 BoidsManager.Accumulate(단일 스레드)와 동일 → 동작 보존.
/// </summary>
public static class PerceptionMath
{
    public static PerceptionResult ComputeBruteForce(int i, in NativeArray<BoidData> boids,
                                                     float perceptSqr, float sepSqr)
    {
        PerceptionResult p = default;
        Vector3 posI = boids[i].position;
        int n = boids.Length;

        for (int j = 0; j < n; j++)
        {
            if (i == j) continue;

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

        return p;
    }
}
