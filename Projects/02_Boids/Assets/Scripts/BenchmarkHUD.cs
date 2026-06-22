using UnityEngine;

/// <summary>
/// 화면 좌상단에 FPS / frame ms / sim ms / boid 수를 표시하는 벤치마크 오버레이.
/// sim ms는 BoidsManager가 Stopwatch로 잰 순수 시뮬레이션 비용(렌더/vsync 제외).
/// "Log row" 버튼을 누르면 현재 측정값을 콘솔에 한 줄(표 형식)로 로깅해 기록을 돕는다.
/// (새 Input System 전용 프로젝트라 키 입력 대신 GUI 버튼 사용)
/// </summary>
public class BenchmarkHUD : MonoBehaviour
{
    [Header("References")]
    public BoidsManager manager;

    [Header("Display")]
    public int fontSize = 22;

    // 프레임 ms / FPS 평활값 (EWMA, α=0.1)
    private float _frameMs;
    private GUIStyle _style;

    private void Awake()
    {
        if (manager == null) manager = FindFirstObjectByType<BoidsManager>();
    }

    private void Update()
    {
        float ms = Time.unscaledDeltaTime * 1000f;
        _frameMs = Mathf.Lerp(_frameMs, ms, 0.1f);
    }

    private void LogRow()
    {
        int n = manager != null ? manager.BoidCount : 0;
        float sim = manager != null ? manager.SimMs : 0f;
        float fps = _frameMs > 0f ? 1000f / _frameMs : 0f;
        string mode = manager != null ? manager.perceptionMode.ToString() : "N/A";
        Debug.Log($"[Benchmark] boids={n} | mode={mode} | sim={sim:F3} ms | frame={_frameMs:F2} ms | {fps:F1} fps");
    }

    private static readonly string[] _modeLabels =
        { "Legacy", "SinglePass", "Grid", "Jobs", "Burst", "JobsGrid" };

    private void OnGUI()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold
            };
            _style.normal.textColor = Color.white;
        }

        int n = manager != null ? manager.BoidCount : 0;
        float sim = manager != null ? manager.SimMs : 0f;
        float fps = _frameMs > 0f ? 1000f / _frameMs : 0f;
        int currentMode = manager != null ? (int)manager.perceptionMode : 0;

        const float pad = 10f;
        float lineH = fontSize + 6f;
        float bgH = lineH * 5 + 46f;
        var rect = new Rect(pad, pad, 460f, bgH);

        // 반투명 배경
        var bg = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(new Rect(rect.x - 4f, rect.y - 4f, rect.width, bgH), Texture2D.whiteTexture);
        GUI.color = bg;

        GUI.Label(rect, $"Boids : {n}", _style);
        rect.y += lineH;
        GUI.Label(rect, $"Sim   : {sim:F3} ms", _style);
        rect.y += lineH;
        GUI.Label(rect, $"Frame : {_frameMs:F2} ms", _style);
        rect.y += lineH;
        GUI.Label(rect, $"FPS   : {fps:F1}", _style);
        rect.y += lineH;
        GUI.Label(rect, $"Mode  : {_modeLabels[currentMode]}", _style);
        rect.y += lineH + 4f;

        // 화살표 버튼으로 모드 변경
        if (GUI.Button(new Rect(pad, rect.y, 34f, 28f), "<") && manager != null)
            manager.perceptionMode = (BoidsManager.PerceptionMode)((_modeLabels.Length + currentMode - 1) % _modeLabels.Length);
        if (GUI.Button(new Rect(pad + 38f, rect.y, 34f, 28f), ">") && manager != null)
            manager.perceptionMode = (BoidsManager.PerceptionMode)((currentMode + 1) % _modeLabels.Length);

        if (GUI.Button(new Rect(pad + 80f, rect.y, 120f, 28f), "Log row"))
            LogRow();
    }
}
