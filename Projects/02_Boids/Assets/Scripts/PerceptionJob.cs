using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

/// <summary>
/// 4단계 — 인지(SAC 누산)를 멀티코어로 병렬 처리하는 Job.
/// IJobParallelFor가 Boid 인덱스를 코어별로 쪼개 동시에 Execute한다.
/// 각 워커는 입력 배열(boids)을 읽기만 하고, 자기 인덱스의 results에만 쓴다 → 경쟁 없음.
///
/// 누산 로직은 SinglePass(BoidsManager.Accumulate)와 동일 → 동작 보존.
/// (Burst 미적용 버전: 패키지 없이 멀티코어 병렬화 효과만 측정)
/// </summary>
public struct PerceptionJobBruteForce : IJobParallelFor
{
    [ReadOnly] public NativeArray<BoidData> boids;
    public NativeArray<PerceptionResult> results;

    public float perceptSqr;
    public float sepSqr;

    public void Execute(int i)
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

        results[i] = p;
    }
}
