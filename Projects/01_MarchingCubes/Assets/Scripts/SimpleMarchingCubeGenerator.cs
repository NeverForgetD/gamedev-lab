using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// ── Collider 비동기 베이킹 Job ────────────────────────────────────────────
// Physics.BakeMesh 는 워커 스레드에서 호출 가능 (Unity 2022+)
// Burst 비호환(managed API) → [BurstCompile] 없음
public struct BakeMeshJob : IJob
{
    public int MeshInstanceId;
    public void Execute() => Physics.BakeMesh(MeshInstanceId, false);
}

// ─────────────────────────────────────────────────────────────────────────
public class SimpleMarchingCubeGenerator : MonoBehaviour
{
    public enum InterpolateMode { Linear, Half, Smoothstep, Snapping }

    [SerializeField] private SimpleDensityField densityField;
    [SerializeField] private InterpolateMode    interpolateMode = InterpolateMode.Linear;
    [SerializeField, Range(-5f, 5f)] private float isoLevel    = 0f;
    [SerializeField] private int                lodStep        = 1;

    [Header("Shading")]
    [Tooltip("ON = 중복 버텍스 제거 + 스무스 셰이딩 / OFF = 플랫 셰이딩")]
    [SerializeField] private bool smoothShading = true;

    [Header("Collider")]
    [Tooltip("OFF = 충돌 메시 생성 안 함 (원거리 청크 등에 활용)")]
    [SerializeField] private bool enableCollider = true;
    [Tooltip("이 LodStep 초과 시 콜라이더 베이킹 스킵 (0 = 항상 베이킹)")]
    [SerializeField] private int  colliderMaxLodStep = 2;

    // ── 프로퍼티 ──────────────────────────────────────────────────────
    public int LodStep
    {
        get => lodStep;
        set => lodStep = Mathf.Max(1, value);
    }

    // ── 컴포넌트 ──────────────────────────────────────────────────────
    private MeshFilter   meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh         mesh;

    // ── MC Job 상태 ───────────────────────────────────────────────────
    private JobHandle    mcJobHandle;
    private NativeStream triangleStream;
    private int          totalCellCount;
    private bool         isMcJobRunning;

    // ── Bake Job 상태 ─────────────────────────────────────────────────
    private JobHandle bakeJobHandle;
    private bool      isBakeRunning;

    // ── 양자화 스케일 ─────────────────────────────────────────────────
    private const float QUANT_SCALE = 10000f;

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
        if (densityField.IsDirty && !densityField.IsFieldJobRunning && !isMcJobRunning)
        {
            densityField.ClearDirty();
            ScheduleGeneration();
        }
    }

    private void LateUpdate()
    {
        // ① MC Job 완료 → 메시 업로드 + Bake Job 예약
        if (isMcJobRunning && mcJobHandle.IsCompleted)
        {
            mcJobHandle.Complete();
            isMcJobRunning = false;
            BuildMesh();
        }

        // ② Bake Job 완료 → 콜라이더에 반영
        if (isBakeRunning && bakeJobHandle.IsCompleted)
        {
            bakeJobHandle.Complete();
            isBakeRunning = false;
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }

    private void OnDisable()
    {
        CompleteMcJob();
        CompleteBakeJob();
        if (meshCollider != null) meshCollider.sharedMesh = null;
        mesh?.Clear();
    }

    private void OnDestroy()
    {
        CompleteMcJob();
        CompleteBakeJob();
    }

    // ─────────────────────────────────────────────────────────────────
    // 외부 API
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// LOD 변경 등 외부에서 메시 재생성을 요청할 때 호출.
    /// 밀도 재계산 없이 MC Job 만 다시 예약한다.
    /// </summary>
    public void TriggerRebuild()
    {
        if (isMcJobRunning) return; // 진행 중이면 완료 후 자동 처리
        densityField.MarkDirty();
    }

    // ─────────────────────────────────────────────────────────────────
    // MC Job 예약
    // ─────────────────────────────────────────────────────────────────
    private void ScheduleGeneration()
    {
        var resolution   = densityField.Resolution;
        var step         = Mathf.Max(1, lodStep);
        int cellsPerAxis = (resolution + step - 1) / step;
        totalCellCount   = cellsPerAxis * cellsPerAxis * cellsPerAxis;

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

        int batch      = Mathf.Max(8, totalCellCount / (SystemInfo.processorCount * 4));
        mcJobHandle    = job.Schedule(totalCellCount, batch);
        isMcJobRunning = true;

        // densityField 의 다음 쓰기 Job 이 이 읽기 Job 완료를 기다리도록 등록
        densityField.RegisterReaderHandle(mcJobHandle);
    }

    // ─────────────────────────────────────────────────────────────────
    // 메시 빌드 (MC Job 완료 후 main thread)
    // ─────────────────────────────────────────────────────────────────
    private void BuildMesh()
    {
        var reader       = triangleStream.AsReader();
        int estimatedMax = totalCellCount * 5;

        var indices = new NativeList<int>(estimatedMax * 3, Allocator.Temp);

        if (smoothShading)
            BuildSmooth(reader, estimatedMax, ref indices);
        else
            BuildFlat(reader, estimatedMax, ref indices);

        triangleStream.Dispose();
        indices.Dispose();

        // 콜라이더는 별도 Job 으로 비동기 처리
        ScheduleColliderBake();
    }

    // ── 스무스 셰이딩 ─────────────────────────────────────────────────
    private void BuildSmooth(NativeStream.Reader reader,
                             int estimatedMax, ref NativeList<int> indices)
    {
        var vertexMap = new NativeHashMap<int3, int>(estimatedMax * 3, Allocator.Temp);
        var vertices  = new NativeList<Vector3>(estimatedMax * 3, Allocator.Temp);
        var origin    = (float3)transform.position;

        for (int i = 0; i < totalCellCount; i++)
        {
            int count = reader.BeginForEachIndex(i);
            for (int t = 0; t < count; t++)
            {
                var tri = reader.Read<Triangle>();
                indices.Add(GetOrAddVertex(tri.a - origin, ref vertexMap, ref vertices));
                indices.Add(GetOrAddVertex(tri.b - origin, ref vertexMap, ref vertices));
                indices.Add(GetOrAddVertex(tri.c - origin, ref vertexMap, ref vertices));
            }
            reader.EndForEachIndex();
        }

        ApplyMesh(vertices.AsArray(), indices);
        vertexMap.Dispose();
        vertices.Dispose();
    }

    // ── 플랫 셰이딩 ───────────────────────────────────────────────────
    private void BuildFlat(NativeStream.Reader reader,
                           int estimatedMax, ref NativeList<int> indices)
    {
        var vertices = new NativeList<Vector3>(estimatedMax * 3, Allocator.Temp);
        var origin   = (float3)transform.position;

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
                indices.Add(idx);
                indices.Add(idx + 1);
                indices.Add(idx + 2);
            }
            reader.EndForEachIndex();
        }

        ApplyMesh(vertices.AsArray(), indices);
        vertices.Dispose();
    }

    // ── Mesh 최종 적용 ────────────────────────────────────────────────
    private void ApplyMesh(NativeArray<Vector3> vertices, NativeList<int> indices)
    {
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        meshFilter.mesh = mesh;
    }

    // ─────────────────────────────────────────────────────────────────
    // 콜라이더 비동기 베이킹
    // ─────────────────────────────────────────────────────────────────
    private void ScheduleColliderBake()
    {
        // 조건 : 콜라이더 비활성화 또는 LodStep 초과 시 스킵
        if (!enableCollider || (colliderMaxLodStep > 0 && lodStep > colliderMaxLodStep))
        {
            meshCollider.sharedMesh = null;
            return;
        }

        // 이전 Bake 가 아직 실행 중이면 완료 후 재시작
        CompleteBakeJob();

        // mesh 업로드(SetVertices/SetIndices)가 완료된 시점에서 호출되므로
        // GetInstanceID() 는 이미 유효한 메시 ID 를 반환한다.
        bakeJobHandle = new BakeMeshJob { MeshInstanceId = mesh.GetInstanceID() }.Schedule();
        isBakeRunning = true;
    }

    // ─────────────────────────────────────────────────────────────────
    // 버텍스 중복 제거 헬퍼
    // ─────────────────────────────────────────────────────────────────
    private static int GetOrAddVertex(float3 pos,
        ref NativeHashMap<int3, int> map, ref NativeList<Vector3> vertices)
    {
        var key = new int3(
            (int)math.round(pos.x * QUANT_SCALE),
            (int)math.round(pos.y * QUANT_SCALE),
            (int)math.round(pos.z * QUANT_SCALE));

        if (map.TryGetValue(key, out int index))
            return index;

        index = vertices.Length;
        vertices.Add((Vector3)pos);
        map.Add(key, index);
        return index;
    }

    // ─────────────────────────────────────────────────────────────────
    // 안전 종료 헬퍼
    // ─────────────────────────────────────────────────────────────────
    private void CompleteMcJob()
    {
        if (!isMcJobRunning) return;
        mcJobHandle.Complete();
        isMcJobRunning = false;
        if (triangleStream.IsCreated) triangleStream.Dispose();
    }

    private void CompleteBakeJob()
    {
        if (!isBakeRunning) return;
        bakeJobHandle.Complete();
        isBakeRunning = false;
    }
}
