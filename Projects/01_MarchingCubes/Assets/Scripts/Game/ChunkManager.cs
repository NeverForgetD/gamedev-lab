using System.Collections.Generic;
using UnityEngine;

public class ChunkManager : MonoBehaviour
{
    [SerializeField] private Transform   player;
    [SerializeField] private GameObject  chunkPrefab;    // SimpleDensityField + SimpleMarchingCubeGenerator
    [SerializeField] private int         renderDistance = 1;   // 플레이어 기준 ±N 청크
    [SerializeField] private int         chunksPerFrame = 2;   // 프레임당 생성 개수

    private float chunkWorldSize;   // (resolution - 1) * unitSize

    private Vector2Int                    lastPlayerChunk = new(int.MaxValue, int.MaxValue);
    private Dictionary<Vector2Int, GameObject> activeChunks = new();
    private Queue<Vector2Int>             generateQueue    = new();
    private HashSet<Vector2Int>           queued           = new();   // Contains O(1)

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

        // 큐에서 프레임당 N개씩 생성
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
        // 필요한 청크 집합
        var needed = new HashSet<Vector2Int>();
        for (int x = -renderDistance; x <= renderDistance; x++)
        for (int z = -renderDistance; z <= renderDistance; z++)
            needed.Add(new Vector2Int(center.x + x, center.y + z));

        // 범위 밖 청크 제거
        var toRemove = new List<Vector2Int>();
        foreach (var coord in activeChunks.Keys)
            if (!needed.Contains(coord)) toRemove.Add(coord);

        foreach (var coord in toRemove)
        {
            Destroy(activeChunks[coord]);
            activeChunks.Remove(coord);
        }

        // 없는 청크 수집 후 거리순 정렬 → 가까운 것부터 큐에 추가
        var toQueue = new List<Vector2Int>();
        foreach (var coord in needed)
            if (!activeChunks.ContainsKey(coord) && !queued.Contains(coord))
                toQueue.Add(coord);

        toQueue.Sort((a, b) =>
            SqrDist(a, center).CompareTo(SqrDist(b, center)));

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

    private Vector2Int WorldToChunk(Vector3 pos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(pos.x / chunkWorldSize),
            Mathf.FloorToInt(pos.z / chunkWorldSize)
        );
    }

    private static int SqrDist(Vector2Int a, Vector2Int b)
    {
        int dx = a.x - b.x, dz = a.y - b.y;
        return dx * dx + dz * dz;
    }
}
