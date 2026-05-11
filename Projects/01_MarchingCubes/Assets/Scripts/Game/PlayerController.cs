using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed   = 5f;
    [SerializeField] private float rotateSpeed = 90f;
    [SerializeField] private float vertSpeed   = 3f;

    void Update()
    {
        var kb    = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        // ── 전진 / 후진 (W / S)
        float forward = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)   forward =  1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) forward = -1f;

        // ── 좌우 회전 (A / D  또는  ← →)
        float turn = 0f;
        if (kb.aKey.isPressed  || kb.leftArrowKey.isPressed)  turn = -1f;
        if (kb.dKey.isPressed  || kb.rightArrowKey.isPressed) turn =  1f;

        // ── 상승 / 하강 (마우스 좌클릭 / 우클릭)
        float vert = 0f;
        if (mouse != null)
        {
            if (mouse.leftButton.isPressed)  vert =  1f;
            if (mouse.rightButton.isPressed) vert = -1f;
        }

        // ── 회전 적용 (Y축)
        transform.Rotate(0f, turn * rotateSpeed * Time.deltaTime, 0f, Space.World);

        // ── 이동 적용
        Vector3 horizontal = transform.forward * forward;
        Vector3 vertical   = Vector3.up * vert;

        transform.position += (horizontal * moveSpeed + vertical * vertSpeed) * Time.deltaTime;
    }
}
