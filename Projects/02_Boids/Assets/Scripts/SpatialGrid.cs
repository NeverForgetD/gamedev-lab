using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spatial Hash Grid — 공간을 균일한 셀(cellSize 변 길이)로 나누고 Boid를 셀에 해싱한다.
/// 셀 크기를 perceptionRadius로 잡으면 반경 내 이웃은 반드시 자신의 셀 + 인접 26셀(3×3×3)
/// 안에 존재하므로, 전수 탐색(O(N²)) 대신 주변 27셀만 검사해 평균 O(N·k)로 줄인다.
///
/// 무한 공간을 다루기 위해 고정 배열이 아닌 해시(Dictionary)를 사용한다 →
/// Boid가 경계 밖으로 멀리 퍼져도 메모리가 좌표 범위에 묶이지 않는다.
/// </summary>
public class SpatialGrid
{
    private readonly float _cellSize;
    private readonly float _invCellSize;

    // 셀 키(3D 좌표 인코딩) → 해당 셀에 속한 Boid 인덱스 목록.
    // 리스트는 매 프레임 Clear만 하고 재사용해 GC 할당을 피한다.
    private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();

    // Neighbors()가 반환하는 재사용 버퍼 (호출마다 새 할당 방지).
    private readonly List<int> _neighborBuf = new List<int>(64);

    public float CellSize => _cellSize;

    public SpatialGrid(float cellSize)
    {
        _cellSize = Mathf.Max(0.0001f, cellSize);
        _invCellSize = 1f / _cellSize;
    }

    /// <summary>매 프레임 모든 Boid를 셀에 다시 배치한다.</summary>
    public void Rebuild(BoidData[] data)
    {
        foreach (var list in _cells.Values)
            list.Clear();

        for (int i = 0; i < data.Length; i++)
        {
            long key = KeyOf(data[i].position);
            if (!_cells.TryGetValue(key, out var list))
            {
                list = new List<int>(8);
                _cells[key] = list;
            }
            list.Add(i);
        }
    }

    /// <summary>pos 주변 27셀에 속한 Boid 인덱스들을 재사용 버퍼에 담아 반환.</summary>
    public List<int> Neighbors(Vector3 pos)
    {
        _neighborBuf.Clear();

        int cx = FloorToCell(pos.x);
        int cy = FloorToCell(pos.y);
        int cz = FloorToCell(pos.z);

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            long key = Encode(cx + dx, cy + dy, cz + dz);
            if (_cells.TryGetValue(key, out var list))
                _neighborBuf.AddRange(list);
        }

        return _neighborBuf;
    }

    /// <summary>디버그 시각화용 — 비어있지 않은 셀들의 중심 좌표를 채운다.</summary>
    public void GetOccupiedCellCenters(List<Vector3> outCenters)
    {
        outCenters.Clear();
        float half = _cellSize * 0.5f;
        foreach (var kv in _cells)
        {
            if (kv.Value.Count == 0) continue;
            Decode(kv.Key, out int x, out int y, out int z);
            outCenters.Add(new Vector3(x * _cellSize + half, y * _cellSize + half, z * _cellSize + half));
        }
    }

    private int FloorToCell(float v) => Mathf.FloorToInt(v * _invCellSize);

    private long KeyOf(Vector3 pos) => Encode(FloorToCell(pos.x), FloorToCell(pos.y), FloorToCell(pos.z));

    // 셀 좌표 3개(각 21비트, ±~1,048,575 범위)를 long 하나로 인코딩.
    private const long Mask = 0x1FFFFF; // 21비트
    private static long Encode(int x, int y, int z)
        => (x & Mask) | ((y & Mask) << 21) | ((z & Mask) << 42);

    private static void Decode(long key, out int x, out int y, out int z)
    {
        x = SignExtend(key & Mask);
        y = SignExtend((key >> 21) & Mask);
        z = SignExtend((key >> 42) & Mask);
    }

    // 21비트 값을 부호 있는 int로 복원.
    private static int SignExtend(long v)
    {
        int i = (int)v;
        if (i >= 0x100000) i -= 0x200000; // 2^20 이상이면 음수
        return i;
    }
}
