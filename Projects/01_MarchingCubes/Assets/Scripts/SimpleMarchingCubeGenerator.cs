using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class SimpleMarchingCubeGenerator : MonoBehaviour
{
    public enum InterpolateMode { Linear, Half, Smoothstep, Snapping }

    [SerializeField] private SimpleDensityField densityField;
    [SerializeField] private InterpolateMode    interpolateMode = InterpolateMode.Linear;
    [SerializeField, Range(-5f, 5f)] private float isoLevel    = 0f;
    [SerializeField] private int                lodStep        = 1;

    public int LodStep
    {
        get => lodStep;
        set => lodStep = Mathf.Max(1, value);
    }

    private MeshFilter   meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Mesh         mesh;

    private int[] cubeCorners = new int[8];
    private List<Vector3> vertices        = new();
    private List<int>     triangleIndices = new();

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
        if (meshCollider != null) meshCollider.sharedMesh = null;
        mesh?.Clear();
    }

    private void GenerateMesh()
    {
        var fieldBuffer   = densityField.DensityField;
        var resolution    = densityField.Resolution;   // 큐브 수
        var pointsPerAxis = resolution + 1;            // 점 수
        var origin        = (Unity.Mathematics.float3)transform.position;
        var step          = Mathf.Max(1, lodStep);

        vertices.Clear();
        triangleIndices.Clear();

        for (var x = 0; x < resolution; x += step)
        for (var y = 0; y < resolution; y += step)
        for (var z = 0; z < resolution; z += step)
        {
            foreach (var triangle in MarchCube(new int3(x, y, z), pointsPerAxis, fieldBuffer, step))
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

    private List<Triangle> MarchCube(int3 p, int pts, FieldData[] fieldBuffer, int step)
    {
        cubeCorners[0] = Utils.Flatten(new int3(p.x,        p.y,        p.z       ), pts);
        cubeCorners[1] = Utils.Flatten(new int3(p.x + step, p.y,        p.z       ), pts);
        cubeCorners[2] = Utils.Flatten(new int3(p.x + step, p.y,        p.z + step), pts);
        cubeCorners[3] = Utils.Flatten(new int3(p.x,        p.y,        p.z + step), pts);
        cubeCorners[4] = Utils.Flatten(new int3(p.x,        p.y + step, p.z       ), pts);
        cubeCorners[5] = Utils.Flatten(new int3(p.x + step, p.y + step, p.z       ), pts);
        cubeCorners[6] = Utils.Flatten(new int3(p.x + step, p.y + step, p.z + step), pts);
        cubeCorners[7] = Utils.Flatten(new int3(p.x,        p.y + step, p.z + step), pts);

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

    private float Linear(float v1, float v2)    => (isoLevel - v1) / (v2 - v1);
    private float Half()                         => 0.5f;
    private float Smoothstep(float v1, float v2) { var t = Linear(v1, v2); return t * t * (3f - 2f * t); }
    private float Snapping(float v1, float v2, float snap = 0.2f)
    {
        var t = Linear(v1, v2);
        if (t < snap) return 0f;
        if (t > 1f - snap) return 1f;
        return t;
    }
}
