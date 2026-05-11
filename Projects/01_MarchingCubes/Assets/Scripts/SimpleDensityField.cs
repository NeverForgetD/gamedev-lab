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
    [SerializeField] private FieldType fieldType   = FieldType.Sphere;
    [SerializeField] private int       resolution  = 16;
    [SerializeField] private float     unitSize    = 1f;
    [SerializeField] private float     refreshRate = 0.3f;

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
    private float       timer;

    public FieldData[] DensityField => densityField;
    public int         Resolution   => resolution;
    public float       UnitSize     => unitSize;

    void Start()
    {
        InitField();
        InitGizmo();
    }

    private void Update()
    {
        if (gizmoBuffer == null || gizmoMaterial == null) return;

        timer += Time.deltaTime;
        if (timer > refreshRate)
        {
            RefreshField();
            gizmoBuffer.SetData(densityField);
            timer = 0;
        }

        if (showGizmos)
            Graphics.DrawMeshInstancedIndirect(gizmoMesh, 0, gizmoMaterial, bounds, argsBuffer);
    }

    public void InitField()
    {
        int count = resolution * resolution * resolution;
        densityField = new FieldData[count];
        RefreshField();
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

            DensityField[i] = new FieldData { position = pos, density = density };
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

            densityField[i] = new FieldData { position = pos, density = density };
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

    // ── Gizmo ─────────────────────────────────────────────────────
    private void InitGizmo()
    {
        if (gizmoMaterial == null || gizmoMesh == null) return;

        int count = resolution * resolution * resolution;

        float size = resolution * unitSize;
        bounds = new Bounds(transform.position, new Vector3(size, size, size));

        gizmoBuffer = new ComputeBuffer(count, STRIDE);
        gizmoBuffer.SetData(densityField);

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

        timer = 0;
    }

    private void OnDestroy()
    {
        gizmoBuffer?.Dispose();
        argsBuffer?.Dispose();
    }
}
