// SimpleMarchingCubeGenerator.cs
// 기존 main-thread 3중 루프를 제거하고 MarchingCubesJob(IJobParallelFor) 으로 교체.
//
// 흐름
//   Update() → densityField Job 완료 감지 → ScheduleGeneration()
//   LateUpdate() → MC Job 완료 감지 → BuildMesh() (main thread 메시 업로드)

using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class SimpleMarchingCubeGenerator : MonoBehaviour
{
    public enum InterpolateMode { Linear, Half, Smoothstep, Snapping }

    [SerializeField] private SimpleDensityField densityField;
    [SerializeField] private InterpolateMode    interpolateMode = InterpolateMode.Linear;
    [SerializeField, Range(-5f, 5f)] private float isoLevel    = 0f;
    [SerializeField] private int                lodStep        = 1;

    public int LodStep
    {
        get => lodStep;
        set => lodStep = Mathf.Max(1, value);
    }

    private MeshFilter   meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh         mesh;

    // ── Job 상태 ──────────────────────────────────────────────────────
    private JobHandle    mcJobHandle;
    private NativeStream triangleStream;   // TempJob, Execute 후 Dispose
    private int          totalCellCount;
    private bool         isMcJobRunning;

    // ─────────────────────────────────────────────────────────────────
    private void Start()
    {
        meshFilter   = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = gameObject.AddComponent<MeshCollider>();
        mesh = new Mesh { name = "MarchingCubesMesh" };
    }

    private void Update()
    {
        // densityField Job 이 완료되면 MC Job 예약
        if (densityField.IsDirty && !densityField.IsFieldJobRunning && !isMcJobRunning)
        {
            densityField.ClearDirty();
            ScheduleGeneration();
        }
    }

    private void LateUpdate()
    {
        // MC Job 완료 후 메시 업로드 (main thread)
        if (isMcJobRunning && mcJobHandle.IsCompleted)
        {
            mcJobHandle.Complete();
            isMcJobRunning = false;
            BuildMesh();
        }
    }

    private void OnDisable()
    {
        CompleteMcJob();
        if (meshCollider != null) meshCollider.sharedMesh = null;
        mesh?.Clear();
    }

    private void OnDestroy()
    {
        CompleteMcJob();
    }

    // ─────────────────────────────────────────────────────────────────
    // Job 예약
    // ─────────────────────────────────────────────────────────────────
    private void ScheduleGeneration()
    {
        var resolution    = densityField.Resolution;
        var step          = Mathf.Max(1, lodStep);
        int cellsPerAxis  = (resolution + step - 1) / step;
        totalCellCount    = cellsPerAxis * cellsPerAxis * cellsPerAxis;

        // NativeStream : 셀마다 독립 버킷 → 가변 Triangle 출력
        triangleStream = new NativeStream(totalCellCount, Allocator.TempJob);

        var job = new MarchingCubesJob
        {
            DensityField    = densityField.DensityFieldNative,
            EdgeTable       = NativeLookupTable.EdgeTable,
            TriangleTable   = NativeLookupTable.TriangleTable,
            Resolution      = resolution,
            PointsPerAxis   = resolution + 1,
            IsoLevel        = isoLevel,
            LodStep         = step,
            InterpolateMode = (int)interpolateMode,
            Writer          = triangleStream.AsWriter()
        };

        // batchSize : 셀 수에 따라 동적 조정
        int batch  = Mathf.Max(8, totalCellCount / (SystemInfo.processorCount * 4));
        mcJobHandle    = job.Schedule(totalCellCount, batch);
        isMcJobRunning = true;
    }

    // ─────────────────────────────────────────────────────────────────
    // 메시 업로드 (main thread, Job 완료 후)
    // ─────────────────────────────────────────────────────────────────
    private void BuildMesh()
    {
        // ① NativeStream → Triangle 수집
        var reader = triangleStream.AsReader();

        // 최대 트라이앵글 수 추정 : 셀당 최대 5개
        int estimatedMax = totalCellCount * 5;
        var vertices        = new NativeList<Vector3>(estimatedMax * 3, Allocator.Temp);
        var triangleIndices = new NativeList<int>(estimatedMax * 3, Allocator.Temp);

        var origin = (Unity.Mathematics.float3)transform.position;

        for (int i = 0; i < totalCellCount; i++)
        {
            int count = reader.BeginForEachIndex(i);
            for (int t = 0; t < count; t++)
            {
                var tri = reader.Read<Triangle>();
                int idx = vertices.Length;

                vertices.Add((Vector3)(tri.a - origin));
                vertices.Add((Vector3)(tri.b - origin));
                vertices.Add((Vector3)(tri.c - origin));

                triangleIndices.Add(idx);
                triangleIndices.Add(idx + 1);
                triangleIndices.Add(idx + 2);
            }
            reader.EndForEachIndex();
        }

        triangleStream.Dispose();

        // ② Mesh 적용
        mesh.Clear();
        mesh.SetVertices(vertices.AsArray());
        mesh.SetTriangles(triangleIndices.AsArray().ToArray(), 0); // int[] 필요
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;

        // ③ Collider 갱신
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;

        vertices.Dispose();
        triangleIndices.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────
    // 안전 종료
    // ─────────────────────────────────────────────────────────────────
    private void CompleteMcJob()
    {
        if (!isMcJobRunning) return;
        mcJobHandle.Complete();
        isMcJobRunning = false;
        if (triangleStream.IsCreated) triangleStream.Dispose();
    }
}
