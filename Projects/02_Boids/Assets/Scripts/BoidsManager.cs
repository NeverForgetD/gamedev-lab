using System.Diagnostics;
using UnityEngine;

public class BoidsManager : MonoBehaviour
{
    [Header("References")]
    public BoidSpawner spawner;

    [Header("Settings")]
    public BoidSettings settings;

    [Header("Target")]
    public Transform target;

    [Header("Predator")]
    public Transform predator;

    [Header("Boundary")]
    public float boundaryRadius = 20f;

    // 인지(이웃 탐색) 방식. 같은 씬에서 토글하며 성능을 비교 측정한다.
    //   Legacy     = O(3N²), Vector3.Distance (최적화 이전, baseline)
    //   SinglePass = O(N²),  단일 패스 + sqrMagnitude (1·2단계)
    public enum PerceptionMode { Legacy, SinglePass }

    [Header("Optimization")]
    public PerceptionMode perceptionMode = PerceptionMode.SinglePass;

    private BoidController[] _boids;
    private BoidData[] _boidData;
    private PerceptionResult[] _perception;

    // ─── 벤치마크 계측 ──────────────────────────────────────
    // 시뮬레이션(인지+조향) 루프만 따로 잰다. 렌더/vsync와 분리해
    // 알고리즘 개선 효과를 순수하게 측정하기 위함.
    private readonly Stopwatch _simWatch = new Stopwatch();
    public float SimMs { get; private set; }      // EWMA 평활된 시뮬 ms
    public int BoidCount => _boids != null ? _boids.Length : 0;

    private void Start()
    {
        _boids = spawner.Spawn();
        _boidData = new BoidData[_boids.Length];
        _perception = new PerceptionResult[_boids.Length];
    }

    private void Update()
    {
        for (int i = 0; i < _boids.Length; i++)
            _boidData[i] = _boids[i].data;

        BoidContext ctx = new BoidContext
        {
            hasTarget       = target != null,
            targetPosition  = target   != null ? target.position   : Vector3.zero,
            hasPredator     = predator != null,
            predatorPosition = predator != null ? predator.position : Vector3.zero
        };

        _simWatch.Restart();

        if (perceptionMode == PerceptionMode.SinglePass)
        {
            // 1·2단계: 이웃을 1패스 순회해 누산값 계산 → 조향만 적용
            ComputePerceptionSinglePass(settings);
            for (int i = 0; i < _boids.Length; i++)
                _boids[i].UpdateBoid(in _perception[i], settings, ctx);
        }
        else
        {
            // Legacy: 각 Boid가 내부에서 SAC를 각각 순회 (baseline)
            for (int i = 0; i < _boids.Length; i++)
                _boids[i].UpdateBoid(_boidData, settings, ctx);
        }

        _simWatch.Stop();

        // ms 단위로 변환 후 지수이동평균(α=0.1)으로 튐 완화
        float ms = (float)(_simWatch.Elapsed.TotalMilliseconds);
        SimMs = Mathf.Lerp(SimMs, ms, 0.1f);
    }

    /// <summary>
    /// 1·2단계 — 모든 Boid 쌍을 검사하되 SAC를 1패스로 누산하고(O(3N²)→O(N²)),
    /// sqrMagnitude로 비교해 sqrt를 제거한다.
    /// </summary>
    private void ComputePerceptionSinglePass(BoidSettings s)
    {
        float perceptSqr = s.perceptionRadius * s.perceptionRadius;
        float sepSqr     = s.separationRadius * s.separationRadius;
        int n = _boidData.Length;

        for (int i = 0; i < n; i++)
        {
            PerceptionResult p = default;
            Vector3 posI = _boidData[i].position;

            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;

                Vector3 offset = posI - _boidData[j].position;
                float sqr = offset.sqrMagnitude;
                if (sqr <= 0f || sqr >= perceptSqr) continue;

                p.alignmentSum += _boidData[j].direction;   // Alignment
                p.cohesionSum  += _boidData[j].position;     // Cohesion
                p.flockCount++;

                if (sqr < sepSqr)
                {
                    // offset.normalized / dist == offset / sqr → sqrt 불필요
                    p.separationSum += offset / sqr;          // Separation
                    p.separationCount++;
                }
            }

            _perception[i] = p;
        }
    }

    private void ApplyBoundary(BoidController boid)
    {
        Vector3 offset = boid.transform.position - transform.position;
        if (offset.magnitude > boundaryRadius)
        {
            Vector3 outward = offset.normalized;
            float outwardSpeed = Vector3.Dot(boid.velocity, outward);
            if (outwardSpeed > 0f)
                boid.velocity -= 2f * outwardSpeed * outward;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, boundaryRadius);
    }
}
