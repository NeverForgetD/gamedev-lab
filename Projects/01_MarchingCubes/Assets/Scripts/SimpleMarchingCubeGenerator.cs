using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class SimpleMarchingCubeGenerator : MonoBehaviour
{
    public enum InterpolateMode { Linear, Half, Smoothstep, Snapping }

    [SerializeField] private SimpleDensityField densityField;
    [SerializeField, Range(-5f, 5f)] private float isoLevel = 0.0f;
    [SerializeField] private InterpolateMode interpolateMode = InterpolateMode.Linear;

    private MeshFilter   meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh         mesh;

    private int[] cubeCorners = new int[8];
    private List<Vector3> vertices = new();
    private List<int> triangleIndices = new();

    private (int first, int second)[] edgeList =
    {
        (0, 1), (1, 2), (2, 3), (3, 0),
        (4, 5), (5, 6), (6, 7), (7, 4),
        (0, 4), (1, 5), (2, 6), (3, 7)
    };

    private void Start()
    {
        meshFilter   = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
            meshCollider = gameObject.AddComponent<MeshCollider>();
        mesh = new Mesh();
    }

    private void Update()
    {
        if (densityField.IsDirty)
        {
            GenerateMesh();
            densityField.ClearDirty();
        }
    }

    private void OnDisable()
    {
        mesh?.Clear();
    }

    private void GenerateMesh()
    {
        var fieldBuffer = densityField.DensityField;
        var resolution  = densityField.Resolution;
        var origin      = (Unity.Mathematics.float3)transform.position; // 월드→로컬 변환용

        vertices.Clear();
        triangleIndices.Clear();

        for (var x = 0; x < resolution - 1; x++)
        for (var y = 0; y < resolution - 1; y++)
        for (var z = 0; z < resolution - 1; z++)
        {
            foreach (var triangle in MarchCube(new int3(x, y, z), resolution, fieldBuffer))
            {
                var i = vertices.Count;
                vertices.Add((Vector3)(triangle.a - origin));
                vertices.Add((Vector3)(triangle.b - origin));
                vertices.Add((Vector3)(triangle.c - origin));
                triangleIndices.Add(i);
                triangleIndices.Add(i + 1);
                triangleIndices.Add(i + 2);
            }
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangleIndices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        meshFilter.mesh = mesh;

        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }

    private List<Triangle> MarchCube(int3 p, int resolution, FieldData[] fieldBuffer)
    {
        cubeCorners[0] = Utils.Flatten(new int3(p.x,     p.y,     p.z    ), resolution);
        cubeCorners[1] = Utils.Flatten(new int3(p.x + 1, p.y,     p.z    ), resolution);
        cubeCorners[2] = Utils.Flatten(new int3(p.x + 1, p.y,     p.z + 1), resolution);
        cubeCorners[3] = Utils.Flatten(new int3(p.x,     p.y,     p.z + 1), resolution);
        cubeCorners[4] = Utils.Flatten(new int3(p.x,     p.y + 1, p.z    ), resolution);
        cubeCorners[5] = Utils.Flatten(new int3(p.x + 1, p.y + 1, p.z    ), resolution);
        cubeCorners[6] = Utils.Flatten(new int3(p.x + 1, p.y + 1, p.z + 1), resolution);
        cubeCorners[7] = Utils.Flatten(new int3(p.x,     p.y + 1, p.z + 1), resolution);

        var cubeIndex = 0;
        for (var i = 0; i < 8; i++)
        {
            if (fieldBuffer[cubeCorners[i]].density < isoLevel)
                cubeIndex |= (1 << i);
        }

        var vertList = new float3[12];
        for (var i = 0; i < 12; i++)
        {
            if ((LookupTable.edgeTable[cubeIndex] & (1 << i)) == 0) continue;

            var p1 = fieldBuffer[cubeCorners[edgeList[i].first]].position;
            var p2 = fieldBuffer[cubeCorners[edgeList[i].second]].position;
            var v1 = fieldBuffer[cubeCorners[edgeList[i].first]].density;
            var v2 = fieldBuffer[cubeCorners[edgeList[i].second]].density;
            vertList[i] = Interpolate(p1, p2, v1, v2);
        }

        var triangleList = new List<Triangle>();
        for (int i = 0; LookupTable.triangleTable[cubeIndex, i] != -1; i += 3)
        {
            triangleList.Add(new Triangle
            {
                a = vertList[LookupTable.triangleTable[cubeIndex, i    ]],
                b = vertList[LookupTable.triangleTable[cubeIndex, i + 1]],
                c = vertList[LookupTable.triangleTable[cubeIndex, i + 2]]
            });
        }

        return triangleList;
    }

    private float3 Interpolate(float3 p1, float3 p2, float v1, float v2)
    {
        var t = interpolateMode switch
        {
            InterpolateMode.Linear     => Linear(v1, v2),
            InterpolateMode.Half       => 0.5f,
            InterpolateMode.Smoothstep => Smoothstep(v1, v2),
            InterpolateMode.Snapping   => Snapping(v1, v2),
            _                          => Linear(v1, v2)
        };
        return p1 + t * (p2 - p1);
    }

    // density 비율로 표면 위치를 선형으로 찾습니다 (표준)
    private float Linear(float v1, float v2)
    {
        return (isoLevel - v1) / (v2 - v1);
    }

    // density 무시하고 항상 엣지 중간점 → 각진 느낌
    private float Half() => 0.5f;

    // linear t 에 smoothstep 커브를 씌워 표면을 더 부드럽게
    private float Smoothstep(float v1, float v2)
    {
        var t = Linear(v1, v2);
        return t * t * (3f - 2f * t);
    }

    // density 가 한쪽 끝에 매우 가까우면 코너로 스냅 → 복셀 느낌
    private float Snapping(float v1, float v2, float snapThreshold = 0.2f)
    {
        var t = Linear(v1, v2);
        if (t < snapThreshold) return 0f;
        if (t > 1f - snapThreshold) return 1f;
        return t;
    }
}
