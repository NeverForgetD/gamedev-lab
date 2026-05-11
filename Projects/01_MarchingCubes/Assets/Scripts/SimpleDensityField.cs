using Unity.Mathematics;
using UnityEngine;

public struct FieldData
{
    public float3 position;
    public float density;
}

public enum NoiseType { _2D, _3D }

[System.Serializable]
public struct NoiseSettings
{
    public bool      enabled;
    public NoiseType noiseType;
    public float     frequency;
    public float     amplitude;
    [Range(1, 8)] public int octaves;
    public float     lacunarity;
    [Range(0f, 1f)] public float gain;
}

public class SimpleDensityField : MonoBehaviour
{
    private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
    private const int STRIDE = 16;

    public enum FieldType { Sphere, Terrain2D }

    [Header("Field Properties")]
    [SerializeField] private FieldType fieldType = FieldType.Sphere;
    [SerializeField] private int resolution = 16;
    [SerializeField] private float unitSize = 1f;
    [SerializeField] private float refreshRate = 0.3f;

    [Header("Sphere Properties")]
    [SerializeField] private float sphereRadius = 7.5f;
    [SerializeField] private NoiseSettings sphereNoise = new NoiseSettings
    {
        enabled    = false,
        noiseType  = NoiseType._3D,
        frequency  = 0.3f,
        amplitude  = 1f,
        octaves    = 4,
        lacunarity = 2f,
        gain       = 0.5f
    };

    [Header("Terrain2D Properties")]
    [SerializeField] private float terrain_baseHeight = 4f;
    [SerializeField] private NoiseSettings terrainNoise = new NoiseSettings
    {
        enabled    = true,
        noiseType  = NoiseType._2D,
        frequency  = 0.3f,
        amplitude  = 2f,
        octaves    = 4,
        lacunarity = 2f,
        gain       = 0.5f
    };

    [Header("Gizmos Settings")]
    [SerializeField] private Mesh gizmoMesh;
    [SerializeField] private Material gizmoMaterial;
    [SerializeField] private bool showGizmos = true;

    private Bounds bounds;
    private ComputeBuffer gizmoBuffer;
    private ComputeBuffer argsBuffer;

    private FieldData[] densityField;
    private float timer;

    public FieldData[] DensityField => densityField;
    public int Resolution => resolution;
    public float UnitSize => unitSize;

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

    // ── Sphere ────────────────────────────────────────────────
    private void CreateSphere(float3 centerPos)
    {
        if (DensityField == null || DensityField.Length == 0) InitField();

        var center = GetCenter();
        for (var i = 0; i < DensityField.Length; i++)
        {
            var x = i % resolution;
            var y = (i / resolution) % resolution;
            var z = i / (resolution * resolution);
            var pos = (new float3(x, y, z) - center) * unitSize + centerPos;

            float density = math.distance(pos, centerPos) - sphereRadius;
            density = ApplyNoise(density, pos, sphereNoise, noiseSign: 1f);

            DensityField[i] = new FieldData { position = pos, density = density };
        }
    }

    // ── Terrain2D ─────────────────────────────────────────────
    private void CreateTerrain2D(float3 originPos)
    {
        var center = GetCenter();
        for (var i = 0; i < densityField.Length; i++)
        {
            var x = i % resolution;
            var y = (i / resolution) % resolution;
            var z = i / (resolution * resolution);
            var pos = (new float3(x, y, z) - center) * unitSize + originPos;

            float density = pos.y - terrain_baseHeight;
            density = ApplyNoise(density, pos, terrainNoise, noiseSign: -1f);

            densityField[i] = new FieldData { position = pos, density = density };
        }
    }

    // ── Noise ─────────────────────────────────────────────────
    private float ApplyNoise(float density, float3 pos, NoiseSettings ns, float noiseSign = 1f)
    {
        if (!ns.enabled) return density;

        float fbm = ns.noiseType == NoiseType._2D
            ? FBm(new float2(pos.x, pos.z), ns)
            : FBm(pos, ns);

        return density + noiseSign * fbm * ns.amplitude;
    }

    private float FBm(float2 p, NoiseSettings ns)
    {
        float value = 0f, amp = 1f, freq = ns.frequency, norm = 0f;
        for (int i = 0; i < ns.octaves; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= ns.lacunarity;
            amp   *= ns.gain;
        }
        return value / norm;
    }

    private float FBm(float3 p, NoiseSettings ns)
    {
        float value = 0f, amp = 1f, freq = ns.frequency, norm = 0f;
        for (int i = 0; i < ns.octaves; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= ns.lacunarity;
            amp   *= ns.gain;
        }
        return value / norm;
    }

    // ── Gizmo ─────────────────────────────────────────────────
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
