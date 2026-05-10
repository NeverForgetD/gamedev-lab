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
    private const int STRIDE = 16;  // sizeof(float3 + float)

    [Header("Field Properties")]
    [SerializeField] private int resolution = 16;
    [SerializeField] private float unitSize = 1f;
    [SerializeField] private float refreshRate = 0.3f;

    [Header("Sphere Shape Properties")]
    [SerializeField] private float sphereRadius = 4f;

    [Header("Gizmo Settings")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private float pointSize = 0.1f;
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
            CreateSampleSphere(transform.position);
            gizmoBuffer.SetData(densityField);
            timer = 0;  // -= refreshRate보다 더 명확
        }

        Graphics.DrawMeshInstancedIndirect(gizmoMesh, 0, gizmoMaterial, bounds, argsBuffer);
    }

    public void InitField()
    {
        int count = resolution * resolution * resolution;
        densityField = new FieldData[count];

        float3 center = new float3(resolution, resolution, resolution) * unitSize * 0.5f;
        CreateSampleSphere(center);
    }

    private void InitGizmo()
    {
        if (gizmoMaterial == null || gizmoMesh == null) return;

        int count = resolution * resolution * resolution;

        // Bounds: 실제 격자 크기로 계산
        float size = resolution * unitSize;
        bounds = new Bounds(transform.position, new Vector3(size, size, size));

        // ComputeBuffer: GPU 메모리에 데이터 할당
        gizmoBuffer = new ComputeBuffer(count, STRIDE);
        gizmoBuffer.SetData(densityField);

        // Material: Shader와 ComputeBuffer 연결
        gizmoMaterial.enableInstancing = true;
        gizmoMaterial.SetBuffer(GizmoBufferProperty, gizmoBuffer);

        // Indirect Args: DrawCall 정보
        uint[] args = {
                gizmoMesh.GetIndexCount(0),
                (uint)count,
                gizmoMesh.GetIndexStart(0),
                gizmoMesh.GetBaseVertex(0),
                0
            };
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint),
            ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);

        timer = 0;
    }

    private void CreateSampleSphere(float3 centerPos)
    {
        if (DensityField == null || DensityField.Length == 0) InitField();

        for (var i = 0; i < DensityField.Length; i++)
        {
            var x = i % resolution;
            var y = (i / resolution) % resolution;
            var z = i / (resolution * resolution);
            var center = new float3(resolution / 2f, resolution / 2f, resolution / 2f);
            var pos = (new float3(x, y, z) - center) * unitSize + centerPos;

            DensityField[i] = new FieldData
            {
                position = pos,
                density = math.distance(pos, centerPos) - sphereRadius
            };
        }
    }
    

    private void OnDestroy()
    {
        gizmoBuffer?.Dispose();
        argsBuffer?.Dispose();
    }
}
