using UnityEngine;

public class LodDistanceTest : MonoBehaviour
{
    [SerializeField] private GameObject   chunkPrefab;
    [SerializeField] private ChunkProfile profile;
    [SerializeField] private Transform    player;

    [Header("Grid")]
    [SerializeField] private int   gridSize = 7;
    [SerializeField] private float spacing  = 20f;

    [Header("LOD 거리 임계값")]
    [SerializeField] private float lod0MaxDist = 30f;
    [SerializeField] private float lod1MaxDist = 60f;
    [SerializeField] private float lod2MaxDist = 100f;
    // lod2MaxDist 초과 → LOD3 (step=8)

    private struct ChunkData
    {
        public Transform               transform;
        public SimpleDensityField      field;
        public SimpleMarchingCubeGenerator generator;
        public int                     currentLod;
    }

    private ChunkData[] chunks;

    void Start()
    {
        int total = gridSize * gridSize;
        chunks = new ChunkData[total];

        float offset = (gridSize - 1) * spacing * 0.5f;

        for (int i = 0; i < gridSize; i++)
        for (int j = 0; j < gridSize; j++)
        {
            var go = Instantiate(chunkPrefab, transform);
            go.transform.position = new Vector3(i * spacing - offset, 0f, j * spacing - offset);
            go.name = $"Chunk_{i}_{j}";

            var field = go.GetComponent<SimpleDensityField>();
            field.Profile = profile;

            int idx = i * gridSize + j;
            chunks[idx] = new ChunkData
            {
                transform  = go.transform,
                field      = field,
                generator  = go.GetComponent<SimpleMarchingCubeGenerator>(),
                currentLod = -1,
            };
        }
    }

    void Update()
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            float dist = Vector3.Distance(player.position, chunks[i].transform.position);
            int   lod  = GetLod(dist);

            if (lod == chunks[i].currentLod) continue;

            chunks[i].generator.LodStep = LodToStep(lod);
            chunks[i].field.MarkDirty();

            var tmp = chunks[i];
            tmp.currentLod = lod;
            chunks[i] = tmp;
        }
    }

    private int GetLod(float dist)
    {
        if (dist < lod0MaxDist) return 0;
        if (dist < lod1MaxDist) return 1;
        if (dist < lod2MaxDist) return 2;
        return 3;
    }

    private static int LodToStep(int lod) => lod switch
    {
        0 => 1,
        1 => 2,
        2 => 4,
        _ => 8,
    };
}
