using System.Collections.Generic;
using UnityEngine;

public class ChunkManager : MonoBehaviour
{
    [SerializeField] private Transform   player;
    [SerializeField] private GameObject  chunkPrefab;
    [SerializeField] private int         renderDistance     = 3;
    [SerializeField] private int         backRenderDistance = 1;
    [SerializeField] private int         chunksPerFrame     = 2;

    private float chunkWorldSize;

    private Vector2Int                        lastPlayerChunk = new(int.MaxValue, int.MaxValue);
    private Dictionary<Vector2Int, GameObject> activeChunks   = new();
    private Queue<Vector2Int>                  generateQueue  = new();
    private HashSet<Vector2Int>                queued         = new();

    void Start()
    {
        var sdf = chunkPrefab.GetComponent<SimpleDensityField>();
        chunkWorldSize = (sdf.Resolution - 1) * sdf.UnitSize;

        UpdateChunks(WorldToChunk(player.position));
    }

    void Update()
    {
        var playerChunk = WorldToChunk(player.position);

        if (playerChunk != lastPlayerChunk)
        {
            lastPlayerChunk = playerChunk;
            UpdateChunks(playerChunk);
        }

        int spawned = 0;
        while (spawned < chunksPerFrame && generateQueue.Count > 0)
        {
            var coord = generateQueue.Dequeue();
            queued.Remove(coord);
            if (!activeChunks.ContainsKey(coord))
                SpawnChunk(coord);
            spawned++;
        }
    }

    private void UpdateChunks(Vector2Int center)
    {
        Vector3 fwd   = player.forward;
        var     fwd2D = new Vector2(fwd.x, fwd.z).normalized;

        var needed = new HashSet<Vector2Int>();
        int range  = Mathf.Max(renderDistance, backRenderDistance);

        for (int x = -range; x <= range; x++)
        for (int z = -range; z <= range; z++)
        {
            var coord = new Vector2Int(center.x + x, center.y + z);
            var dir2D = new Vector2(x, z);
            int dist  = Mathf.RoundToInt(dir2D.magnitude);

            if (dist == 0) { needed.Add(coord); continue; }

            float dot   = Vector2.Dot(dir2D.normalized, fwd2D);
            bool  front = dot >= -0.2f;
            int   limit = front ? renderDistance : backRenderDistance;

            if (dist <= limit)
                needed.Add(coord);
        }

        var toRemove = new List<Vector2Int>();
        foreach (var coord in activeChunks.Keys)
            if (!needed.Contains(coord)) toRemove.Add(coord);

        foreach (var coord in toRemove)
        {
            Destroy(activeChunks[coord]);
            activeChunks.Remove(coord);
        }

        var toQueue = new List<Vector2Int>();
        foreach (var coord in needed)
            if (!activeChunks.ContainsKey(coord) && !queued.Contains(coord))
                toQueue.Add(coord);

        toQueue.Sort((a, b) => SqrDist(a, center).CompareTo(SqrDist(b, center)));

        foreach (var coord in toQueue)
        {
            generateQueue.Enqueue(coord);
            queued.Add(coord);
        }
    }

    private void SpawnChunk(Vector2Int coord)
    {
        var worldPos = new Vector3(coord.x * chunkWorldSize, 0f, coord.y * chunkWorldSize);
        var chunk    = Instantiate(chunkPrefab, worldPos, Quaternion.identity, transform);
        chunk.name   = $"Chunk_{coord.x}_{coord.y}";
        activeChunks[coord] = chunk;
    }

    public List<SimpleDensityField> GetChunksInRadius(Vector3 worldPos, float radius)
    {
        var   result   = new List<SimpleDensityField>();
        float halfSize = chunkWorldSize * 0.5f;

        foreach (var kvp in activeChunks)
        {
            var sdf = kvp.Value.GetComponent<SimpleDensityField>();
            if (sdf == null) continue;

            Vector3 center = kvp.Value.transform.position;
            float   cx     = Mathf.Clamp(worldPos.x, center.x - halfSize, center.x + halfSize);
            float   cz     = Mathf.Clamp(worldPos.z, center.z - halfSize, center.z + halfSize);
            float   dx     = worldPos.x - cx;
            float   dz     = worldPos.z - cz;

            if (dx * dx + dz * dz <= radius * radius)
                result.Add(sdf);
        }
        return result;
    }

    private Vector2Int WorldToChunk(Vector3 pos) => new(
        Mathf.FloorToInt(pos.x / chunkWorldSize),
        Mathf.FloorToInt(pos.z / chunkWorldSize)
    );

    private static int SqrDist(Vector2Int a, Vector2Int b)
    {
        int dx = a.x - b.x, dz = a.y - b.y;
        return dx * dx + dz * dz;
    }
}
