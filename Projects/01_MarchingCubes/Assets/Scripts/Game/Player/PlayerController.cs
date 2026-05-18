using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed   = 5f;
    [SerializeField] private float rotateSpeed = 90f;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // ── 전진 / 후진 (W / S)
        float forward = 0f;
        if (kb.wKey.isPressed) forward =  1f;
        if (kb.sKey.isPressed) forward = -1f;

        // ── 좌우 회전 (A / D  또는  ← →)
        float yaw = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  yaw = -1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) yaw =  1f;

        // ── 회전 적용
        transform.Rotate(0f, yaw * rotateSpeed * Time.deltaTime, 0f, Space.Self);

        // ── 이동 적용
        transform.position += transform.forward * forward * moveSpeed * Time.deltaTime;
    }
}
