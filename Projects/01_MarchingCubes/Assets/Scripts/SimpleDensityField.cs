using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public struct FieldData
{
    public float3 position;
    public float  density;
}

[System.Serializable]
public struct NoiseSettings
{
    public bool  applyNoise;
    public float frequency;
    public float amplitude;
}

public class SimpleDensityField : MonoBehaviour
{
    private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
    private const int STRIDE = 16; // sizeof(FieldData) : float3(12) + float(4)

    public enum FieldType { Sphere, Terrain2D }

    [SerializeField] private ChunkProfile profile;

    [Header("Gizmos Settings")]
    [SerializeField] private Mesh     gizmoMesh;
    [SerializeField] private Material gizmoMaterial;
    [SerializeField] private bool     showGizmos = true;

    private Bounds        bounds;
    private ComputeBuffer gizmoBuffer;
    private ComputeBuffer argsBuffer;

    // ── NativeArray (Persistent) ──────────────────────────────────────
    private NativeArray<FieldData> densityField;
    private NativeArray<float>     deltaField;

    // ── 진행 중인 Job 핸들 ────────────────────────────────────────────
    private JobHandle pendingFieldHandle;
    private bool      isFieldJobRunning;

    // densityField 를 [ReadOnly] 로 읽는 외부 Job (MarchingCubesJob 등) 핸들.
    // 다음 쓰기 Job 예약 시 dependency 로 포함해 Data Race 를 방지한다.
    private JobHandle readerHandle;

    public bool IsDirty           { get; private set; }
    public bool IsFieldJobRunning => isFieldJobRunning;

    // Generator 가 Job 완료 후 읽어가는 NativeArray (ReadOnly)
    public NativeArray<FieldData> DensityFieldNative => densityField;

    public ChunkProfile Profile  { get => profile; set => profile = value; }
    public int          Resolution => profile.resolution;
    public float        UnitSize   => profile.UnitSize;
    public float        WorldSize  => profile.WorldSize;

    // ─────────────────────────────────────────────────────────────────
    void Start()
    {
        AllocateNativeArrays();
        InitGizmo();
        ScheduleRefreshField();
    }

    private void Update()
    {
        // Job 완료 체크
        if (isFieldJobRunning && pendingFieldHandle.IsCompleted)
        {
            pendingFieldHandle.Complete();
            isFieldJobRunning = false;

            if (showGizmos && gizmoBuffer != null)
                UploadToGizmoBuffer();

            IsDirty = true;
        }

        if (showGizmos && gizmoBuffer != null && gizmoMaterial != null)
            Graphics.DrawMeshInstancedIndirect(
                gizmoMesh, 0, gizmoMaterial, bounds, argsBuffer);
    }

    // ─────────────────────────────────────────────────────────────────
    // 공개 API
    // ─────────────────────────────────────────────────────────────────

    /// <summary>NativeArray 재할당 + 필드 리프레시 예약 (ChunkManager 호출)</summary>
    public void InitField()
    {
        CompleteRunningJob();          // 진행 중이면 즉시 완료
        AllocateNativeArrays();
        ScheduleRefreshField();
    }

    public void ClearDirty() => IsDirty = false;
    public void MarkDirty()  => IsDirty = true;

    public void ResetField()
    {
        CompleteRunningJob();
        if (deltaField.IsCreated)
            for (int i = 0; i < deltaField.Length; i++) deltaField[i] = 0f;
        IsDirty = false;
    }

    /// <summary>지형 편집 : ModifyDensityJob → RefreshField Job 체인</summary>
    public void ModifyDensity(float3 center, float radius, float delta)
    {
        CompleteRunningJob();

        var modJob = new ModifyDensityJob
        {
            DensityField = densityField,
            DeltaField   = deltaField,
            Center       = center,
            Radius       = radius,
            Delta        = delta
        };

        // ModifyDensity → RefreshField 순서로 체인
        var modHandle = modJob.Schedule(densityField.Length, 64);
        ScheduleRefreshField(modHandle);
    }

    // ─────────────────────────────────────────────────────────────────
    // 내부 : Job 스케줄링
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// densityField 를 [ReadOnly] 로 읽는 외부 Job 핸들을 등록.
    /// 다음 ScheduleRefreshField 호출 시 dependency 로 자동 포함된다.
    /// </summary>
    public void RegisterReaderHandle(JobHandle handle)
    {
        readerHandle = JobHandle.CombineDependencies(readerHandle, handle);
    }

    private void ScheduleRefreshField(JobHandle dependency = default)
    {
        CompleteRunningJob();

        // 읽기 Job(MarchingCubesJob 등)이 끝난 뒤 쓰기 Job 을 시작하도록 연결
        dependency   = JobHandle.CombineDependencies(dependency, readerHandle);
        readerHandle = default; // 소비 후 초기화

        switch (profile.fieldType)
        {
            case FieldType.Sphere:    ScheduleSphere(dependency);    break;
            case FieldType.Terrain2D: ScheduleTerrain2D(dependency); break;
        }
    }

    private void ScheduleSphere(JobHandle dependency)
    {
        var center = GetFieldCenter();
        var job    = new CreateSphereJob
        {
            DeltaField      = deltaField,
            DensityField    = densityField,
            PointsPerAxis   = profile.resolution + 1,
            CenterPos       = (float3)transform.position,
            FieldCenter     = center,
            UnitSize        = profile.UnitSize,
            SphereRadius    = profile.sphereRadius,
            ApplyNoise      = profile.sphereNoise.applyNoise,
            NoiseFrequency  = profile.sphereNoise.frequency,
            NoiseAmplitude  = profile.sphereNoise.amplitude
        };

        pendingFieldHandle = job.Schedule(densityField.Length, 64, dependency);
        isFieldJobRunning  = true;
    }

    private void ScheduleTerrain2D(JobHandle dependency)
    {
        var center = GetFieldCenter();
        var job    = new CreateTerrain2DJob
        {
            DeltaField      = deltaField,
            DensityField    = densityField,
            PointsPerAxis   = profile.resolution + 1,
            OriginPos       = (float3)transform.position,
            FieldCenter     = center,
            UnitSize        = profile.UnitSize,
            BaseHeight      = profile.terrain_baseHeight,
            ApplyNoise      = profile.terrainNoise.applyNoise,
            NoiseFrequency  = profile.terrainNoise.frequency,
            NoiseAmplitude  = profile.terrainNoise.amplitude
        };

        pendingFieldHandle = job.Schedule(densityField.Length, 64, dependency);
        isFieldJobRunning  = true;
    }

    // ─────────────────────────────────────────────────────────────────
    // 내부 유틸
    // ─────────────────────────────────────────────────────────────────

    private float3 GetFieldCenter() => profile.fieldType switch
    {
        FieldType.Sphere    => new float3(1, 1, 1) * profile.resolution / 2f,
        FieldType.Terrain2D => new float3(profile.resolution / 2f, 0f, profile.resolution / 2f),
        _                   => new float3(1, 1, 1) * profile.resolution / 2f
    };

    private void AllocateNativeArrays()
    {
        if (profile == null) { Debug.LogError("ChunkProfile not assigned", this); return; }

        int pts   = profile.resolution + 1;
        int count = pts * pts * pts;

        // 이미 있으면 Dispose 후 재할당
        if (densityField.IsCreated) densityField.Dispose();
        if (deltaField.IsCreated)   deltaField.Dispose();

        densityField = new NativeArray<FieldData>(count, Allocator.Persistent);
        deltaField   = new NativeArray<float>(count, Allocator.Persistent);
    }

    private void CompleteRunningJob()
    {
        if (!isFieldJobRunning) return;
        pendingFieldHandle.Complete();
        isFieldJobRunning = false;
    }

    private void UploadToGizmoBuffer()
    {
        // NativeArray → ComputeBuffer
        // Unity 2022+: SetData(NativeArray) 직접 지원
        gizmoBuffer.SetData(densityField);
    }

    // ─────────────────────────────────────────────────────────────────
    // Gizmo
    // ─────────────────────────────────────────────────────────────────
    private void InitGizmo()
    {
        if (gizmoMaterial == null || gizmoMesh == null) return;

        int pts   = profile.resolution + 1;
        int count = pts * pts * pts;
        float size = profile.WorldSize;
        bounds = new Bounds(transform.position, new Vector3(size, size, size));

        gizmoBuffer = new ComputeBuffer(count, STRIDE);

        gizmoMaterial = new Material(gizmoMaterial);
        gizmoMaterial.enableInstancing = true;
        gizmoMaterial.SetBuffer(GizmoBufferProperty, gizmoBuffer);

        uint[] args =
        {
            gizmoMesh.GetIndexCount(0),
            (uint)count,
            gizmoMesh.GetIndexStart(0),
            gizmoMesh.GetBaseVertex(0),
            0
        };
        argsBuffer = new ComputeBuffer(
            1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
    }

    // ─────────────────────────────────────────────────────────────────
    // 생명주기
    // ─────────────────────────────────────────────────────────────────
    private void OnDestroy()
    {
        CompleteRunningJob();
        if (densityField.IsCreated) densityField.Dispose();
        if (deltaField.IsCreated)   deltaField.Dispose();

        gizmoBuffer?.Dispose();
        argsBuffer?.Dispose();
        if (gizmoMaterial != null) Destroy(gizmoMaterial);
    }
}
