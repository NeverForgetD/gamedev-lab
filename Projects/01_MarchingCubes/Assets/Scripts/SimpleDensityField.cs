using System;
using Unity.Mathematics;
using UnityEngine;

public struct FieldData
{
    public float3 position;
    public float density;
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
    private const int STRIDE = 16;

    // fBm 고정 수치
    private const int   NOISE_OCTAVES    = 4;
    private const float NOISE_LACUNARITY = 2f;
    private const float NOISE_GAIN       = 0.5f;

    public enum FieldType { Sphere, Terrain2D }

    [Header("Field Properties")]
    [SerializeField] private FieldType fieldType  = FieldType.Sphere;
    [SerializeField] private int       resolution = 16;
    [SerializeField] private float     unitSize   = 1f;

    [Header("Sphere Properties")]
    [SerializeField] private float        sphereRadius = 5f;
    [SerializeField] private NoiseSettings sphereNoise = new NoiseSettings
    {
        applyNoise = false,
        frequency  = 0.05f,
        amplitude  = 2f,
    };

    [Header("Terrain2D Properties")]
    [SerializeField] private float         terrain_baseHeight = 5f;
    [SerializeField] private NoiseSettings terrainNoise       = new NoiseSettings
    {
        applyNoise = true,
        frequency  = 0.05f,
        amplitude  = 5f,
    };

    [Header("Gizmos Settings")]
    [SerializeField] private Mesh     gizmoMesh;
    [SerializeField] private Material gizmoMaterial;
    [SerializeField] private bool     showGizmos = true;

    private Bounds        bounds;
    private ComputeBuffer gizmoBuffer;
    private ComputeBuffer argsBuffer;

    private FieldData[] densityField;
    private float[]     deltaField;       // 편집 누적값 (RefreshField에서 리셋 안 됨)

    public bool IsDirty { get; private set; }

    public FieldData[] DensityField => densityField;
    public int         Resolution   => resolution;
    public float       UnitSize     => unitSize;
    public float       WorldSize    => (resolution - 1) * unitSize;

    void Start()
    {
        InitField();
        InitGizmo();
    }

    private void Update()
    {
        if (showGizmos && gizmoBuffer != null && gizmoMaterial != null)
            Graphics.DrawMeshInstancedIndirect(gizmoMesh, 0, gizmoMaterial, bounds, argsBuffer);
    }

    public void InitField()
    {
        int count = resolution * resolution * resolution;
        densityField = new FieldData[count];
        deltaField   = new float[count];
        RefreshField();
        IsDirty = true;   // 최초 생성 후 메시 생성 트리거
    }

    public void ClearDirty() => IsDirty = false;

    public void SetResolution(int newResolution)
    {
        resolution = newResolution;
        deltaField = null;
        InitField();
    }

    public void ResetField()
    {
        if (deltaField != null)
            Array.Clear(deltaField, 0, deltaField.Length);
        IsDirty = false;
    }

    private void RefreshField()
    {
        switch (fieldType)
        {
            case FieldType.Sphere:    CreateSphere(transform.position);    break;
            case FieldType.Terrain2D: CreateTerrain2D(transform.position); break;
        }
    }

    private float3 GetCenter() => fieldType switch
    {
        FieldType.Sphere    => new float3(1, 1, 1) * (resolution - 1) / 2f,
        FieldType.Terrain2D => new float3((resolution - 1) / 2f, 0f, (resolution - 1) / 2f),
        _                   => new float3(1, 1, 1) * (resolution - 1) / 2f
    };

    // ── Sphere (노이즈: 3D) ───────────────────────────────────────
    private void CreateSphere(float3 centerPos)
    {
        if (DensityField == null || DensityField.Length == 0) InitField();

        var center = GetCenter();
        for (var i = 0; i < DensityField.Length; i++)
        {
            var x   = i % resolution;
            var y   = (i / resolution) % resolution;
            var z   = i / (resolution * resolution);
            var pos = (new float3(x, y, z) - center) * unitSize + centerPos;

            float density = math.distance(pos, centerPos) - sphereRadius;
            if (sphereNoise.applyNoise)
                density += FBm3D(pos, sphereNoise.frequency) * sphereNoise.amplitude;

            DensityField[i] = new FieldData { position = pos, density = density + deltaField[i] };
        }
    }

    // ── Terrain2D (노이즈: 2D xz) ─────────────────────────────────
    private void CreateTerrain2D(float3 originPos)
    {
        var center = GetCenter();
        for (var i = 0; i < densityField.Length; i++)
        {
            var x   = i % resolution;
            var y   = (i / resolution) % resolution;
            var z   = i / (resolution * resolution);
            var pos = (new float3(x, y, z) - center) * unitSize + originPos;

            float density = pos.y - terrain_baseHeight;
            if (terrainNoise.applyNoise)
                density -= FBm2D(new float2(pos.x, pos.z), terrainNoise.frequency) * terrainNoise.amplitude;

            densityField[i] = new FieldData { position = pos, density = density + deltaField[i] };
        }
    }

    // ── fBm ──────────────────────────────────────────────────────
    private float FBm2D(float2 p, float frequency)
    {
        float value = 0f, amp = 1f, freq = frequency, norm = 0f;
        for (int i = 0; i < NOISE_OCTAVES; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= NOISE_LACUNARITY;
            amp   *= NOISE_GAIN;
        }
        return value / norm;
    }

    private float FBm3D(float3 p, float frequency)
    {
        float value = 0f, amp = 1f, freq = frequency, norm = 0f;
        for (int i = 0; i < NOISE_OCTAVES; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= NOISE_LACUNARITY;
            amp   *= NOISE_GAIN;
        }
        return value / norm;
    }

    // ── Terrain Edit ──────────────────────────────────────────────
    // delta > 0 : 파내기 (density 올림 → 공기)
    // delta < 0 : 채우기 (density 내림 → 땅)
    public void ModifyDensity(float3 center, float radius, float delta)
    {
        float radiusSq = radius * radius;

        for (int i = 0; i < densityField.Length; i++)
        {
            float distSq = math.distancesq(densityField[i].position, center);
            if (distSq >= radiusSq) continue;

            float t = 1f - math.sqrt(distSq) / radius;   // 1=중앙, 0=경계
            deltaField[i] += delta * t * t;               // falloff 적용 후 누적
        }

        RefreshField();                                           // 절차적 + delta 즉시 합산
        if (gizmoBuffer != null) gizmoBuffer.SetData(densityField); // 기즈모도 즉시 갱신
        IsDirty = true;                                           // Generator에 메시 재생성 신호
    }

    // ── Gizmo ─────────────────────────────────────────────────────
    private void InitGizmo()
    {
        if (gizmoMaterial == null || gizmoMesh == null) return;

        int count = resolution * resolution * resolution;

        float size = resolution * unitSize;
        bounds = new Bounds(transform.position, new Vector3(size, size, size));

        gizmoBuffer = new ComputeBuffer(count, STRIDE);
        gizmoBuffer.SetData(densityField);

        gizmoMaterial = new Material(gizmoMaterial);   // 청크마다 독립 인스턴스
        gizmoMaterial.enableInstancing = true;
        gizmoMaterial.SetBuffer(GizmoBufferProperty, gizmoBuffer);

        uint[] args = {
            gizmoMesh.GetIndexCount(0),
            (uint)count,
            gizmoMesh.GetIndexStart(0),
            gizmoMesh.GetBaseVertex(0),
            0
        };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
    }

    private void OnDestroy()
    {
        gizmoBuffer?.Dispose();
        argsBuffer?.Dispose();
        if (gizmoMaterial != null) Destroy(gizmoMaterial);
    }
}
