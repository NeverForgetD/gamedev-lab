using UnityEngine;

public class ChunkProfileTest : MonoBehaviour
{
    [SerializeField] private GameObject   chunkPrefab;
    [SerializeField] private ChunkProfile profile;
    [SerializeField] private float        spacing = 20f;

    // LOD0=step1, LOD1=step2, LOD2=step4, LOD3=step8
    private static readonly int[] lodSteps = { 1, 2, 4, 8 };

    void Start()
    {
        for (int lod = 0; lod < 4; lod++)
        {
            int step = lodSteps[lod];
            for (int col = 0; col < 4; col++)
            {
                var go = Instantiate(chunkPrefab, transform);
                go.name = $"Chunk_LOD{lod}_Col{col}";
                go.transform.position = new Vector3(col * spacing, 0f, lod * spacing);

                go.GetComponent<SimpleDensityField>().Profile = profile;
                go.GetComponent<SimpleMarchingCubeGenerator>().LodStep = step;
            }
        }
    }
}
