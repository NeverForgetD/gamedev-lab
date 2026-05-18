using UnityEngine;

public class PropellerRotate : MonoBehaviour
{

    [Header("회전 속도")]
    [SerializeField] private float rotateSpeed = 200f;

    [Header("회전 축")]
    [SerializeField] private Vector3 rotateAxis = Vector3.forward;

    private void Update()
    {
        transform.Rotate(rotateAxis * rotateSpeed * Time.deltaTime);
    }
}
