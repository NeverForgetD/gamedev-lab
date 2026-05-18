using UnityEngine;

[CreateAssetMenu(menuName = "MarchingCubes/ChunkProfile")]
public class ChunkProfile : ScriptableObject
{
    [Header("Field")]
    public SimpleDensityField.FieldType fieldType  = SimpleDensityField.FieldType.Terrain2D;
    public int   resolution = 16;
    public float unitSize   = 1f;

    [Header("Terrain2D")]
    public float         terrain_baseHeight = 5f;
    public NoiseSettings terrainNoise = new NoiseSettings
    {
        applyNoise = true,
        frequency  = 0.05f,
        amplitude  = 5f,
    };

    [Header("Sphere")]
    public float         sphereRadius = 5f;
    public NoiseSettings sphereNoise = new NoiseSettings
    {
        applyNoise = false,
        frequency  = 0.05f,
        amplitude  = 2f,
    };

    public float WorldSize => (resolution - 1) * unitSize;
}
