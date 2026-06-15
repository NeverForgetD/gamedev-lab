using UnityEngine;

/// <summary>
/// 한 Boid가 이웃을 1회 순회하며 누산한 인지 결과.
/// BoidsManager(인지 담당)가 채우고, BoidController(조향 담당)가 소비한다.
/// 누산값만 담고 평균/조향은 BoidController가 계산해 책임을 분리한다.
/// </summary>
public struct PerceptionResult
{
    public Vector3 separationSum;   // Σ (self - other) / sqrDist   (separationRadius 내)
    public Vector3 alignmentSum;    // Σ other.direction            (perceptionRadius 내)
    public Vector3 cohesionSum;     // Σ other.position             (perceptionRadius 내)
    public int flockCount;          // perceptionRadius 내 이웃 수
    public int separationCount;     // separationRadius 내 이웃 수
}
