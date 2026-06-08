using System;
using UnityEngine;

[Serializable]
public class BoidSettings
{
    [Header("Speed")]
    public float minSpeed = 2f;
    public float maxSpeed = 5f;

    [Header("Perception")]
    public float perceptionRadius = 2.5f;
    public float separationRadius = 1f;

    [Header("Weights")]
    public float separationWeight = 1f;
    public float alignmentWeight = 1f;
    public float cohesionWeight = 1f;

    [Header("Steering")]
    public float maxSteerForce = 3f;

    // ─── 충돌 회피 튜닝 공식 ────────────────────────────────────────────
    // 핵심 제약: maxSteerForce × collisionAvoidanceWeight
    //            ≥ 3 × maxSpeed² / collisionAvoidanceDistance
    //
    // collisionAvoidanceDistance 권장: 1 ~ 2 × maxSpeed
    // collisionAvoidanceWeight   공식: 3 × maxSpeed² / (maxSteerForce × collisionAvoidanceDistance)
    //                                  (최솟값 기준, 2배 여유 권장)
    //
    // [예시 A] maxSpeed = 5  (기본)
    //   collisionAvoidanceDistance = 5   (= 1 × maxSpeed)
    //   collisionAvoidanceWeight   = 10  (최솟값 5의 2배 여유)
    //   검증: 3 × 10 = 30 ≥ 3 × 25 / 5 = 15 ✓
    //
    // [예시 B] maxSpeed = 20  (고속)
    //   collisionAvoidanceDistance = 20  (= 1 × maxSpeed)
    //   collisionAvoidanceWeight   = 40  (최솟값 20의 2배 여유)
    //   검증: 3 × 40 = 120 ≥ 3 × 400 / 20 = 60 ✓
    // ───────────────────────────────────────────────────────────────────

    [Header("Collisions")]
    public LayerMask collisionMask;
    public float collisionRadius = 0.27f;
    public float collisionAvoidanceWeight = 10f;
    public float collisionAvoidanceDistance = 5f;
}
