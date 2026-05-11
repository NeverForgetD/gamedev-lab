using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class TerrainEditor : MonoBehaviour
{
    [SerializeField] private ChunkManager chunkManager;

    [Header("Brush")]
    [SerializeField] private float brushRadius   = 3f;
    [SerializeField] private float brushStrength = 2f;
    [SerializeField] private float editRate      = 0.1f;

    [Header("Raycast")]
    [SerializeField] private Camera    cam;
    [SerializeField] private LayerMask layerMask = ~0;

    private float editTimer;

    void Update()
    {
        editTimer += Time.deltaTime;

        var mouse = Mouse.current;
        if (mouse == null) return;

        bool dig  = mouse.leftButton.isPressed;
        bool fill = mouse.rightButton.isPressed;
        if (!dig && !fill) return;
        if (editTimer < editRate) return;
        if (!TryGetHitPoint(out Vector3 hitPoint)) return;

        float delta  = brushStrength * (dig ? 1f : -1f);
        var   chunks = chunkManager.GetChunksInRadius(hitPoint, brushRadius);

        foreach (var sdf in chunks)
            sdf.ModifyDensity((float3)hitPoint, brushRadius, delta);

        editTimer = 0f;
    }

    private bool TryGetHitPoint(out Vector3 hitPoint)
    {
        hitPoint = Vector3.zero;
        if (cam == null) return false;

        var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, 1000f, layerMask))
        {
            hitPoint = hit.point;
            return true;
        }
        return false;
    }
}
