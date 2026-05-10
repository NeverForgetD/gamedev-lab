using Unity.Mathematics;
using UnityEngine;

public struct FieldData
{
    public float3 position;
    public float density;
}

public class SimpleDensityField : MonoBehaviour
{
    private static readonly int GizmoBufferProperty = Shader.PropertyToID("_GizmoBuffer");
    private const int STRIDE = 16;

    public enum FieldType { Sphere, Terrain2D, Cave3D }

    [Header("Field Properties")]
    [SerializeField] private FieldType fieldType = FieldType.Sphere;
    [SerializeField] private int resolution = 16;
    [SerializeField] private float unitSize = 1f;
    [SerializeField] private float refreshRate = 0.3f;

    [Header("Sphere Properties")]
    [SerializeField] private float sphereRadius = 7.5f;

    [Header("Terrain2D Properties")]
    [SerializeField] private float terrain_baseHeight  = 4f;
    [SerializeField] private float terrain_frequency   = 0.3f;
    [SerializeField] private float terrain_amplitude   = 2f;
    private int   terrain_octaves   = 4;
    private float terrain_lacunarity = 2f;
    private float terrain_gain       = 0.5f;

    [Header("Cave3D Properties")]
    [SerializeField] private float cave_frequency     = 0.3f;
    [SerializeField] private float cave_amplitude     = 1f;
    [SerializeField] private float cave_surfaceLevel  = 0f;
    private int   cave_octaves    = 4;
    private float cave_lacunarity = 2f;
    private float cave_gain       = 0.5f;

    [Header("Render Settings")]
    [SerializeField] private Mesh gizmoMesh;
    [SerializeField] private Material gizmoMaterial;

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
            case FieldType.Sphere:    CreateSphere(transform.position);   break;
            case FieldType.Terrain2D: CreateTerrain2D(transform.position); break;
            case FieldType.Cave3D:    CreateCave3D(transform.position);    break;
        }
    }

    private float3 GetCenter() => fieldType switch
    {
        FieldType.Sphere    => new float3(1, 1, 1) * (resolution - 1) / 2f,
        FieldType.Terrain2D => new float3((resolution - 1) / 2f, 0f, (resolution - 1) / 2f),
        FieldType.Cave3D    => new float3(1, 1, 1) * (resolution - 1) / 2f,
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

            DensityField[i] = new FieldData
            {
                position = pos,
                density  = math.distance(pos, centerPos) - sphereRadius
            };
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

            var surfaceHeight = terrain_baseHeight
                              + FBm2D(new float2(pos.x, pos.z)) * terrain_amplitude;

            densityField[i] = new FieldData
            {
                position = pos,
                density  = pos.y - surfaceHeight
            };
        }
    }

    // ── Cave3D ────────────────────────────────────────────────
    private void CreateCave3D(float3 originPos)
    {
        var center = GetCenter();
        for (var i = 0; i < densityField.Length; i++)
        {
            var x = i % resolution;
            var y = (i / resolution) % resolution;
            var z = i / (resolution * resolution);
            var pos = (new float3(x, y, z) - center) * unitSize + originPos;

            // 높이에 비례해 density 증가 → 위는 공기, 아래는 땅
            // 3D 노이즈가 그 사이를 조각해 동굴/오버행 생성
            densityField[i] = new FieldData
            {
                position = pos,
                density  = cave_surfaceLevel - FBm3D(pos) * cave_amplitude
            };
        }
    }

    // ── fBm ───────────────────────────────────────────────────
    private float FBm2D(float2 p)
    {
        float value = 0f, amp = 1f, freq = terrain_frequency, norm = 0f;
        for (int i = 0; i < terrain_octaves; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= terrain_lacunarity;
            amp   *= terrain_gain;
        }
        return value / norm;
    }

    private float FBm3D(float3 p)
    {
        float value = 0f, amp = 1f, freq = cave_frequency, norm = 0f;
        for (int i = 0; i < cave_octaves; i++)
        {
            value += noise.snoise(p * freq) * amp;
            norm  += amp;
            freq  *= cave_lacunarity;
            amp   *= cave_gain;
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
