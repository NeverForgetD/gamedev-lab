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

    [Header("Shading")]
    [Tooltip("ON = 중복 버텍스 제거 + 스무스 셰이딩 / OFF = 플랫 셰이딩")]
    [SerializeField] private bool smoothShading = true;

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
    private NativeStream triangleStream;
    private int          totalCellCount;
    private bool         isMcJobRunning;

    // ── 양자화 스케일 ─────────────────────────────────────────────────
    // 버텍스 위치를 정수 키로 변환할 때 쓰는 배율.
    // 1/UnitSize * 1000 정도면 부동소수점 오차(~1e-6)를 흡수하면서
    // 실제 다른 두 점은 구별할 수 있다.
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
    }

    // ─────────────────────────────────────────────────────────────────
    // 메시 업로드
    // ─────────────────────────────────────────────────────────────────
    private void BuildMesh()
    {
        var reader       = triangleStream.AsReader();
        int estimatedMax = totalCellCount * 5; // 셀당 최대 5 삼각형

        var indices = new NativeList<int>(estimatedMax * 3, Allocator.Temp);

        if (smoothShading)
            BuildSmooth(reader, estimatedMax, ref indices);
        else
            BuildFlat(reader, estimatedMax, ref indices);

        triangleStream.Dispose();
        indices.Dispose();

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }

    // ── 스무스 셰이딩 : 버텍스 공유 ──────────────────────────────────
    private void BuildSmooth(NativeStream.Reader reader,
                             int estimatedMax, ref NativeList<int> indices)
    {
        // 양자화 키 → 버텍스 인덱스
        var vertexMap = new NativeHashMap<int3, int>(estimatedMax * 3, Allocator.Temp);
        var vertices  = new NativeList<Vector3>(estimatedMax * 3, Allocator.Temp);

        var origin = (float3)transform.position;

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

    // ── 플랫 셰이딩 : 삼각형마다 독립 버텍스 ────────────────────────
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
        // SetIndices : NativeArray<int> 직접 수용 → ToArray() 불필요
        mesh.SetIndices(indices.AsArray(), MeshTopology.Triangles, 0);
        mesh.RecalculateNormals();  // 공유 버텍스면 평균 노멀 → 스무스
        mesh.RecalculateBounds();
        meshFilter.mesh = mesh;
    }

    // ─────────────────────────────────────────────────────────────────
    // 버텍스 중복 제거 헬퍼
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 위치를 QUANT_SCALE 로 양자화한 int3 키로 캐시를 조회.
    /// 없으면 신규 버텍스로 추가하고 인덱스를 반환한다.
    /// </summary>
    private static int GetOrAddVertex(float3 pos,
        ref NativeHashMap<int3, int> map, ref NativeList<Vector3> vertices)
    {
        // 부동소수점 오차를 흡수하는 양자화 키
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
    private void CompleteMcJob()
    {
        if (!isMcJobRunning) return;
        mcJobHandle.Complete();
        isMcJobRunning = false;
        if (triangleStream.IsCreated) triangleStream.Dispose();
    }
}
