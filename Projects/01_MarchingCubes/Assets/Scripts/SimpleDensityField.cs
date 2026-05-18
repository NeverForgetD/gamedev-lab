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

    private const int   NOISE_OCTAVES    = 4;
    private const float NOISE_LACUNARITY = 2f;
    private const float NOISE_GAIN       = 0.5f;

    public enum FieldType { Sphere, Terrain2D }

    [SerializeField] private ChunkProfile profile;

    [Header("Gizmos Settings")]
    [SerializeField] private Mesh     gizmoMesh;
    [SerializeField] private Material gizmoMaterial;
    [SerializeField] private bool     showGizmos = true;

    private Bounds        bounds;
    private ComputeBuffer gizmoBuffer;
    private ComputeBuffer argsBuffer;

    private FieldData[] densityField;
    private float[]     deltaField;

    public bool IsDirty { get; private set; }

    public ChunkProfile Profile
    {
        get => profile;
        set => profile = value;
    }

    public FieldData[] DensityField => densityField;
    public int         Resolution   => profile.resolution;
    public float       UnitSize     => profile.UnitSize;
    public float       WorldSize    => profile.WorldSize;

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
        if (profile == null) { Debug.LogError("ChunkProfile not assigned", this); return; }

        int pts   = profile.resolution + 1;
        int count = pts * pts * pts;
        densityField = new FieldData[count];
        deltaField   = new float[count];
        RefreshField();
        IsDirty = true;
    }

    public void ClearDirty() => IsDirty = false;

    public void ResetField()
    {
        if (deltaField != null)
            Array.Clear(deltaField, 0, deltaField.Length);
        IsDirty = false;
    }

    private void RefreshField()
    {
        switch (profile.fieldType)
        {
            case FieldType.Sphere:    CreateSphere(transform.position);    break;
            case FieldType.Terrain2D: CreateTerrain2D(transform.position); break;
        }
    }

    private float3 GetCenter() => profile.fieldType switch
    {
        FieldType.Sphere    => new float3(1, 1, 1) * profile.resolution / 2f,
        FieldType.Terrain2D => new float3(profile.resolution / 2f, 0f, profile.resolution / 2f),
        _                   => new float3(1, 1, 1) * profile.resolution / 2f
    };

    // ── Sphere ────────────────────────────────────────────────────
    private void CreateSphere(float3 centerPos)
    {
        int pts    = profile.resolution + 1;
        var center = GetCenter();
        for (var i = 0; i < densityField.Length; i++)
        {
            var x   = i % pts;
            var y   = (i / pts) % pts;
            var z   = i / (pts * pts);
            var pos = (new float3(x, y, z) - center) * profile.UnitSize + centerPos;

            float density = math.distance(pos, centerPos) - profile.sphereRadius;
            if (profile.sphereNoise.applyNoise)
                density += FBm3D(pos, profile.sphereNoise.frequency) * profile.sphereNoise.amplitude;

            densityField[i] = new FieldData { position = pos, density = density + deltaField[i] };
        }
    }

    // ── Terrain2D ─────────────────────────────────────────────────
    private void CreateTerrain2D(float3 originPos)
    {
        int pts    = profile.resolution + 1;
        var center = GetCenter();
        for (var i = 0; i < densityField.Length; i++)
        {
            var x   = i % pts;
            var y   = (i / pts) % pts;
            var z   = i / (pts * pts);
            var pos = (new float3(x, y, z) - center) * profile.UnitSize + originPos;

            float density = pos.y - profile.terrain_baseHeight;
            if (profile.terrainNoise.applyNoise)
                density -= FBm2D(new float2(pos.x, pos.z), profile.terrainNoise.frequency) * profile.terrainNoise.amplitude;

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
    public void ModifyDensity(float3 center, float radius, float delta)
    {
        float radiusSq = radius * radius;

        for (int i = 0; i < densityField.Length; i++)
        {
            float distSq = math.distancesq(densityField[i].position, center);
            if (distSq >= radiusSq) continue;

            float t = 1f - math.sqrt(distSq) / radius;
            deltaField[i] += delta * t * t;
        }

        RefreshField();
        if (gizmoBuffer != null) gizmoBuffer.SetData(densityField);
        IsDirty = true;
    }

    // ── Gizmo ─────────────────────────────────────────────────────
    private void InitGizmo()
    {
        if (gizmoMaterial == null || gizmoMesh == null) return;

        int pts   = profile.resolution + 1;
        int count = pts * pts * pts;
        float size = profile.WorldSize;
        bounds = new Bounds(transform.position, new Vector3(size, size, size));

        gizmoBuffer = new ComputeBuffer(count, STRIDE);
        gizmoBuffer.SetData(densityField);

        gizmoMaterial = new Material(gizmoMaterial);
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
