using System.Collections.Generic;
using UnityEngine;

public class SimpleTriangleDrawer : MonoBehaviour
{
    private MeshFilter meshFilter;
    private Mesh mesh;


    void Start()
    {
        meshFilter = gameObject.GetComponent<MeshFilter>();
        mesh = new Mesh();
        meshFilter.mesh = mesh;

        Draw();
    }

    private void Draw()
    {
        mesh.Clear();


        List<Vector3> vertices = new()
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0f, 0.5f, 0f)
        };
        List<int> triangleIndicies = new()
        {
            0, 1, 2
        };

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangleIndicies, 0);
    }
}
