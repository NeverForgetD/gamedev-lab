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

    [Header("LOD")]
    [Tooltip("OFF = 모든 청크 LodStep 1 (풀 해상도)")]
    [SerializeField] private bool enableLOD = true;
    [SerializeField] private LodBand[] lodBands = new LodBand[]
    {
        new() { maxDistance = 1, lodStep = 1 },  // 인접 청크 : 풀 해상도
        new() { maxDistance = 3, lodStep = 2 },  // 중거리    : 4배 절감
        new() { maxDistance = 99, lodStep = 4 }, // 원거리    : 16배 절감
    };

    // ── 청크 데이터 ───────────────────────────────────────────────────
    [System.Serializable]
    public struct LodBand
    {
        [Tooltip("플레이어 청크로부터의 맨해튼 거리 이하일 때 적용")]
        public int maxDistance;
        public int lodStep;
    }

    private struct ChunkEntry
    {
        public SimpleDensityField          field;
        public SimpleMarchingCubeGenerator generator;
        public MeshRenderer                renderer;
        public int                         lodStep; // 현재 적용 중인 LOD 단계
    }

    private Dictionary<Vector2Int, ChunkEntry> activeChunks  = new();
    private Queue<GameObject>                  pool          = new();
    private List<Vector2Int>                   generateQueue = new();
    private Plane[]                            frustumPlanes = new Plane[6];

    private Vector2Int lastPlayerChunk = new(int.MaxValue, int.MaxValue);
    private float      chunkWorldSize;

    // ─────────────────────────────────────────────────────────────────
    private void Start()
    {
        chunkWorldSize = chunkPrefab.GetComponent<SimpleDensityField>().WorldSize;

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

    // ─────────────────────────────────────────────────────────────────
    // 청크 추가 / 제거
    // ─────────────────────────────────────────────────────────────────
    private void UpdateChunks()
    {
        var needed = new HashSet<Vector2Int>();
        for (int x = -renderDistance; x <= renderDistance; x++)
        for (int z = -renderDistance; z <= renderDistance; z++)
            needed.Add(lastPlayerChunk + new Vector2Int(x, z));

        // 범위 밖 청크 제거
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

        // 새 청크 활성화
        foreach (var coord in needed)
        {
            if (activeChunks.ContainsKey(coord)) continue;
            if (pool.Count == 0) continue;

            var go = pool.Dequeue();
            go.transform.position = ChunkToWorld(coord);
            go.name = $"Chunk_{coord.x}_{coord.y}";
            go.SetActive(true);

            int step = GetLodStep(ManhattanDist(coord, lastPlayerChunk));
            activeChunks[coord] = new ChunkEntry
            {
                field     = go.GetComponent<SimpleDensityField>(),
                generator = go.GetComponent<SimpleMarchingCubeGenerator>(),
                renderer  = go.GetComponent<MeshRenderer>(),
                lodStep   = step,
            };

            generateQueue.Add(coord);
        }

        SortGenerateQueue();

        // 기존 활성 청크 LOD 갱신
        UpdateChunkLOD();
    }

    // ─────────────────────────────────────────────────────────────────
    // 생성 큐 처리
    // ─────────────────────────────────────────────────────────────────
    private void ProcessGenerateQueue()
    {
        int count = 0;
        while (generateQueue.Count > 0 && count < chunksPerFrame)
        {
            var coord = generateQueue[0];
            generateQueue.RemoveAt(0);

            if (!activeChunks.TryGetValue(coord, out var entry)) { count++; continue; }

            // LOD 스텝을 InitField 직전에 반영
            int step = GetLodStep(ManhattanDist(coord, lastPlayerChunk));
            entry.generator.LodStep = step;

            // ChunkEntry 는 struct 이므로 수정 후 재저장
            entry.lodStep        = step;
            activeChunks[coord]  = entry;

            entry.field.InitField();
            count++;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // LOD 갱신 — 플레이어가 이동할 때마다 기존 청크의 LOD 를 업데이트
    // ─────────────────────────────────────────────────────────────────
    private void UpdateChunkLOD()
    {
        if (!enableLOD) return;

        // activeChunks 를 수정하므로 키 목록을 미리 복사
        var coords = new List<Vector2Int>(activeChunks.Keys);

        foreach (var coord in coords)
        {
            var entry   = activeChunks[coord];
            int newStep = GetLodStep(ManhattanDist(coord, lastPlayerChunk));

            if (entry.lodStep == newStep) continue; // 변화 없으면 스킵

            entry.generator.LodStep = newStep;
            entry.lodStep           = newStep;
            activeChunks[coord]     = entry;

            // 밀도 재계산 없이 MC Job 만 재예약
            entry.generator.TriggerRebuild();
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // 프러스텀 컬링
    // ─────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────
    // 공개 유틸
    // ─────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────
    // 내부 헬퍼
    // ─────────────────────────────────────────────────────────────────

    /// <summary>LOD 비활성화 시 항상 1, 활성화 시 거리에 맞는 LodStep 반환</summary>
    private int GetLodStep(int distance)
    {
        if (!enableLOD) return 1;

        foreach (var band in lodBands)
            if (distance <= band.maxDistance) return band.lodStep;

        // 모든 밴드 초과 시 마지막 밴드 값 사용
        return lodBands[lodBands.Length - 1].lodStep;
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
