using UnityEngine;

/// <summary>
/// 5단계 — GPU Instancing 렌더링.
/// 시뮬레이션용 GameObject/Transform은 그대로 두되, 각 Boid의 MeshRenderer를 끄고
/// 같은 메시·머티리얼을 Graphics.RenderMeshInstanced로 한 번(에 가깝게) 그린다.
/// Drawcall N → ⌈N/1023⌉ 으로 줄여 CPU 렌더 오버헤드를 제거한다.
///
/// 주의: 인지·조향(sim ms)과 무관한 렌더 단계라 sim ms에는 영향이 없다. fps에서 차이가 난다.
/// </summary>
public class BoidInstancedRenderer : MonoBehaviour
{
    [Header("References")]
    public BoidsManager manager;

    [Header("Rendering")]
    public bool useGpuInstancing = true;

    private Mesh boidMesh;
    private Material boidMaterial;

    private const int MaxPerBatch = 1023;   // RenderMeshInstanced 배치당 최대 인스턴스

    private Transform[] _renderTransforms;  // 각 Boid의 메시 Transform (회전·스케일 포함)
    private MeshRenderer[] _renderers;      // 인스턴싱 시 꺼둘 개별 렌더러
    private Material _instanceMat;          // enableInstancing 사본 (원본 에셋 보존)
    private Matrix4x4[] _batch;
    private bool _ready;
    private bool _instancingActive;

    private void Awake()
    {
        if (manager == null) manager = GetComponent<BoidsManager>();
        if (manager == null) manager = FindFirstObjectByType<BoidsManager>();
    }

    // Boid 스폰 이후에야 초기화 가능하므로 LateUpdate에서 지연 초기화한다.
    private bool TryInit()
    {
        var boids = manager != null ? manager.Boids : null;
        if (boids == null || boids.Length == 0) return false;

        int n = boids.Length;
        _renderTransforms = new Transform[n];
        _renderers = new MeshRenderer[n];

        for (int i = 0; i < n; i++)
        {
            var mr = boids[i].GetComponentInChildren<MeshRenderer>();
            _renderers[i] = mr;
            _renderTransforms[i] = mr != null ? mr.transform : boids[i].transform;
        }

        if (boidMesh == null)
        {
            var mf = boids[0].GetComponentInChildren<MeshFilter>();
            if (mf != null) boidMesh = mf.sharedMesh;
        }
        if (boidMaterial == null && _renderers[0] != null)
            boidMaterial = _renderers[0].sharedMaterial;

        if (boidMesh == null || boidMaterial == null) return false;

        _instanceMat = new Material(boidMaterial) { enableInstancing = true };
        _batch = new Matrix4x4[MaxPerBatch];
        _ready = true;
        return true;
    }

    private void LateUpdate()
    {
        if (!_ready && !TryInit()) return;

        // 토글 변화 시 개별 MeshRenderer on/off (인스턴싱 켜면 끔 → 이중 렌더 방지)
        if (useGpuInstancing != _instancingActive)
        {
            foreach (var r in _renderers)
                if (r != null) r.enabled = !useGpuInstancing;
            _instancingActive = useGpuInstancing;
        }

        if (!useGpuInstancing) return;

        var rp = new RenderParams(_instanceMat);
        int total = _renderTransforms.Length;

        for (int start = 0; start < total; start += MaxPerBatch)
        {
            int count = Mathf.Min(MaxPerBatch, total - start);
            for (int k = 0; k < count; k++)
                _batch[k] = _renderTransforms[start + k].localToWorldMatrix;

            Graphics.RenderMeshInstanced(rp, boidMesh, 0, _batch, count);
        }
    }

    private void OnDestroy()
    {
        if (_instanceMat != null) Destroy(_instanceMat);
    }
}
