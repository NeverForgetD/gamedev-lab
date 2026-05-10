using Unity.Mathematics;
using UnityEngine;

public struct FieldData
{
    public float3 position;
    public float density;
}

public class SimpleDensityField : MonoBehaviour
{
    [Header("Field Properties")]
    [SerializeField] private int resolution = 16;
    [SerializeField] private float unitSize = 1f;

    [Header("Sphere Shape Properties")]
    [SerializeField] private float sphereRadius = 4f;

    [Header("Gizmo Settings")]
    [SerializeField] private bool showGizmos = true;
    [SerializeField] private float pointSize = 0.1f;
    [SerializeField] private Color insideColor = new Color(0f, 1f, 0f, 0.8f);
    [SerializeField] private Color outsideColor = new Color(1f, 0f, 0f, 0.3f);


    public FieldData[] DensityField { get; private set; }
    public int Resolution => resolution;
    public float UnitSize => unitSize;

    void Start()
    {
        InitField();
        CreateSampleSphere(new float3(resolution, resolution, resolution) * unitSize * 0.5f);
    }

    public void InitField()
    {
        int count = resolution * resolution * resolution;
        DensityField = new FieldData[count];

        float3 center = new float3(resolution, resolution, resolution) * unitSize * 0.5f;
        CreateSampleSphere(center);
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

    private void OnDrawGizmos()
    {
        if (!showGizmos || DensityField == null || DensityField.Length == 0) return;

        float3 center = new float3(resolution, resolution, resolution) * unitSize * 0.5f;
        Vector3 wolrdCenter = Vector3.zero;

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(center, sphereRadius);

        foreach (var data in DensityField)
        {
            if (data.density > 0)
            {

                Gizmos.color = insideColor;
                wolrdCenter = transform.TransformPoint(data.position);
                Gizmos.DrawSphere(wolrdCenter, pointSize);
            }
            else
            {
                Gizmos.color = outsideColor;
                wolrdCenter = transform.TransformPoint(data.position);
                Gizmos.DrawSphere(wolrdCenter, pointSize * 0.5f);
            }
        }   
    }

}
