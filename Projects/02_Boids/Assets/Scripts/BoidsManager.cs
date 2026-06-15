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

    private BoidController[] _boids;
    private BoidData[] _boidData;

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
        for (int i = 0; i < _boids.Length; i++)
        {
            //ApplyBoundary(_boids[i]);
            _boids[i].UpdateBoid(_boidData, settings, ctx);
        }
        _simWatch.Stop();

        // ms 단위로 변환 후 지수이동평균(α=0.1)으로 튐 완화
        float ms = (float)(_simWatch.Elapsed.TotalMilliseconds);
        SimMs = Mathf.Lerp(SimMs, ms, 0.1f);
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
