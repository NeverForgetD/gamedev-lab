// NativeLookupTable.cs
// LookupTable의 정적 int[] / int[,] 를 Burst Job이 읽을 수 있는
// NativeArray<int> (Persistent) 로 관리한다.
//
// ▸ 초기화 : RuntimeInitializeOnLoadMethod → 도메인 리로드 직후 자동 실행
// ▸ 해제   : Application.quitting 이벤트 → 앱 종료 시 자동 Dispose
// ▸ 직렬화 : triangleTable[256,16] → 1D 배열 (cubeIndex * 16 + slot)

using Unity.Collections;
using UnityEngine;

public static class NativeLookupTable
{
    // ── 공개 NativeArray ─────────────────────────────────────────────
    /// <summary>256 entries : edgeTable[cubeIndex]</summary>
    public static NativeArray<int> EdgeTable     { get; private set; }

    /// <summary>256×16 entries : TriTable[cubeIndex * 16 + slot] (-1 = 종료)</summary>
    public static NativeArray<int> TriangleTable { get; private set; }

    public static bool IsCreated => EdgeTable.IsCreated;

    // ── 생명주기 ─────────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        // 도메인 리로드 시 중복 할당 방지
        if (EdgeTable.IsCreated) Dispose();

        // ── edgeTable ────────────────────────────────────────────────
        EdgeTable = new NativeArray<int>(
            LookupTable.edgeTable, Allocator.Persistent);

        // ── triangleTable : 2D → 1D flatten ─────────────────────────
        // LookupTable.triangleTable[cubeIndex, slot]
        //   → TriangleTable[cubeIndex * 16 + slot]
        const int ROWS = 256, COLS = 16;
        var flatTri = new NativeArray<int>(ROWS * COLS, Allocator.Persistent);
        for (int row = 0; row < ROWS; row++)
        for (int col = 0; col < COLS; col++)
            flatTri[row * COLS + col] = LookupTable.triangleTable[row, col];

        TriangleTable = flatTri;

        // 앱 종료 시 자동 해제
        Application.quitting -= Dispose;   // 중복 등록 방지
        Application.quitting += Dispose;

        Debug.Log("[NativeLookupTable] Initialized — " +
                  $"EdgeTable:{EdgeTable.Length}, TriTable:{TriangleTable.Length}");
    }

    private static void Dispose()
    {
        if (EdgeTable.IsCreated)     EdgeTable.Dispose();
        if (TriangleTable.IsCreated) TriangleTable.Dispose();
    }

#if UNITY_EDITOR
    // 에디터에서 Play Mode 진입/종료 시 정리
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditorCallback()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            Dispose();
    }
#endif
}
