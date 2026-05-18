using UnityEngine;

public class ChunkProfileTest : MonoBehaviour
{
    [SerializeField] private GameObject    chunkPrefab;
    [SerializeField] private ChunkProfile[] profiles = new ChunkProfile[4];
    [SerializeField] private float          spacing  = 16f;

    void Start()
    {
        for (int row = 0; row < 4; row++)
        {
            var profile = profiles[row];
            for (int col = 0; col < 4; col++)
            {
                var go = Instantiate(chunkPrefab, transform);
                go.name = $"Chunk_Row{row}_Col{col}";
                go.transform.position = new Vector3(col * spacing, 0f, row * spacing);

                var field = go.GetComponent<SimpleDensityField>();
                field.Profile = profile;
            }
        }
    }
}
