using System.Collections.Generic;
using UnityEngine;

public class ChunkManager : MonoBehaviour
{
    [SerializeField] private GameObject chunkPrefab;
    [SerializeField] private Transform  player;
    [SerializeField] private Camera     cam;

    [Header("Streaming")]
    [SerializeField] private int renderDistance = 3;
    [SerializeField] private int chunksPerFrame = 2;

    private struct ChunkEntry
    {
        public SimpleDensityField field;
        public MeshRenderer       renderer;
    }

    private Dictionary<Vector2Int, ChunkEntry> activeChunks  = new();
    private Queue<GameObject>                  pool          = new();
    private List<Vector2Int>                   generateQueue = new();
    private Plane[]                            frustumPlanes = new Plane[6];

    private Vector2Int lastPlayerChunk = new(int.MaxValue, int.MaxValue);
    private float      chunkWorldSize;

    private void Start()
    {
        var sampleField = chunkPrefab.GetComponent<SimpleDensityField>();
        chunkWorldSize = sampleField.WorldSize;

        int side     = renderDistance * 2 + 1;
        int poolSize = side * side;
        for (int i = 0; i < poolSize; i++)
        {
            var go = Instantiate(chunkPrefab, transform);
            go.SetActive(false);
            pool.Enqueue(go);
        }

        UpdateChunks();
    }

    private void Update()
    {
        var currentChunk = WorldToChunk(player.position);
        if (currentChunk != lastPlayerChunk)
        {
            lastPlayerChunk = currentChunk;
            UpdateChunks();
        }

        ProcessGenerateQueue();
        UpdateFrustumVisibility();
    }

    private void UpdateChunks()
    {
        var needed = new HashSet<Vector2Int>();
        for (int x = -renderDistance; x <= renderDistance; x++)
        for (int z = -renderDistance; z <= renderDistance; z++)
            needed.Add(lastPlayerChunk + new Vector2Int(x, z));

        var toRemove = new List<Vector2Int>();
        foreach (var coord in activeChunks.Keys)
            if (!needed.Contains(coord)) toRemove.Add(coord);

        foreach (var coord in toRemove)
        {
            var entry = activeChunks[coord];
            entry.field.ResetField();
            entry.field.gameObject.SetActive(false);
            pool.Enqueue(entry.field.gameObject);
            activeChunks.Remove(coord);
            generateQueue.Remove(coord);
        }

        foreach (var coord in needed)
        {
            if (activeChunks.ContainsKey(coord)) continue;
            if (pool.Count == 0) continue;

            var go = pool.Dequeue();
            go.transform.position = ChunkToWorld(coord);
            go.name = $"Chunk_{coord.x}_{coord.y}";
            go.SetActive(true);

            activeChunks[coord] = new ChunkEntry
            {
                field    = go.GetComponent<SimpleDensityField>(),
                renderer = go.GetComponent<MeshRenderer>(),
            };

            generateQueue.Add(coord);
        }

        SortGenerateQueue();
    }

    private void ProcessGenerateQueue()
    {
        int count = 0;
        while (generateQueue.Count > 0 && count < chunksPerFrame)
        {
            var coord = generateQueue[0];
            generateQueue.RemoveAt(0);

            if (activeChunks.TryGetValue(coord, out var entry))
                entry.field.InitField();

            count++;
        }
    }

    private void UpdateFrustumVisibility()
    {
        if (cam == null) return;

        GeometryUtility.CalculateFrustumPlanes(cam, frustumPlanes);
        float half = chunkWorldSize * 0.5f;

        foreach (var (coord, entry) in activeChunks)
        {
            if (entry.renderer == null) continue;
            var center = ChunkToWorld(coord) + new Vector3(0f, half, 0f);
            var bounds = new Bounds(center, new Vector3(chunkWorldSize, chunkWorldSize, chunkWorldSize));
            entry.renderer.enabled = GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
        }
    }

    public List<SimpleDensityField> GetChunksInRadius(Vector3 worldPos, float radius)
    {
        var   result   = new List<SimpleDensityField>();
        float halfSize = chunkWorldSize * 0.5f;
        float radiusSq = radius * radius;

        foreach (var (coord, entry) in activeChunks)
        {
            var   center = ChunkToWorld(coord);
            float cx     = Mathf.Clamp(worldPos.x, center.x - halfSize, center.x + halfSize);
            float cz     = Mathf.Clamp(worldPos.z, center.z - halfSize, center.z + halfSize);
            float dx     = worldPos.x - cx;
            float dz     = worldPos.z - cz;

            if (dx * dx + dz * dz <= radiusSq)
                result.Add(entry.field);
        }
        return result;
    }

    private Vector2Int WorldToChunk(Vector3 pos) => new(
        Mathf.RoundToInt(pos.x / chunkWorldSize),
        Mathf.RoundToInt(pos.z / chunkWorldSize)
    );

    private Vector3 ChunkToWorld(Vector2Int coord) =>
        new Vector3(coord.x * chunkWorldSize, 0f, coord.y * chunkWorldSize);

    private void SortGenerateQueue()
    {
        var playerChunk = lastPlayerChunk;
        generateQueue.Sort((a, b) =>
            ManhattanDist(a, playerChunk).CompareTo(ManhattanDist(b, playerChunk)));
    }

    private static int ManhattanDist(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
}
