using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
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
    //   Grid       = O(N·k), Spatial Hash Grid로 주변 27셀만 검사 (3단계)
    //   JobsBruteForce = O(N²)를 멀티코어 병렬 처리 (4a, Burst 미적용)
    //   JobsBurst  = 4a + Burst 네이티브 컴파일 (4b)
    //   JobsGrid   = Jobs + Burst + Grid 결합 = O(N·k) 병렬 (4c, 최고 성능)
    public enum PerceptionMode { Legacy, SinglePass, Grid, JobsBruteForce, JobsBurst, JobsGrid }

    [Header("Optimization")]
    public PerceptionMode perceptionMode = PerceptionMode.Grid;
    public bool drawGridGizmos = false;

    private BoidController[] _boids;
    private BoidData[] _boidData;
    private PerceptionResult[] _perception;
    private SpatialGrid _grid;

    // Jobs용 NativeArray / 네이티브 그리드 (Persistent 할당, 매 프레임 재사용)
    private NativeArray<BoidData> _naBoidData;
    private NativeArray<PerceptionResult> _naPerception;
    private NativeParallelMultiHashMap<int, int> _naGrid;

    // ─── 벤치마크 계측 ──────────────────────────────────────
    // 시뮬레이션(인지+조향) 루프만 따로 잰다. 렌더/vsync와 분리해
    // 알고리즘 개선 효과를 순수하게 측정하기 위함.
    private readonly Stopwatch _simWatch = new Stopwatch();
    public float SimMs { get; private set; }      // EWMA 평활된 시뮬 ms
    public int BoidCount => _boids != null ? _boids.Length : 0;
    public BoidController[] Boids => _boids;       // 렌더러 등 외부에서 참조

    private void Start()
    {
        _boids = spawner.Spawn();
        _boidData = new BoidData[_boids.Length];
        _perception = new PerceptionResult[_boids.Length];
        _grid = new SpatialGrid(settings.perceptionRadius);

        _naBoidData   = new NativeArray<BoidData>(_boids.Length, Allocator.Persistent);
        _naPerception = new NativeArray<PerceptionResult>(_boids.Length, Allocator.Persistent);
        _naGrid       = new NativeParallelMultiHashMap<int, int>(_boids.Length, Allocator.Persistent);
    }

    private void OnDestroy()
    {
        if (_naBoidData.IsCreated)   _naBoidData.Dispose();
        if (_naPerception.IsCreated) _naPerception.Dispose();
        if (_naGrid.IsCreated)       _naGrid.Dispose();
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

        if (perceptionMode == PerceptionMode.Legacy)
        {
            // Legacy: 각 Boid가 내부에서 SAC를 각각 순회 (baseline)
            for (int i = 0; i < _boids.Length; i++)
                _boids[i].UpdateBoid(_boidData, settings, ctx);
        }
        else
        {
            // SinglePass / Grid / Jobs: Manager가 1패스로 인지 계산 → Boid는 조향만
            switch (perceptionMode)
            {
                case PerceptionMode.Grid:           ComputePerceptionGrid(settings);          break;
                case PerceptionMode.JobsBruteForce: ComputePerceptionJobs(settings, false);   break;
                case PerceptionMode.JobsBurst:      ComputePerceptionJobs(settings, true);    break;
                case PerceptionMode.JobsGrid:       ComputePerceptionJobsGrid(settings);      break;
                default:                            ComputePerceptionSinglePass(settings);    break;
            }

            for (int i = 0; i < _boids.Length; i++)
                _boids[i].UpdateBoid(in _perception[i], settings, ctx);
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
                Accumulate(ref p, posI, _boidData[j], perceptSqr, sepSqr);
            }

            _perception[i] = p;
        }
    }

    /// <summary>
    /// 3단계 — Spatial Hash Grid 기반 탐색. 각 Boid는 자신의 셀 + 인접 26셀(3×3×3)만
    /// 검사한다. 셀 크기 = perceptionRadius 이므로 반경 내 이웃은 반드시 27셀 안에 있다.
    /// 평균 복잡도 O(N·k) (k = 이웃 밀도).
    /// </summary>
    private void ComputePerceptionGrid(BoidSettings s)
    {
        float perceptSqr = s.perceptionRadius * s.perceptionRadius;
        float sepSqr     = s.separationRadius * s.separationRadius;
        int n = _boidData.Length;

        _grid.Rebuild(_boidData);

        for (int i = 0; i < n; i++)
        {
            PerceptionResult p = default;
            Vector3 posI = _boidData[i].position;

            foreach (int j in _grid.Neighbors(posI))
            {
                if (i == j) continue;
                Accumulate(ref p, posI, _boidData[j], perceptSqr, sepSqr);
            }

            _perception[i] = p;
        }
    }

    /// <summary>
    /// 4a/4b — 인지를 멀티코어로 병렬 처리(IJobParallelFor). 알고리즘은 SinglePass와 동일한
    /// O(N²)이지만 코어 수만큼 분산된다. burst=true면 [BurstCompile] Job으로 네이티브 가속.
    /// </summary>
    private void ComputePerceptionJobs(BoidSettings s, bool burst)
    {
        _naBoidData.CopyFrom(_boidData);

        float perceptSqr = s.perceptionRadius * s.perceptionRadius;
        float sepSqr     = s.separationRadius * s.separationRadius;

        // batch=32: 워커가 한 번에 가져가는 인덱스 묶음 크기
        if (burst)
        {
            var job = new PerceptionJobBurst
            {
                boids = _naBoidData, results = _naPerception,
                perceptSqr = perceptSqr, sepSqr = sepSqr
            };
            job.Schedule(_boidData.Length, 32).Complete();
        }
        else
        {
            var job = new PerceptionJobBruteForce
            {
                boids = _naBoidData, results = _naPerception,
                perceptSqr = perceptSqr, sepSqr = sepSqr
            };
            job.Schedule(_boidData.Length, 32).Complete();
        }

        _naPerception.CopyTo(_perception);
    }

    /// <summary>
    /// 4c — Jobs + Burst + Grid. 네이티브 그리드를 채운 뒤 병렬 Job이 주변 27셀만 검사.
    /// O(N·k) + 멀티코어 + Burst (종합 최고 성능).
    /// </summary>
    private void ComputePerceptionJobsGrid(BoidSettings s)
    {
        _naBoidData.CopyFrom(_boidData);

        // 네이티브 그리드 재구성 (메인 스레드, O(N))
        float inv = 1f / s.perceptionRadius;
        _naGrid.Clear();
        for (int k = 0; k < _boidData.Length; k++)
        {
            Vector3 pos = _boidData[k].position;
            int cx = Mathf.FloorToInt(pos.x * inv);
            int cy = Mathf.FloorToInt(pos.y * inv);
            int cz = Mathf.FloorToInt(pos.z * inv);
            _naGrid.Add(PerceptionGridJob.Hash(cx, cy, cz), k);
        }

        var job = new PerceptionGridJob
        {
            boids       = _naBoidData,
            grid        = _naGrid,
            results     = _naPerception,
            perceptSqr  = s.perceptionRadius * s.perceptionRadius,
            sepSqr      = s.separationRadius * s.separationRadius,
            invCellSize = inv
        };
        job.Schedule(_boidData.Length, 32).Complete();

        _naPerception.CopyTo(_perception);
    }

    /// <summary>이웃 boid 하나를 인지 누산값에 반영. SinglePass·Grid가 공유.</summary>
    private static void Accumulate(ref PerceptionResult p, Vector3 posI, BoidData other,
                                   float perceptSqr, float sepSqr)
    {
        Vector3 offset = posI - other.position;
        float sqr = offset.sqrMagnitude;
        if (sqr <= 0f || sqr >= perceptSqr) return;

        p.alignmentSum += other.direction;   // Alignment
        p.cohesionSum  += other.position;     // Cohesion
        p.flockCount++;

        if (sqr < sepSqr)
        {
            // offset.normalized / dist == offset / sqr → sqrt 불필요
            p.separationSum += offset / sqr;  // Separation
            p.separationCount++;
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

    private readonly System.Collections.Generic.List<Vector3> _gizmoCells
        = new System.Collections.Generic.List<Vector3>();

    private void OnDrawGizmos()
    {
        // drawGridGizmos는 3단계 Grid(CPU Spatial Hash)에서만 지원.
        // JobsGrid는 NativeParallelMultiHashMap이라 매 프레임 읽기 비용이 크므로 미지원.
        if (!drawGridGizmos || !Application.isPlaying || _grid == null) return;

        _grid.GetOccupiedCellCenters(_gizmoCells);
        Vector3 size = Vector3.one * _grid.CellSize;
        Gizmos.color = new Color(0f, 1f, 0.6f, 0.25f);
        foreach (var c in _gizmoCells)
            Gizmos.DrawWireCube(c, size);
    }
}
