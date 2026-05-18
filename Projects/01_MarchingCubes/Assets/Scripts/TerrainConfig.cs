using UnityEngine;

[CreateAssetMenu(menuName = "MarchingCubes/TerrainConfig")]
public class TerrainConfig : ScriptableObject
{
    public float worldSize = 15f;
    public float isoLevel  = 0f;
}
